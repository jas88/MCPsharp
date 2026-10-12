using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Analyzes migration dependencies and relationships
/// </summary>
internal sealed partial class DependencyAnalyzer
{
    private readonly ILogger<DependencyAnalyzer> _logger;

    public DependencyAnalyzer(ILogger<DependencyAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void AnalyzeDependencies(List<MigrationInfo> migrations)
    {
        // Build dependency graph based on migration timestamps and operations
        for (int i = 1; i < migrations.Count; i++)
        {
            var currentMigration = migrations[i];
            var previousMigration = migrations[i - 1];

            // Check for implicit dependencies based on table/column references
            var implicitDeps = FindImplicitDependencies(currentMigration, previousMigration);
            currentMigration.Dependencies.AddRange(implicitDeps);
        }
    }

    public MigrationDependency GetMigrationDependencies(string migrationName, List<MigrationInfo> migrations)
    {
        LogGettingDependencies(_logger, migrationName);

        var dependency = new MigrationDependency
        {
            MigrationName = migrationName,
            Type = DependencyType.Direct
        };

        try
        {
            // Find the target migration
            var targetMigration = migrations.FirstOrDefault(m => m.Name == migrationName);
            if (targetMigration == null)
            {
                LogMigrationNotFound(_logger, migrationName);
                return dependency;
            }

            // Analyze explicit dependencies from migration attributes or comments
            dependency.DependsOn = targetMigration.Dependencies;

            // Analyze implicit dependencies based on table/column references
            AnalyzeImplicitDependencies(targetMigration, migrations, dependency);

            // Build reverse dependencies
            dependency.RequiredBy = migrations
                .Where(m => m.Dependencies.Contains(migrationName) ||
                           HasImplicitDependencyOn(m, targetMigration))
                .Select(m => m.Name)
                .ToList();

            dependency.Type = dependency.DependsOn.Count != 0 ? DependencyType.Explicit : DependencyType.Implicit;
        }
        catch (Exception ex)
        {
            LogDependenciesFailed(_logger, ex, migrationName);
        }

        return dependency;
    }

    private static void AnalyzeImplicitDependencies(MigrationInfo targetMigration, List<MigrationInfo> allMigrations, MigrationDependency dependency)
    {
        // Analyze implicit dependencies based on table/column references
        foreach (var operation in targetMigration.Operations.Where(o => !string.IsNullOrEmpty(o.TableName)))
        {
            // Find previous migrations that reference this table
            var referencingMigrations = allMigrations
                .Where(m => m.CreatedAt < targetMigration.CreatedAt)
                .Where(m => m.Operations.Any(o => o.TableName == operation.TableName))
                .Select(m => m.Name)
                .ToList();

            dependency.DependsOn.AddRange(referencingMigrations);
        }

        dependency.DependsOn = dependency.DependsOn.Distinct().ToList();
    }

    private static bool HasImplicitDependencyOn(MigrationInfo migration, MigrationInfo targetMigration)
    {
        // Check if migration implicitly depends on target migration
        return migration.Operations.Any(o =>
            !string.IsNullOrEmpty(o.TableName) &&
            targetMigration.Operations.Any(to => to.TableName == o.TableName));
    }

    private static List<string> FindImplicitDependencies(MigrationInfo currentMigration, MigrationInfo previousMigration)
    {
        var dependencies = new List<string>();

        if (previousMigration != null)
        {
            // Check if current migration references tables created in previous migration
            var currentTables = currentMigration.Operations
                .Where(o => !string.IsNullOrEmpty(o.TableName))
                .Select(o => o.TableName!)
                .Distinct()
                .ToList();

            var previousTables = previousMigration.Operations
                .Where(o => o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase))
                .Select(o => o.TableName!)
                .Distinct()
                .ToList();

            dependencies.AddRange(currentTables.Intersect(previousTables));
        }

        return dependencies;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting dependencies for migration: {MigrationName}")]
    private static partial void LogGettingDependencies(ILogger logger, string migrationName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Migration not found: {MigrationName}")]
    private static partial void LogMigrationNotFound(ILogger logger, string migrationName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get dependencies for migration: {MigrationName}")]
    private static partial void LogDependenciesFailed(ILogger logger, Exception exception, string migrationName);
}

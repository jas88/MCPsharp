using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Analyzes migration dependencies and relationships
/// </summary>
internal class DependencyAnalyzer
{
    private readonly ILogger<DependencyAnalyzer> _logger;

    public DependencyAnalyzer(ILogger<DependencyAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public async Task AnalyzeDependenciesAsync(List<MigrationInfo> migrations, CancellationToken cancellationToken = default)
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
        _logger.LogInformation("Getting dependencies for migration: {MigrationName}", migrationName);

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
                _logger.LogWarning("Migration not found: {MigrationName}", migrationName);
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

            dependency.Type = dependency.DependsOn.Any() ? DependencyType.Explicit : DependencyType.Implicit;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dependencies for migration: {MigrationName}", migrationName);
        }

        return dependency;
    }

    private void AnalyzeImplicitDependencies(MigrationInfo targetMigration, List<MigrationInfo> allMigrations, MigrationDependency dependency)
    {
        // Analyze implicit dependencies based on table/column references
        foreach (var operation in targetMigration.Operations)
        {
            if (!string.IsNullOrEmpty(operation.TableName))
            {
                // Find previous migrations that reference this table
                var referencingMigrations = allMigrations
                    .Where(m => m.CreatedAt < targetMigration.CreatedAt)
                    .Where(m => m.Operations.Any(o => o.TableName == operation.TableName))
                    .Select(m => m.Name)
                    .ToList();

                dependency.DependsOn.AddRange(referencingMigrations);
            }
        }

        dependency.DependsOn = dependency.DependsOn.Distinct().ToList();
    }

    private bool HasImplicitDependencyOn(MigrationInfo migration, MigrationInfo targetMigration)
    {
        // Check if migration implicitly depends on target migration
        return migration.Operations.Any(o =>
            !string.IsNullOrEmpty(o.TableName) &&
            targetMigration.Operations.Any(to => to.TableName == o.TableName));
    }

    private List<string> FindImplicitDependencies(MigrationInfo currentMigration, MigrationInfo previousMigration)
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
}

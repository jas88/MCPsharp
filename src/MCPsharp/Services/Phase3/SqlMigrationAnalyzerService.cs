using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Comprehensive SQL migration analysis service for Entity Framework migrations
/// Phase 3 implementation
/// </summary>
public class SqlMigrationAnalyzerService : ISqlMigrationAnalyzerService
{
    private readonly ILogger<SqlMigrationAnalyzerService> _logger;
    private readonly Dictionary<DatabaseProvider, List<string>> _migrationPatterns;
    private readonly Dictionary<string, Regex> _sqlOperationPatterns;

    public SqlMigrationAnalyzerService(ILogger<SqlMigrationAnalyzerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _migrationPatterns = new Dictionary<DatabaseProvider, List<string>>
        {
            [DatabaseProvider.SqlServer] = new() { "migrationBuilder", "SqlServer", "Microsoft.EntityFrameworkCore.SqlServer" },
            [DatabaseProvider.PostgreSQL] = new() { "migrationBuilder", "Npgsql", "Npgsql.EntityFrameworkCore.PostgreSQL" },
            [DatabaseProvider.MySQL] = new() { "migrationBuilder", "MySql", "Pomelo.EntityFrameworkCore.MySql" },
            [DatabaseProvider.SQLite] = new() { "migrationBuilder", "Sqlite", "Microsoft.EntityFrameworkCore.Sqlite" }
        };

        _sqlOperationPatterns = new Dictionary<string, Regex>
        {
            ["CreateTable"] = new Regex(@"CreateTable\s*\(\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AddColumn"] = new Regex(@"AddColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropColumn"] = new Regex(@"DropColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AlterColumn"] = new Regex(@"AlterColumn\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropTable"] = new Regex(@"DropTable\s*\(\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["RenameTable"] = new Regex(@"RenameTable\s*\(\s*name:\s*[""']([^'""]+)[""']\s*,\s*newName:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["CreateIndex"] = new Regex(@"CreateIndex\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropIndex"] = new Regex(@"DropIndex\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AddForeignKey"] = new Regex(@"AddForeignKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropForeignKey"] = new Regex(@"DropForeignKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["AddPrimaryKey"] = new Regex(@"AddPrimaryKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["DropPrimaryKey"] = new Regex(@"DropPrimaryKey\s*\(\s*table:\s*[""']([^'""]+)[""']\s*,\s*name:\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            ["Sql"] = new Regex(@"Sql\s*\(\s*[""']([^'""]+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };
    }

    public async Task<MigrationAnalysisResult> AnalyzeMigrationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting migration analysis for project: {ProjectPath}", projectPath);

        var result = new MigrationAnalysisResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Detect database provider
            var provider = await DetectDatabaseProviderAsync(projectPath, cancellationToken);
            result.Summary.Provider = provider;

            // Get all migration files
            var migrationFiles = await GetMigrationFilesAsync(projectPath, cancellationToken);
            _logger.LogInformation("Found {Count} migration files", migrationFiles.Count);

            // Analyze each migration
            foreach (var file in migrationFiles)
            {
                try
                {
                    var migration = await ParseMigrationFileAsync(file, cancellationToken);
                    migration.Provider = provider;
                    result.Migrations.Add(migration);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse migration file: {File}", file);
                }
            }

            // Sort migrations by creation time
            result.Migrations = result.Migrations.OrderBy(m => m.CreatedAt).ToList();

            // Analyze dependencies
            await AnalyzeDependenciesAsync(result.Migrations, cancellationToken);

            // Detect breaking changes
            result.BreakingChanges = await DetectBreakingChangesInMigrationsAsync(result.Migrations, cancellationToken);

            // Build schema changes
            await BuildSchemaChangesAsync(result, cancellationToken);

            result.Summary.TotalMigrations = result.Migrations.Count;
            result.Summary.AppliedMigrations = result.Migrations.Count(m => m.IsApplied);
            result.Summary.PendingMigrations = result.Migrations.Count(m => !m.IsApplied);
            result.Summary.BreakingChanges = result.BreakingChanges.Count;
            result.Summary.HighRiskOperations = result.Migrations.SelectMany(m => m.Operations)
                .Count(o => o.IsBreakingChange);

            stopwatch.Stop();
            result.Summary.TotalAnalysisTime = stopwatch.Elapsed;

            _logger.LogInformation("Migration analysis completed in {Elapsed}: {TotalMigrations} migrations, {BreakingChanges} breaking changes",
                stopwatch.Elapsed, result.Summary.TotalMigrations, result.Summary.BreakingChanges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Migration analysis failed for project: {ProjectPath}", projectPath);
            throw;
        }

        return result;
    }

    public async Task<List<MigrationHistory>> GetMigrationHistoryAsync(string dbContextPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting migration history from DbContext: {DbContextPath}", dbContextPath);

        var history = new List<MigrationHistory>();

        try
        {
            // Parse the DbContext file to extract migration history information
            var dbContextCode = await File.ReadAllTextAsync(dbContextPath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(dbContextCode);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Look for OnConfiguring method or connection string configuration
            var onConfiguringMethod = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "OnConfiguring");

            if (onConfiguringMethod != null)
            {
                // Extract connection information
                var connectionInfo = ExtractConnectionInfo(onConfiguringMethod);
                if (connectionInfo != null)
                {
                    // In a real implementation, we would connect to the database and query the __EFMigrationsHistory table
                    // For now, return a placeholder implementation
                    _logger.LogInformation("Would query migration history from database: {ConnectionString}", connectionInfo);
                }
            }

            // Also look for DbContext constructor with configuration
            var constructors = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>();
            foreach (var constructor in constructors)
            {
                var configParam = constructor.ParameterList.Parameters
                    .FirstOrDefault(p => p.Type?.ToString().Contains("DbContextOptions") == true);

                if (configParam != null)
                {
                    _logger.LogDebug("DbContext configured via options pattern");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get migration history from DbContext: {DbContextPath}", dbContextPath);
        }

        return history;
    }

    public async Task<List<BreakingChange>> DetectBreakingChangesAsync(string fromMigration, string toMigration, string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Detecting breaking changes from {From} to {To}", fromMigration, toMigration);

        var breakingChanges = new List<BreakingChange>();

        try
        {
            // Get all migration files
            var migrationFiles = await GetMigrationFilesAsync(projectPath, cancellationToken);
            var migrations = new List<MigrationInfo>();

            // Parse migrations in the specified range
            var inRange = false;
            foreach (var file in migrationFiles.OrderBy(f => f))
            {
                var migration = await ParseMigrationFileAsync(file, cancellationToken);

                if (migration.Name == fromMigration)
                    inRange = true;

                if (inRange)
                    migrations.Add(migration);

                if (migration.Name == toMigration)
                    break;
            }

            // Analyze operations for breaking changes
            for (int i = 0; i < migrations.Count; i++)
            {
                var migration = migrations[i];

                foreach (var operation in migration.Operations)
                {
                    var breakingChange = AnalyzeOperationForBreakingChanges(operation, migration, i > 0 ? migrations[i-1] : null);
                    if (breakingChange != null)
                    {
                        breakingChange.FromMigration = fromMigration;
                        breakingChange.ToMigration = toMigration;
                        breakingChanges.Add(breakingChange);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect breaking changes from {From} to {To}", fromMigration, toMigration);
        }

        return breakingChanges;
    }

    public async Task<MigrationDependency> GetMigrationDependenciesAsync(string migrationName, string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting dependencies for migration: {MigrationName}", migrationName);

        var dependency = new MigrationDependency
        {
            MigrationName = migrationName,
            Type = DependencyType.Direct
        };

        try
        {
            var migrationFiles = await GetMigrationFilesAsync(projectPath, cancellationToken);
            var migrations = new List<MigrationInfo>();

            // Parse all migrations
            foreach (var file in migrationFiles)
            {
                var migration = await ParseMigrationFileAsync(file, cancellationToken);
                migrations.Add(migration);
            }

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
            await AnalyzeImplicitDependenciesAsync(targetMigration, migrations, dependency, cancellationToken);

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

    public async Task<MigrationReport> GenerateMigrationReportAsync(string projectPath, bool includeHistory = true, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating migration report for project: {ProjectPath}", projectPath);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Analyze migrations
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);

            // Track schema evolution
            var evolution = await TrackSchemaEvolutionAsync(projectPath, cancellationToken);

            // Build report
            var report = new MigrationReport
            {
                Metadata = new ReportMetadata
                {
                    ProjectPath = projectPath,
                    GeneratedAt = DateTime.UtcNow,
                    AnalyzerVersion = "1.0.0",
                    Provider = analysisResult.Summary.Provider,
                    TotalMigrations = analysisResult.Summary.TotalMigrations,
                    AnalysisTime = stopwatch.Elapsed
                },
                Migrations = analysisResult.Migrations,
                BreakingChanges = analysisResult.BreakingChanges,
                Evolution = evolution,
                RiskAssessment = BuildRiskAssessment(analysisResult),
                Recommendations = BuildRecommendations(analysisResult, evolution),
                Statistics = BuildStatistics(analysisResult, evolution)
            };

            // Include migration history if requested
            if (includeHistory)
            {
                // This would query actual database history in a real implementation
                _logger.LogDebug("Migration history inclusion requested");
            }

            stopwatch.Stop();
            _logger.LogInformation("Migration report generated in {Elapsed}", stopwatch.Elapsed);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate migration report for project: {ProjectPath}", projectPath);
            throw;
        }
    }

    public async Task<MigrationInfo> ParseMigrationFileAsync(string migrationFilePath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Parsing migration file: {FilePath}", migrationFilePath);

        if (!File.Exists(migrationFilePath))
            throw new FileNotFoundException($"Migration file not found: {migrationFilePath}");

        var migration = new MigrationInfo
        {
            FilePath = migrationFilePath,
            Name = Path.GetFileNameWithoutExtension(migrationFilePath),
            CreatedAt = File.GetCreationTime(migrationFilePath)
        };

        try
        {
            var code = await File.ReadAllTextAsync(migrationFilePath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Find the migration class
            var migrationClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.BaseList?.Types.Any(t => t.ToString().Contains("Migration")) == true);

            if (migrationClass != null)
            {
                // Extract DbContext name
                var dbContextAttr = migrationClass.AttributeLists
                    .SelectMany(al => al.Attributes)
                    .FirstOrDefault(a => a.Name.ToString().Contains("DbContext"));

                if (dbContextAttr != null)
                {
                    migration.DbContextName = ExtractStringArgument(dbContextAttr);
                }

                // Parse Up and Down methods
                var upMethod = migrationClass.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Up");

                var downMethod = migrationClass.Members
                    .OfType<MethodDeclarationSyntax>()
                    .FirstOrDefault(m => m.Identifier.Text == "Down");

                if (upMethod != null)
                {
                    migration.Operations.AddRange(ParseMigrationOperations(upMethod, OperationDirection.Up));
                }

                if (downMethod != null)
                {
                    migration.Operations.AddRange(ParseMigrationOperations(downMethod, OperationDirection.Down));
                }

                // Extract dependencies from attributes or comments
                migration.Dependencies = ExtractMigrationDependencies(migrationClass);
            }

            // Calculate checksum
            migration.Checksum = CalculateChecksum(code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse migration file: {FilePath}", migrationFilePath);
            throw;
        }

        return migration;
    }

    public async Task<SchemaEvolution> TrackSchemaEvolutionAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tracking schema evolution for project: {ProjectPath}", projectPath);

        var evolution = new SchemaEvolution();

        try
        {
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);
            var snapshots = new List<SchemaSnapshot>();
            var changes = new List<SchemaChange>();

            // Build schema snapshots for each migration
            var currentSchema = new Dictionary<string, TableDefinition>();

            foreach (var migration in analysisResult.Migrations.OrderBy(m => m.CreatedAt))
            {
                var snapshot = new SchemaSnapshot
                {
                    MigrationName = migration.Name,
                    Timestamp = migration.CreatedAt,
                    Tables = new List<TableDefinition>(currentSchema.Values.ToList())
                };

                // Apply migration operations to create new schema
                var updatedSchema = ApplyMigrationOperations(currentSchema, migration);

                // Calculate changes
                var migrationChanges = CalculateSchemaChanges(currentSchema, updatedSchema, migration);
                changes.AddRange(migrationChanges);

                // Update current schema
                currentSchema = updatedSchema;
                snapshot.Tables = new List<TableDefinition>(currentSchema.Values.ToList());

                snapshots.Add(snapshot);
            }

            evolution.Snapshots = snapshots;
            evolution.Changes = changes;
            evolution.Metrics = CalculateEvolutionMetrics(snapshots, changes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track schema evolution for project: {ProjectPath}", projectPath);
        }

        return evolution;
    }

    public async Task<List<MigrationValidationIssue>> ValidateMigrationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating migrations for project: {ProjectPath}", projectPath);

        var issues = new List<MigrationValidationIssue>();

        try
        {
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);

            // Validate each migration
            foreach (var migration in analysisResult.Migrations)
            {
                // Check for common issues
                issues.AddRange(ValidateMigrationFile(migration));
            }

            // Validate migration sequence
            issues.AddRange(ValidateMigrationSequence(analysisResult.Migrations));

            // Check for data loss risks
            issues.AddRange(ValidateDataLossRisks(analysisResult.Migrations));

            // Validate database provider consistency
            issues.AddRange(ValidateDatabaseProviderConsistency(analysisResult.Migrations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate migrations for project: {ProjectPath}", projectPath);
        }

        return issues;
    }

    public async Task<MigrationExecutionEstimate> EstimateMigrationExecutionAsync(string migrationName, string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Estimating execution for migration: {MigrationName}", migrationName);

        var estimate = new MigrationExecutionEstimate
        {
            MigrationName = migrationName
        };

        try
        {
            var migrationFiles = await GetMigrationFilesAsync(projectPath, cancellationToken);
            var migrationFile = migrationFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == migrationName);

            if (migrationFile != null)
            {
                var migration = await ParseMigrationFileAsync(migrationFile, cancellationToken);

                // Estimate based on operation types and complexity
                estimate.EstimatedExecutionTime = EstimateExecutionTime(migration.Operations);
                estimate.EstimatedDiskSpace = EstimateDiskSpace(migration.Operations);
                estimate.RequiresDowntime = RequiresDowntime(migration.Operations);
                estimate.RiskFactors = IdentifyRiskFactors(migration.Operations);
                estimate.Prerequisites = IdentifyPrerequisites(migration.Operations);
                estimate.ResourceEstimates = EstimateResources(migration.Operations);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to estimate migration execution: {MigrationName}", migrationName);
        }

        return estimate;
    }

    public async Task<List<string>> GetMigrationFilesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Searching for migration files in: {ProjectPath}", projectPath);

        var migrationFiles = new List<string>();

        try
        {
            // Common migration directory patterns
            var searchPaths = new[]
            {
                Path.Combine(projectPath, "Migrations"),
                Path.Combine(projectPath, "Data", "Migrations"),
                Path.Combine(projectPath, "Infrastructure", "Migrations"),
                Path.Combine(projectPath, "Persistence", "Migrations")
            };

            foreach (var searchPath in searchPaths)
            {
                if (Directory.Exists(searchPath))
                {
                    var files = Directory.GetFiles(searchPath, "*.cs")
                        .Where(f => !Path.GetFileName(f).StartsWith("DesignTime"))
                        .OrderBy(f => f);

                    migrationFiles.AddRange(files);
                    _logger.LogDebug("Found {Count} migration files in {Path}", files.Count(), searchPath);
                }
            }

            // Also search the entire project if no specific directories found
            if (!migrationFiles.Any())
            {
                var allCsFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => Path.GetFileNameWithoutExtension(f).Length > 15) // EF migrations have long timestamps
                    .Where(f => !Path.GetFileName(f).StartsWith("DesignTime"))
                    .Where(f => !Path.GetFileName(f).Contains("ModelSnapshot"))
                    .OrderBy(f => f);

                migrationFiles.AddRange(allCsFiles);
                _logger.LogDebug("Found {Count} potential migration files in project", allCsFiles.Count());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search for migration files in: {ProjectPath}", projectPath);
        }

        return migrationFiles.Distinct().ToList();
    }

    public async Task<DatabaseProvider> DetectDatabaseProviderAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Detecting database provider for project: {ProjectPath}", projectPath);

        try
        {
            // Check project files for provider packages
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);

            foreach (var csprojFile in csprojFiles)
            {
                var content = await File.ReadAllTextAsync(csprojFile, cancellationToken);

                foreach (var kvp in _migrationPatterns)
                {
                    if (kvp.Value.Any(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogDebug("Detected database provider: {Provider}", kvp.Key);
                        return kvp.Key;
                    }
                }
            }

            // Check source files for using statements
            var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                .Take(50); // Limit search for performance

            foreach (var csFile in csFiles)
            {
                var content = await File.ReadAllTextAsync(csFile, cancellationToken);

                foreach (var kvp in _migrationPatterns)
                {
                    if (kvp.Value.Any(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogDebug("Detected database provider: {Provider}", kvp.Key);
                        return kvp.Key;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect database provider for project: {ProjectPath}", projectPath);
        }

        _logger.LogDebug("Could not detect database provider, defaulting to Unknown");
        return DatabaseProvider.Unknown;
    }

    #region Private Helper Methods

    private async Task AnalyzeDependenciesAsync(List<MigrationInfo> migrations, CancellationToken cancellationToken)
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

    private async Task<List<BreakingChange>> DetectBreakingChangesInMigrationsAsync(List<MigrationInfo> migrations, CancellationToken cancellationToken)
    {
        var breakingChanges = new List<BreakingChange>();

        for (int i = 0; i < migrations.Count; i++)
        {
            var migration = migrations[i];

            foreach (var operation in migration.Operations)
            {
                var breakingChange = AnalyzeOperationForBreakingChanges(operation, migration, i > 0 ? migrations[i-1] : null);
                if (breakingChange != null)
                {
                    breakingChanges.Add(breakingChange);
                }
            }
        }

        return breakingChanges;
    }

    private BreakingChange? AnalyzeOperationForBreakingChanges(MigrationOperation operation, MigrationInfo migration, MigrationInfo? previousMigration)
    {
        // Analyze operation for breaking changes
        switch (operation.Type.ToLowerInvariant())
        {
            case "droptable":
                return new BreakingChange
                {
                    Type = "DropTable",
                    Severity = Severity.Critical,
                    Description = $"Table '{operation.TableName}' will be dropped, causing data loss",
                    TableName = operation.TableName,
                    FromMigration = previousMigration?.Name,
                    ToMigration = migration.Name,
                    Recommendation = "Ensure data is backed up before applying this migration"
                };

            case "dropcolumn":
                return new BreakingChange
                {
                    Type = "DropColumn",
                    Severity = Severity.High,
                    Description = $"Column '{operation.ColumnName}' in table '{operation.TableName}' will be dropped",
                    TableName = operation.TableName,
                    ColumnName = operation.ColumnName,
                    FromMigration = previousMigration?.Name,
                    ToMigration = migration.Name,
                    Recommendation = "Ensure data is migrated or backed up before dropping column"
                };

            case "altercolumn":
                // Check if the alter operation is breaking
                if (IsBreakingColumnChange(operation))
                {
                    return new BreakingChange
                    {
                        Type = "AlterColumn",
                        Severity = Severity.Medium,
                        Description = $"Column '{operation.ColumnName}' in table '{operation.TableName}' will be altered",
                        TableName = operation.TableName,
                        ColumnName = operation.ColumnName,
                        FromMigration = previousMigration?.Name,
                        ToMigration = migration.Name,
                        Recommendation = "Verify data compatibility before applying column changes"
                    };
                }
                break;

            case "rename":
            case "renametable":
                return new BreakingChange
                {
                    Type = "Rename",
                    Severity = Severity.High,
                    Description = $"Table '{operation.TableName}' will be renamed",
                    TableName = operation.TableName,
                    FromMigration = previousMigration?.Name,
                    ToMigration = migration.Name,
                    Recommendation = "Update all references to the old table name"
                };
        }

        return null;
    }

    private bool IsBreakingColumnChange(MigrationOperation operation)
    {
        // Check for breaking column changes
        if (operation.Parameters.TryGetValue("type", out var newType) ||
            operation.Parameters.TryGetValue("maxLength", out var maxLength) ||
            operation.Parameters.TryGetValue("nullable", out var nullable))
        {
            // Type changes, reducing length, or making non-nullable are potentially breaking
            return true;
        }

        return false;
    }

    private List<MigrationOperation> ParseMigrationOperations(MethodDeclarationSyntax method, OperationDirection direction)
    {
        var operations = new List<MigrationOperation>();

        // Parse migrationBuilder calls in the method body
        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var operation = ParseInvocationExpression(invocation, direction);
            if (operation != null)
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    private MigrationOperation? ParseInvocationExpression(InvocationExpressionSyntax invocation, OperationDirection direction)
    {
        // Parse migrationBuilder.CreateTable(), AddColumn(), etc.
        var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
        if (memberAccess?.Name?.Identifier.Text == null)
            return null;

        var operationType = memberAccess.Name.Identifier.Text;
        var operation = new MigrationOperation
        {
            Type = operationType,
            Direction = direction,
            IsBreakingChange = IsBreakingOperation(operationType)
        };

        // Extract parameters
        if (invocation.ArgumentList != null)
        {
            foreach (var argument in invocation.ArgumentList.Arguments)
            {
                if (argument.Expression is LiteralExpressionSyntax literal)
                {
                    // Handle literal arguments
                }
            }
        }

        // Use regex patterns to extract table/column names
        var fullText = invocation.ToFullString();

        foreach (var pattern in _sqlOperationPatterns)
        {
            var match = pattern.Value.Match(fullText);
            if (match.Success)
            {
                switch (pattern.Key)
                {
                    case "CreateTable":
                    case "DropTable":
                    case "RenameTable":
                        operation.TableName = match.Groups[1].Value;
                        break;
                    case "AddColumn":
                    case "DropColumn":
                    case "AlterColumn":
                        operation.TableName = match.Groups[1].Value;
                        operation.ColumnName = match.Groups[2].Value;
                        operation.AffectedColumns.Add(match.Groups[2].Value);
                        break;
                    case "CreateIndex":
                    case "DropIndex":
                    case "AddForeignKey":
                    case "DropForeignKey":
                    case "AddPrimaryKey":
                    case "DropPrimaryKey":
                        operation.TableName = match.Groups[1].Value;
                        break;
                    case "Sql":
                        operation.SqlStatement = match.Groups[1].Value;
                        break;
                }
                break;
            }
        }

        return operation;
    }

    private bool IsBreakingOperation(string operationType)
    {
        var breakingOperations = new[]
        {
            "DropTable", "DropColumn", "AlterColumn", "RenameTable",
            "DropIndex", "DropForeignKey", "DropPrimaryKey"
        };

        return breakingOperations.Contains(operationType, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> ExtractMigrationDependencies(ClassDeclarationSyntax migrationClass)
    {
        var dependencies = new List<string>();

        // Extract dependencies from attributes
        var attributes = migrationClass.AttributeLists
            .SelectMany(al => al.Attributes)
            .Where(a => a.Name.ToString().Contains("Migration"));

        foreach (var attr in attributes)
        {
            // Parse dependency information from attributes
            // This is a simplified implementation
        }

        return dependencies;
    }

    private string? ExtractStringArgument(AttributeSyntax attribute)
    {
        if (attribute.ArgumentList?.Arguments.Any() == true)
        {
            var firstArg = attribute.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }
        return null;
    }

    private string? ExtractConnectionInfo(MethodDeclarationSyntax onConfiguringMethod)
    {
        // Extract connection string from UseSqlServer, UseNpgsql, etc.
        var invocations = onConfiguringMethod.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess?.Name?.Identifier.Text?.StartsWith("Use") == true)
            {
                var connectionString = ExtractConnectionString(invocation);
                if (!string.IsNullOrEmpty(connectionString))
                {
                    return connectionString;
                }
            }
        }

        return null;
    }

    private string? ExtractConnectionString(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList?.Arguments.Any() == true)
        {
            var firstArg = invocation.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }
        }
        return null;
    }

    private string CalculateChecksum(string content)
    {
        // Simple checksum implementation
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private async Task AnalyzeImplicitDependenciesAsync(MigrationInfo targetMigration, List<MigrationInfo> allMigrations, MigrationDependency dependency, CancellationToken cancellationToken)
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

    private async Task BuildSchemaChangesAsync(MigrationAnalysisResult result, CancellationToken cancellationToken)
    {
        var schemaChanges = new Dictionary<string, List<SchemaChange>>();

        foreach (var migration in result.Migrations)
        {
            foreach (var operation in migration.Operations)
            {
                var change = new SchemaChange
                {
                    ChangeType = GetChangeType(operation.Type),
                    ObjectType = GetObjectType(operation.Type),
                    ObjectName = operation.TableName ?? operation.ColumnName ?? "Unknown",
                    SchemaName = operation.SchemaName,
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                };

                if (!schemaChanges.ContainsKey(migration.Name))
                    schemaChanges[migration.Name] = new List<SchemaChange>();

                schemaChanges[migration.Name].Add(change);
            }
        }

        result.SchemaChanges = schemaChanges;
    }

    private string GetChangeType(string operationType)
    {
        return operationType.ToLowerInvariant() switch
        {
            "create" or "add" => "Create",
            "drop" or "remove" => "Drop",
            "alter" or "modify" or "rename" => "Alter",
            _ => "Unknown"
        };
    }

    private string GetObjectType(string operationType)
    {
        return operationType.ToLowerInvariant() switch
        {
            var t when t.Contains("table") => "Table",
            var t when t.Contains("column") => "Column",
            var t when t.Contains("index") => "Index",
            var t when t.Contains("key") => "Constraint",
            var t when t.Contains("foreign") => "ForeignKey",
            _ => "Unknown"
        };
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

    private Dictionary<string, TableDefinition> ApplyMigrationOperations(Dictionary<string, TableDefinition> currentSchema, MigrationInfo migration)
    {
        var newSchema = new Dictionary<string, TableDefinition>(currentSchema);

        foreach (var kvp in currentSchema)
        {
            newSchema[kvp.Key] = CloneTableDefinition(kvp.Value);
        }

        // Apply migration operations to create new schema
        foreach (var operation in migration.Operations.Where(o => o.Direction == OperationDirection.Up))
        {
            switch (operation.Type.ToLowerInvariant())
            {
                case "createtable":
                    if (!string.IsNullOrEmpty(operation.TableName))
                    {
                        newSchema[operation.TableName] = new TableDefinition
                        {
                            Name = operation.TableName,
                            Schema = operation.SchemaName
                        };
                    }
                    break;
                case "droptable":
                    if (!string.IsNullOrEmpty(operation.TableName))
                    {
                        newSchema.Remove(operation.TableName);
                    }
                    break;
                // Handle other operation types...
            }
        }

        return newSchema;
    }

    private TableDefinition CloneTableDefinition(TableDefinition table)
    {
        return new TableDefinition
        {
            Name = table.Name,
            Schema = table.Schema,
            Columns = table.Columns.Select(c => new ColumnDefinition
            {
                Name = c.Name,
                DataType = c.DataType,
                IsNullable = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey,
                IsForeignKey = c.IsForeignKey,
                DefaultValue = c.DefaultValue,
                MaxLength = c.MaxLength,
                Precision = c.Precision,
                Scale = c.Scale
            }).ToList(),
            PrimaryKey = table.PrimaryKey,
            ForeignKeys = new List<string>(table.ForeignKeys),
            Indexes = new List<string>(table.Indexes)
        };
    }

    private List<SchemaChange> CalculateSchemaChanges(Dictionary<string, TableDefinition> oldSchema, Dictionary<string, TableDefinition> newSchema, MigrationInfo migration)
    {
        var changes = new List<SchemaChange>();

        // Compare schemas to find changes
        var allTables = oldSchema.Keys.Union(newSchema.Keys).ToList();

        foreach (var tableName in allTables)
        {
            var oldTable = oldSchema.GetValueOrDefault(tableName);
            var newTable = newSchema.GetValueOrDefault(tableName);

            if (oldTable == null && newTable != null)
            {
                // Table was created
                changes.Add(new SchemaChange
                {
                    ChangeType = "Create",
                    ObjectType = "Table",
                    ObjectName = tableName,
                    NewDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldTable != null && newTable == null)
            {
                // Table was dropped
                changes.Add(new SchemaChange
                {
                    ChangeType = "Drop",
                    ObjectType = "Table",
                    ObjectName = tableName,
                    OldDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldTable != null && newTable != null)
            {
                // Table was altered
                var tableChanges = CalculateTableChanges(oldTable, newTable, migration);
                changes.AddRange(tableChanges);
            }
        }

        return changes;
    }

    private List<SchemaChange> CalculateTableChanges(TableDefinition oldTable, TableDefinition newTable, MigrationInfo migration)
    {
        var changes = new List<SchemaChange>();

        // Compare columns
        var oldColumns = oldTable.Columns.ToDictionary(c => c.Name);
        var newColumns = newTable.Columns.ToDictionary(c => c.Name);

        var allColumns = oldColumns.Keys.Union(newColumns.Keys).ToList();

        foreach (var columnName in allColumns)
        {
            var oldColumn = oldColumns.GetValueOrDefault(columnName);
            var newColumn = newColumns.GetValueOrDefault(columnName);

            if (oldColumn == null && newColumn != null)
            {
                // Column was added
                changes.Add(new SchemaChange
                {
                    ChangeType = "Create",
                    ObjectType = "Column",
                    ObjectName = columnName,
                    NewDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldColumn != null && newColumn == null)
            {
                // Column was dropped
                changes.Add(new SchemaChange
                {
                    ChangeType = "Drop",
                    ObjectType = "Column",
                    ObjectName = columnName,
                    OldDefinition = new Dictionary<string, object>(),
                    MigrationName = migration.Name,
                    ChangedAt = migration.CreatedAt
                });
            }
            else if (oldColumn != null && newColumn != null)
            {
                // Column was altered
                if (!AreColumnsEqual(oldColumn, newColumn))
                {
                    changes.Add(new SchemaChange
                    {
                        ChangeType = "Alter",
                        ObjectType = "Column",
                        ObjectName = columnName,
                        OldDefinition = new Dictionary<string, object>(),
                        NewDefinition = new Dictionary<string, object>(),
                        MigrationName = migration.Name,
                        ChangedAt = migration.CreatedAt
                    });
                }
            }
        }

        return changes;
    }

    private bool AreColumnsEqual(ColumnDefinition col1, ColumnDefinition col2)
    {
        return col1.DataType == col2.DataType &&
               col1.IsNullable == col2.IsNullable &&
               col1.MaxLength == col2.MaxLength &&
               col1.Precision == col2.Precision &&
               col1.Scale == col2.Scale &&
               col1.DefaultValue == col2.DefaultValue;
    }

    private Dictionary<string, EvolutionMetric> CalculateEvolutionMetrics(List<SchemaSnapshot> snapshots, List<SchemaChange> changes)
    {
        var metrics = new Dictionary<string, EvolutionMetric>();

        if (snapshots.Count < 2)
            return metrics;

        var initialSnapshot = snapshots.First();
        var finalSnapshot = snapshots.Last();

        // Table count evolution
        metrics["TableCount"] = new EvolutionMetric
        {
            MetricName = "Table Count",
            InitialValue = initialSnapshot.Tables.Count,
            FinalValue = finalSnapshot.Tables.Count,
            ChangePercentage = finalSnapshot.Tables.Count > 0 ?
                ((double)(finalSnapshot.Tables.Count - initialSnapshot.Tables.Count) / initialSnapshot.Tables.Count) * 100 : 0,
            Trend = finalSnapshot.Tables.Count > initialSnapshot.Tables.Count ? "Increasing" :
                    finalSnapshot.Tables.Count < initialSnapshot.Tables.Count ? "Decreasing" : "Stable"
        };

        // Column count evolution
        var initialColumnCount = initialSnapshot.Tables.Sum(t => t.Columns.Count);
        var finalColumnCount = finalSnapshot.Tables.Sum(t => t.Columns.Count);

        metrics["ColumnCount"] = new EvolutionMetric
        {
            MetricName = "Column Count",
            InitialValue = initialColumnCount,
            FinalValue = finalColumnCount,
            ChangePercentage = initialColumnCount > 0 ?
                ((double)(finalColumnCount - initialColumnCount) / initialColumnCount) * 100 : 0,
            Trend = finalColumnCount > initialColumnCount ? "Increasing" :
                    finalColumnCount < initialColumnCount ? "Decreasing" : "Stable"
        };

        return metrics;
    }

    private RiskAssessment BuildRiskAssessment(MigrationAnalysisResult analysisResult)
    {
        var riskAssessment = new RiskAssessment
        {
            OverallRisk = CalculateOverallRisk(analysisResult)
        };

        foreach (var migration in analysisResult.Migrations)
        {
            var migrationRisk = new MigrationRisk
            {
                MigrationName = migration.Name,
                RiskLevel = CalculateMigrationRisk(migration),
                RiskFactors = IdentifyMigrationRiskFactors(migration),
                MitigationStrategies = GenerateMitigationStrategies(migration)
            };

            riskAssessment.MigrationRisks.Add(migrationRisk);
        }

        riskAssessment.HighRiskOperations = analysisResult.Migrations
            .SelectMany(m => m.Operations)
            .Where(o => o.IsBreakingChange)
            .Select(o => $"{o.Type} on {o.TableName}")
            .Distinct()
            .ToList();

        riskAssessment.DataLossRisks = analysisResult.Migrations
            .SelectMany(m => m.Operations)
            .Where(o => o.Type.Equals("DropTable", StringComparison.OrdinalIgnoreCase) ||
                        o.Type.Equals("DropColumn", StringComparison.OrdinalIgnoreCase))
            .Select(o => $"{o.Type} on {o.TableName}{(o.ColumnName != null ? $".{o.ColumnName}" : "")}")
            .Distinct()
            .ToList();

        return riskAssessment;
    }

    private RiskLevel CalculateOverallRisk(MigrationAnalysisResult analysisResult)
    {
        var criticalCount = analysisResult.BreakingChanges.Count(bc => bc.Severity == Severity.Critical);
        var highCount = analysisResult.BreakingChanges.Count(bc => bc.Severity == Severity.High);
        var highRiskOps = analysisResult.Migrations.SelectMany(m => m.Operations).Count(o => o.IsBreakingChange);

        if (criticalCount > 0)
            return RiskLevel.Critical;
        if (highCount > 2 || highRiskOps > 5)
            return RiskLevel.High;
        if (highCount > 0 || highRiskOps > 2)
            return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private RiskLevel CalculateMigrationRisk(MigrationInfo migration)
    {
        var criticalOps = migration.Operations.Count(o =>
            o.Type.Equals("DropTable", StringComparison.OrdinalIgnoreCase));

        var highRiskOps = migration.Operations.Count(o =>
            o.Type.Equals("DropColumn", StringComparison.OrdinalIgnoreCase) ||
            o.Type.Equals("AlterColumn", StringComparison.OrdinalIgnoreCase));

        if (criticalOps > 0)
            return RiskLevel.Critical;
        if (highRiskOps > 2)
            return RiskLevel.High;
        if (highRiskOps > 0)
            return RiskLevel.Medium;
        return RiskLevel.Low;
    }

    private List<string> IdentifyMigrationRiskFactors(MigrationInfo migration)
    {
        var riskFactors = new List<string>();

        foreach (var operation in migration.Operations)
        {
            switch (operation.Type.ToLowerInvariant())
            {
                case "droptable":
                    riskFactors.Add($"Dropping table '{operation.TableName}' will cause data loss");
                    break;
                case "dropcolumn":
                    riskFactors.Add($"Dropping column '{operation.ColumnName}' will cause data loss");
                    break;
                case "altercolumn":
                    riskFactors.Add($"Altering column '{operation.ColumnName}' may cause data conversion issues");
                    break;
                case "sql":
                    riskFactors.Add("Custom SQL operation may have unpredictable effects");
                    break;
            }
        }

        return riskFactors.Distinct().ToList();
    }

    private List<string> GenerateMitigationStrategies(MigrationInfo migration)
    {
        var strategies = new List<string>();

        foreach (var operation in migration.Operations)
        {
            switch (operation.Type.ToLowerInvariant())
            {
                case "droptable":
                case "dropcolumn":
                    strategies.Add("Create data backup before applying migration");
                    strategies.Add("Consider archiving data instead of dropping");
                    break;
                case "altercolumn":
                    strategies.Add("Test data migration on staging environment");
                    strategies.Add("Validate data conversion rules");
                    break;
            }
        }

        return strategies.Distinct().ToList();
    }

    private Recommendations BuildRecommendations(MigrationAnalysisResult analysisResult, SchemaEvolution evolution)
    {
        var recommendations = new Recommendations();

        // Best practices
        recommendations.BestPractices.Add("Use descriptive migration names");
        recommendations.BestPractices.Add("Test migrations on staging environment first");
        recommendations.BestPractices.Add("Create database backups before applying migrations");
        recommendations.BestPractices.Add("Review breaking changes before deployment");

        // Warnings based on analysis
        if (analysisResult.BreakingChanges.Any())
        {
            recommendations.Warnings.Add($"{analysisResult.BreakingChanges.Count} breaking changes detected");
        }

        if (analysisResult.Migrations.Any(m => m.Operations.Any(o => o.Type.Equals("Sql", StringComparison.OrdinalIgnoreCase))))
        {
            recommendations.Warnings.Add("Custom SQL operations detected - review carefully");
        }

        // Optimizations
        var largeMigrations = analysisResult.Migrations.Where(m => m.Operations.Count > 20);
        if (largeMigrations.Any())
        {
            recommendations.Optimizations.Add("Consider splitting large migrations into smaller ones");
        }

        // Security considerations
        recommendations.SecurityConsiderations.Add("Review migration permissions and access controls");
        recommendations.SecurityConsiderations.Add("Audit migration history for compliance");

        return recommendations;
    }

    private Dictionary<string, object> BuildStatistics(MigrationAnalysisResult analysisResult, SchemaEvolution evolution)
    {
        var stats = new Dictionary<string, object>();

        stats["TotalMigrations"] = analysisResult.Summary.TotalMigrations;
        stats["AppliedMigrations"] = analysisResult.Summary.AppliedMigrations;
        stats["PendingMigrations"] = analysisResult.Summary.PendingMigrations;
        stats["BreakingChanges"] = analysisResult.Summary.BreakingChanges;
        stats["HighRiskOperations"] = analysisResult.Summary.HighRiskOperations;
        stats["DatabaseProvider"] = analysisResult.Summary.Provider.ToString();

        // Operation type statistics
        var operationStats = analysisResult.Migrations
            .SelectMany(m => m.Operations)
            .GroupBy(o => o.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        stats["OperationStatistics"] = operationStats;

        // Timeline statistics
        if (analysisResult.Migrations.Any())
        {
            var firstMigration = analysisResult.Migrations.Min(m => m.CreatedAt);
            var lastMigration = analysisResult.Migrations.Max(m => m.CreatedAt);
            stats["MigrationSpan"] = (lastMigration - firstMigration).TotalDays;
        }

        return stats;
    }

    private List<MigrationValidationIssue> ValidateMigrationFile(MigrationInfo migration)
    {
        var issues = new List<MigrationValidationIssue>();

        // Check for empty migrations
        if (!migration.Operations.Any())
        {
            issues.Add(new MigrationValidationIssue
            {
                Type = "EmptyMigration",
                Severity = Severity.Low,
                Message = "Migration contains no operations",
                MigrationFile = migration.FilePath,
                Recommendation = "Consider removing empty migration or adding operations"
            });
        }

        // Check for missing primary keys in create table operations
        foreach (var operation in migration.Operations.Where(o => o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase)))
        {
            // This is a simplified check - in reality would need to parse the table structure
            if (!operation.Parameters.ContainsKey("primaryKey"))
            {
                issues.Add(new MigrationValidationIssue
                {
                    Type = "MissingPrimaryKey",
                    Severity = Severity.Medium,
                    Message = $"CreateTable operation for '{operation.TableName}' may be missing primary key",
                    MigrationFile = migration.FilePath,
                    Recommendation = "Ensure all tables have appropriate primary keys"
                });
            }
        }

        // Check for potentially dangerous SQL operations
        foreach (var operation in migration.Operations.Where(o => o.Type.Equals("Sql", StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.IsNullOrEmpty(operation.SqlStatement))
            {
                var sql = operation.SqlStatement.ToLowerInvariant();
                if (sql.Contains("delete") || sql.Contains("truncate") || sql.Contains("drop"))
                {
                    issues.Add(new MigrationValidationIssue
                    {
                        Type = "DangerousSql",
                        Severity = Severity.High,
                        Message = "Custom SQL contains potentially dangerous operations",
                        MigrationFile = migration.FilePath,
                        Recommendation = "Review custom SQL operations carefully"
                    });
                }
            }
        }

        return issues;
    }

    private List<MigrationValidationIssue> ValidateMigrationSequence(List<MigrationInfo> migrations)
    {
        var issues = new List<MigrationValidationIssue>();

        // Check for proper ordering
        var orderedMigrations = migrations.OrderBy(m => m.CreatedAt).ToList();
        for (int i = 1; i < orderedMigrations.Count; i++)
        {
            var current = orderedMigrations[i];
            var previous = orderedMigrations[i - 1];

            if (current.CreatedAt < previous.CreatedAt)
            {
                issues.Add(new MigrationValidationIssue
                {
                    Type = "IncorrectOrder",
                    Severity = Severity.Medium,
                    Message = $"Migration '{current.Name}' has earlier timestamp than '{previous.Name}'",
                    Recommendation = "Ensure migrations are properly ordered by timestamp"
                });
            }
        }

        // Check for duplicate migration names
        var duplicateNames = migrations.GroupBy(m => m.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicateName in duplicateNames)
        {
            issues.Add(new MigrationValidationIssue
            {
                Type = "DuplicateName",
                Severity = Severity.High,
                Message = $"Multiple migrations found with name: {duplicateName}",
                Recommendation = "Migration names must be unique"
            });
        }

        return issues;
    }

    private List<MigrationValidationIssue> ValidateDataLossRisks(List<MigrationInfo> migrations)
    {
        var issues = new List<MigrationValidationIssue>();

        foreach (var migration in migrations)
        {
            foreach (var operation in migration.Operations)
            {
                if (operation.Type.Equals("DropTable", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new MigrationValidationIssue
                    {
                        Type = "DataLossRisk",
                        Severity = Severity.Critical,
                        Message = $"Migration will drop table '{operation.TableName}' causing data loss",
                        MigrationFile = migration.FilePath,
                        Recommendation = "Ensure data is backed up or migrated before dropping tables"
                    });
                }

                if (operation.Type.Equals("DropColumn", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new MigrationValidationIssue
                    {
                        Type = "DataLossRisk",
                        Severity = Severity.High,
                        Message = $"Migration will drop column '{operation.ColumnName}' from table '{operation.TableName}' causing data loss",
                        MigrationFile = migration.FilePath,
                        Recommendation = "Ensure data is backed up or migrated before dropping columns"
                    });
                }
            }
        }

        return issues;
    }

    private List<MigrationValidationIssue> ValidateDatabaseProviderConsistency(List<MigrationInfo> migrations)
    {
        var issues = new List<MigrationValidationIssue>();

        // Check if all migrations use consistent database provider patterns
        var providers = migrations.Select(m => m.Provider).Distinct().ToList();

        if (providers.Count > 1)
        {
            issues.Add(new MigrationValidationIssue
            {
                Type = "InconsistentProvider",
                Severity = Severity.Medium,
                Message = $"Multiple database providers detected: {string.Join(", ", providers)}",
                Recommendation = "Ensure all migrations use consistent database provider"
            });
        }

        return issues;
    }

    private TimeSpan EstimateExecutionTime(List<MigrationOperation> operations)
    {
        // Simple estimation based on operation types
        var baseTime = TimeSpan.FromSeconds(1);
        var operationTime = operations.Count * TimeSpan.FromSeconds(0.5);

        var complexOperations = operations.Count(o =>
            o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase) ||
            o.Type.Equals("Sql", StringComparison.OrdinalIgnoreCase));

        var complexTime = complexOperations * TimeSpan.FromSeconds(2);

        return baseTime + operationTime + complexTime;
    }

    private long EstimateDiskSpace(List<MigrationOperation> operations)
    {
        // Simple estimation - would be more sophisticated in reality
        var createTableOps = operations.Count(o => o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase));
        var addColumnOps = operations.Count(o => o.Type.Equals("AddColumn", StringComparison.OrdinalIgnoreCase));

        return (createTableOps * 1024 * 1024) + (addColumnOps * 1024 * 100); // MB
    }

    private bool RequiresDowntime(List<MigrationOperation> operations)
    {
        // Check for operations that typically require downtime
        return operations.Any(o =>
            o.Type.Equals("DropTable", StringComparison.OrdinalIgnoreCase) ||
            o.Type.Equals("AlterColumn", StringComparison.OrdinalIgnoreCase) ||
            (o.Type.Equals("Sql", StringComparison.OrdinalIgnoreCase) &&
             !string.IsNullOrEmpty(o.SqlStatement) &&
             o.SqlStatement.ToLowerInvariant().Contains("alter")));
    }

    private List<string> IdentifyRiskFactors(List<MigrationOperation> operations)
    {
        var riskFactors = new List<string>();

        foreach (var operation in operations)
        {
            switch (operation.Type.ToLowerInvariant())
            {
                case "droptable":
                    riskFactors.Add("Table deletion - potential data loss");
                    break;
                case "dropcolumn":
                    riskFactors.Add("Column deletion - potential data loss");
                    break;
                case "altercolumn":
                    riskFactors.Add("Column alteration - potential data conversion issues");
                    break;
                case "sql":
                    riskFactors.Add("Custom SQL - unpredictable effects");
                    break;
            }
        }

        return riskFactors.Distinct().ToList();
    }

    private List<string> IdentifyPrerequisites(List<MigrationOperation> operations)
    {
        var prerequisites = new List<string>();

        if (operations.Any(o => o.Type.Equals("DropTable", StringComparison.OrdinalIgnoreCase)))
        {
            prerequisites.Add("Database backup required");
        }

        if (operations.Any(o => o.Type.Equals("Sql", StringComparison.OrdinalIgnoreCase)))
        {
            prerequisites.Add("Review custom SQL for compatibility");
        }

        return prerequisites.Distinct().ToList();
    }

    private Dictionary<string, object> EstimateResources(List<MigrationOperation> operations)
    {
        return new Dictionary<string, object>
        {
            ["EstimatedMemoryMB"] = 128,
            ["EstimatedCpuPercent"] = 25,
            ["EstimatedDuration"] = EstimateExecutionTime(operations),
            ["RequiresExclusiveLock"] = RequiresDowntime(operations)
        };
    }

    #endregion
}
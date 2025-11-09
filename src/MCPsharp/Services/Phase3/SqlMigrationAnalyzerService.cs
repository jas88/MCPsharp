using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;
using MCPsharp.Services.Phase3.SqlMigration;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Comprehensive SQL migration analysis service for Entity Framework migrations
/// Phase 3 implementation - Coordinator service
/// </summary>
public class SqlMigrationAnalyzerService : ISqlMigrationAnalyzerService
{
    private readonly ILogger<SqlMigrationAnalyzerService> _logger;
    private readonly MigrationParser _migrationParser;
    private readonly DatabaseProviderDetector _providerDetector;
    private readonly BreakingChangeAnalyzer _breakingChangeAnalyzer;
    private readonly DependencyAnalyzer _dependencyAnalyzer;
    private readonly SchemaEvolutionTracker _schemaEvolutionTracker;
    private readonly MigrationValidator _validator;
    private readonly ExecutionEstimator _estimator;
    private readonly ReportGenerator _reportGenerator;

    public SqlMigrationAnalyzerService(ILogger<SqlMigrationAnalyzerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _migrationParser = new MigrationParser(logger);
        _providerDetector = new DatabaseProviderDetector(logger);
        _breakingChangeAnalyzer = new BreakingChangeAnalyzer(logger);
        _dependencyAnalyzer = new DependencyAnalyzer(logger);
        _schemaEvolutionTracker = new SchemaEvolutionTracker(logger);
        _validator = new MigrationValidator(logger);
        _estimator = new ExecutionEstimator(logger);
        _reportGenerator = new ReportGenerator(logger);
    }

    public async Task<MigrationAnalysisResult> AnalyzeMigrationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting migration analysis for project: {ProjectPath}", projectPath);

        var result = new MigrationAnalysisResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Detect database provider
            var provider = await _providerDetector.DetectDatabaseProviderAsync(projectPath, cancellationToken);
            result.Summary.Provider = provider;

            // Get all migration files
            var migrationFiles = await _migrationParser.GetMigrationFilesAsync(projectPath, cancellationToken);
            _logger.LogInformation("Found {Count} migration files", migrationFiles.Count);

            // Analyze each migration
            foreach (var file in migrationFiles)
            {
                try
                {
                    var migration = await _migrationParser.ParseMigrationFileAsync(file, cancellationToken);
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
            await _dependencyAnalyzer.AnalyzeDependenciesAsync(result.Migrations, cancellationToken);

            // Detect breaking changes
            result.BreakingChanges = await _breakingChangeAnalyzer.DetectBreakingChangesInMigrationsAsync(result.Migrations, cancellationToken);

            // Build schema changes
            _schemaEvolutionTracker.BuildSchemaChanges(result);

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
            var migrationFiles = await _migrationParser.GetMigrationFilesAsync(projectPath, cancellationToken);
            var migrations = new List<MigrationInfo>();

            // Parse migrations in the specified range
            var inRange = false;
            foreach (var file in migrationFiles.OrderBy(f => f))
            {
                var migration = await _migrationParser.ParseMigrationFileAsync(file, cancellationToken);

                if (migration.Name == fromMigration)
                    inRange = true;

                if (inRange)
                    migrations.Add(migration);

                if (migration.Name == toMigration)
                    break;
            }

            // Analyze operations for breaking changes
            breakingChanges = _breakingChangeAnalyzer.DetectBreakingChangesInRange(migrations, fromMigration, toMigration);
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
            var migrationFiles = await _migrationParser.GetMigrationFilesAsync(projectPath, cancellationToken);
            var migrations = new List<MigrationInfo>();

            // Parse all migrations
            foreach (var file in migrationFiles)
            {
                var migration = await _migrationParser.ParseMigrationFileAsync(file, cancellationToken);
                migrations.Add(migration);
            }

            // Get dependencies
            dependency = _dependencyAnalyzer.GetMigrationDependencies(migrationName, migrations);
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
            var evolution = _schemaEvolutionTracker.TrackSchemaEvolution(analysisResult.Migrations);

            // Build report
            stopwatch.Stop();
            var report = _reportGenerator.GenerateReport(projectPath, analysisResult, evolution, stopwatch.Elapsed);

            // Include migration history if requested
            if (includeHistory)
            {
                // This would query actual database history in a real implementation
                _logger.LogDebug("Migration history inclusion requested");
            }

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
        return await _migrationParser.ParseMigrationFileAsync(migrationFilePath, cancellationToken);
    }

    public async Task<SchemaEvolution> TrackSchemaEvolutionAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Tracking schema evolution for project: {ProjectPath}", projectPath);

        try
        {
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);
            var evolution = _schemaEvolutionTracker.TrackSchemaEvolution(analysisResult.Migrations);
            return evolution;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track schema evolution for project: {ProjectPath}", projectPath);
            return new SchemaEvolution();
        }
    }

    public async Task<List<MigrationValidationIssue>> ValidateMigrationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Validating migrations for project: {ProjectPath}", projectPath);

        var issues = new List<MigrationValidationIssue>();

        try
        {
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);
            issues = _validator.ValidateMigrations(analysisResult.Migrations);
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
            var migrationFiles = await _migrationParser.GetMigrationFilesAsync(projectPath, cancellationToken);
            var migrationFile = migrationFiles.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == migrationName);

            if (migrationFile != null)
            {
                var migration = await _migrationParser.ParseMigrationFileAsync(migrationFile, cancellationToken);
                estimate = _estimator.EstimateMigrationExecution(migrationName, migration);
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
        return await _migrationParser.GetMigrationFilesAsync(projectPath, cancellationToken);
    }

    public async Task<DatabaseProvider> DetectDatabaseProviderAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        return await _providerDetector.DetectDatabaseProviderAsync(projectPath, cancellationToken);
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
}

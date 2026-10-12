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
public partial class SqlMigrationAnalyzerService : ISqlMigrationAnalyzerService
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

    public SqlMigrationAnalyzerService(ILogger<SqlMigrationAnalyzerService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _migrationParser = new MigrationParser(loggerFactory.CreateLogger<MigrationParser>());
        _providerDetector = new DatabaseProviderDetector(loggerFactory.CreateLogger<DatabaseProviderDetector>());
        _breakingChangeAnalyzer = new BreakingChangeAnalyzer(loggerFactory.CreateLogger<BreakingChangeAnalyzer>());
        _dependencyAnalyzer = new DependencyAnalyzer(loggerFactory.CreateLogger<DependencyAnalyzer>());
        _schemaEvolutionTracker = new SchemaEvolutionTracker(loggerFactory.CreateLogger<SchemaEvolutionTracker>());
        _validator = new MigrationValidator(loggerFactory.CreateLogger<MigrationValidator>());
        _estimator = new ExecutionEstimator(loggerFactory.CreateLogger<ExecutionEstimator>());
        _reportGenerator = new ReportGenerator(loggerFactory.CreateLogger<ReportGenerator>());
    }

    public async Task<MigrationAnalysisResult> AnalyzeMigrationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        LogStartingMigrationAnalysis(_logger, projectPath);

        var result = new MigrationAnalysisResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Detect database provider
            var provider = await _providerDetector.DetectDatabaseProviderAsync(projectPath, cancellationToken);
            result.Summary.Provider = provider;

            // Get all migration files
            var migrationFiles = await _migrationParser.GetMigrationFilesAsync(projectPath, cancellationToken);
            LogFoundMigrationFiles(_logger, migrationFiles.Count);

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
                    LogMigrationFileSkipped(_logger, ex, file);
                }
            }

            // Sort migrations by creation time
            result.Migrations = result.Migrations.OrderBy(m => m.CreatedAt).ToList();

            // Analyze dependencies
            DependencyAnalyzer.AnalyzeDependencies(result.Migrations);

            // Detect breaking changes
            result.BreakingChanges = BreakingChangeAnalyzer.DetectBreakingChangesInMigrations(result.Migrations);

            // Build schema changes
            SchemaEvolutionTracker.BuildSchemaChanges(result);

            result.Summary.TotalMigrations = result.Migrations.Count;
            result.Summary.AppliedMigrations = result.Migrations.Count(m => m.IsApplied);
            result.Summary.PendingMigrations = result.Migrations.Count(m => !m.IsApplied);
            result.Summary.BreakingChanges = result.BreakingChanges.Count;
            result.Summary.HighRiskOperations = result.Migrations.SelectMany(m => m.Operations)
                .Count(o => o.IsBreakingChange);

            stopwatch.Stop();
            result.Summary.TotalAnalysisTime = stopwatch.Elapsed;

            LogMigrationAnalysisCompleted(_logger, stopwatch.Elapsed, result.Summary.TotalMigrations, result.Summary.BreakingChanges);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogMigrationAnalysisFailed(_logger, ex, projectPath);
            throw new InvalidOperationException($"Migration analysis failed for project '{projectPath}': {ex.Message}", ex);
        }

        return result;
    }

    public async Task<List<MigrationHistory>> GetMigrationHistoryAsync(string dbContextPath, CancellationToken cancellationToken = default)
    {
        LogGettingMigrationHistory(_logger, dbContextPath);

        var history = new List<MigrationHistory>();

        try
        {
            // Parse the DbContext file to extract migration history information
            var dbContextCode = await File.ReadAllTextAsync(dbContextPath, cancellationToken);
            var syntaxTree = CSharpSyntaxTree.ParseText(dbContextCode, cancellationToken: cancellationToken);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Look for OnConfiguring method or connection string configuration
            var onConfiguringMethod = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m => m.Identifier.Text == "OnConfiguring");

            if (onConfiguringMethod != null)
            {
                // Extract connection information
                var connectionInfo = ExtractConnectionInfo(onConfiguringMethod);
                if (connectionInfo != null && _logger.IsEnabled(LogLevel.Debug))
                {
                    // In a real implementation, we would connect to the database and query the __EFMigrationsHistory table
                    // For now, return a placeholder implementation
                    // Only a redacted server/database description is logged, never the raw connection string
                    var connectionTarget = DescribeConnectionTarget(connectionInfo);
                    LogWouldQueryMigrationHistory(_logger, connectionTarget);
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
                    LogDbContextOptionsPattern(_logger);
                }
            }
        }
        catch (Exception ex)
        {
            LogMigrationHistoryFailed(_logger, ex, dbContextPath);
        }

        return history;
    }

    public async Task<List<BreakingChange>> DetectBreakingChangesAsync(string fromMigration, string toMigration, string projectPath, CancellationToken cancellationToken = default)
    {
        LogDetectingBreakingChanges(_logger, fromMigration, toMigration);

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
            LogBreakingChangeDetectionFailed(_logger, ex, fromMigration, toMigration);
        }

        return breakingChanges;
    }

    public async Task<MigrationDependency> GetMigrationDependenciesAsync(string migrationName, string projectPath, CancellationToken cancellationToken = default)
    {
        LogGettingDependencies(_logger, migrationName);

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
            LogDependenciesFailed(_logger, ex, migrationName);
        }

        return dependency;
    }

    public async Task<MigrationReport> GenerateMigrationReportAsync(string projectPath, bool includeHistory = true, CancellationToken cancellationToken = default)
    {
        LogGeneratingReport(_logger, projectPath);

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
                LogMigrationHistoryRequested(_logger);
            }

            LogReportGeneratedIn(_logger, stopwatch.Elapsed);

            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReportGenerationFailed(_logger, ex, projectPath);
            throw new InvalidOperationException($"Failed to generate migration report for project '{projectPath}': {ex.Message}", ex);
        }
    }

    public async Task<MigrationInfo> ParseMigrationFileAsync(string migrationFilePath, CancellationToken cancellationToken = default)
    {
        return await _migrationParser.ParseMigrationFileAsync(migrationFilePath, cancellationToken);
    }

    public async Task<SchemaEvolution> TrackSchemaEvolutionAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        LogTrackingProjectSchemaEvolution(_logger, projectPath);

        try
        {
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);
            var evolution = _schemaEvolutionTracker.TrackSchemaEvolution(analysisResult.Migrations);
            return evolution;
        }
        catch (Exception ex)
        {
            LogSchemaEvolutionFailed(_logger, ex, projectPath);
            return new SchemaEvolution();
        }
    }

    public async Task<List<MigrationValidationIssue>> ValidateMigrationsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        LogValidatingProjectMigrations(_logger, projectPath);

        var issues = new List<MigrationValidationIssue>();

        try
        {
            var analysisResult = await AnalyzeMigrationsAsync(projectPath, cancellationToken);
            issues = _validator.ValidateMigrations(analysisResult.Migrations);
        }
        catch (Exception ex)
        {
            LogMigrationValidationFailed(_logger, ex, projectPath);
        }

        return issues;
    }

    public async Task<MigrationExecutionEstimate> EstimateMigrationExecutionAsync(string migrationName, string projectPath, CancellationToken cancellationToken = default)
    {
        LogEstimatingExecution(_logger, migrationName);

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
            LogExecutionEstimateFailed(_logger, ex, migrationName);
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

    private static string? ExtractConnectionInfo(MethodDeclarationSyntax onConfiguringMethod)
    {
        // Extract connection string from UseSqlServer, UseNpgsql, etc.
        var invocations = onConfiguringMethod.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess?.Name?.Identifier.Text?.StartsWith("Use", StringComparison.Ordinal) == true)
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

    private static string? ExtractConnectionString(InvocationExpressionSyntax invocation)
    {
        if (invocation.ArgumentList?.Arguments.Any() == true)
        {
            var firstArg = invocation.ArgumentList.Arguments[0];
            if (firstArg.Expression is LiteralExpressionSyntax literal && literal.Kind() == SyntaxKind.StringLiteralExpression)
            {
                return literal.Token.ValueText;
            }
        }
        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting migration analysis for project: {ProjectPath}")]
    private static partial void LogStartingMigrationAnalysis(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} migration files")]
    private static partial void LogFoundMigrationFiles(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse migration file: {File}")]
    private static partial void LogMigrationFileSkipped(ILogger logger, Exception exception, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration analysis completed in {Elapsed}: {TotalMigrations} migrations, {BreakingChanges} breaking changes")]
    private static partial void LogMigrationAnalysisCompleted(ILogger logger, TimeSpan elapsed, int totalMigrations, int breakingChanges);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration analysis failed for project: {ProjectPath}")]
    private static partial void LogMigrationAnalysisFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting migration history from DbContext: {DbContextPath}")]
    private static partial void LogGettingMigrationHistory(ILogger logger, string dbContextPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Would query migration history from database: {ConnectionTarget}")]
    private static partial void LogWouldQueryMigrationHistory(ILogger logger, string connectionTarget);

    private static readonly string[] ServerKeys = { "Data Source", "Server", "Host", "Address", "Addr", "Network Address" };
    private static readonly string[] DatabaseKeys = { "Initial Catalog", "Database" };

    /// <summary>
    /// Produces a log-safe description of a connection string containing only the server and database
    /// names, so credentials (passwords, tokens, account keys) are never written to logs.
    /// </summary>
    public static string DescribeConnectionTarget(string connectionString)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder();
        try
        {
            builder.ConnectionString = connectionString;
        }
        catch (ArgumentException)
        {
            return "<unparseable connection string>";
        }

        var server = FindFirstValue(builder, ServerKeys) ?? "<unknown>";
        var database = FindFirstValue(builder, DatabaseKeys) ?? "<unknown>";
        return $"server={server}; database={database}";
    }

    private static string? FindFirstValue(System.Data.Common.DbConnectionStringBuilder builder, string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
            {
                return value.ToString();
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "DbContext configured via options pattern")]
    private static partial void LogDbContextOptionsPattern(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get migration history from DbContext: {DbContextPath}")]
    private static partial void LogMigrationHistoryFailed(ILogger logger, Exception exception, string dbContextPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detecting breaking changes from {From} to {To}")]
    private static partial void LogDetectingBreakingChanges(ILogger logger, string from, string to);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to detect breaking changes from {From} to {To}")]
    private static partial void LogBreakingChangeDetectionFailed(ILogger logger, Exception exception, string from, string to);

    [LoggerMessage(Level = LogLevel.Information, Message = "Getting dependencies for migration: {MigrationName}")]
    private static partial void LogGettingDependencies(ILogger logger, string migrationName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get dependencies for migration: {MigrationName}")]
    private static partial void LogDependenciesFailed(ILogger logger, Exception exception, string migrationName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating migration report for project: {ProjectPath}")]
    private static partial void LogGeneratingReport(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Migration history inclusion requested")]
    private static partial void LogMigrationHistoryRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration report generated in {Elapsed}")]
    private static partial void LogReportGeneratedIn(ILogger logger, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to generate migration report for project: {ProjectPath}")]
    private static partial void LogReportGenerationFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking schema evolution for project: {ProjectPath}")]
    private static partial void LogTrackingProjectSchemaEvolution(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to track schema evolution for project: {ProjectPath}")]
    private static partial void LogSchemaEvolutionFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Validating migrations for project: {ProjectPath}")]
    private static partial void LogValidatingProjectMigrations(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to validate migrations for project: {ProjectPath}")]
    private static partial void LogMigrationValidationFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Estimating execution for migration: {MigrationName}")]
    private static partial void LogEstimatingExecution(ILogger logger, string migrationName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to estimate migration execution: {MigrationName}")]
    private static partial void LogExecutionEstimateFailed(ILogger logger, Exception exception, string migrationName);
}

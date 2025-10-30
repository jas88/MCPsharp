using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services;

/// <summary>
/// Service for analyzing Entity Framework migrations and database schema changes
/// Interface to be implemented by Phase 3 agents
/// </summary>
public interface ISqlMigrationAnalyzerService
{
    /// <summary>
    /// Analyzes all migrations in a project
    /// </summary>
    /// <param name="projectPath">Path to the project directory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive migration analysis results</returns>
    Task<MigrationAnalysisResult> AnalyzeMigrationsAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets migration history from a database context
    /// </summary>
    /// <param name="dbContextPath">Path to the DbContext file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of migration history records</returns>
    Task<List<MigrationHistory>> GetMigrationHistoryAsync(string dbContextPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects breaking changes between two migrations
    /// </summary>
    /// <param name="fromMigration">Starting migration name</param>
    /// <param name="toMigration">Ending migration name</param>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of breaking changes</returns>
    Task<List<BreakingChange>> DetectBreakingChangesAsync(string fromMigration, string toMigration, string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets migration dependencies and dependency graph
    /// </summary>
    /// <param name="migrationName">Migration name to analyze</param>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Migration dependency information</returns>
    Task<MigrationDependency> GetMigrationDependenciesAsync(string migrationName, string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a comprehensive migration report
    /// </summary>
    /// <param name="projectPath">Path to the project directory</param>
    /// <param name="includeHistory">Whether to include migration history</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Comprehensive migration report</returns>
    Task<MigrationReport> GenerateMigrationReportAsync(string projectPath, bool includeHistory = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses a single migration file
    /// </summary>
    /// <param name="migrationFilePath">Path to the migration file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Parsed migration information</returns>
    Task<MigrationInfo> ParseMigrationFileAsync(string migrationFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks schema changes over time across all migrations
    /// </summary>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Schema evolution information</returns>
    Task<SchemaEvolution> TrackSchemaEvolutionAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates migration consistency and detects potential issues
    /// </summary>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of validation issues</returns>
    Task<List<MigrationValidationIssue>> ValidateMigrationsAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates migration execution time and resource requirements
    /// </summary>
    /// <param name="migrationName">Migration name</param>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution estimation</returns>
    Task<MigrationExecutionEstimate> EstimateMigrationExecutionAsync(string migrationName, string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all migration files in a project
    /// </summary>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of migration file paths</returns>
    Task<List<string>> GetMigrationFilesAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the database provider being used
    /// </summary>
    /// <param name="projectPath">Project path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Database provider information</returns>
    Task<DatabaseProvider> DetectDatabaseProviderAsync(string projectPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Migration validation issue
/// </summary>
public class MigrationValidationIssue
{
    public string Type { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? MigrationFile { get; set; }
    public string? DbContextFile { get; set; }
    public int? LineNumber { get; set; }
    public string? Recommendation { get; set; }
}

/// <summary>
/// Migration execution estimate
/// </summary>
public class MigrationExecutionEstimate
{
    public string MigrationName { get; set; } = string.Empty;
    public TimeSpan EstimatedExecutionTime { get; set; }
    public long EstimatedDiskSpace { get; set; }
    public bool RequiresDowntime { get; set; }
    public List<string> RiskFactors { get; set; } = new();
    public List<string> Prerequisites { get; set; } = new();
    public Dictionary<string, object> ResourceEstimates { get; set; } = new();
}
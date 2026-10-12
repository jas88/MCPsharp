using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Detects the database provider used in a project
/// </summary>
internal sealed partial class DatabaseProviderDetector
{
    private readonly ILogger<DatabaseProviderDetector> _logger;
    private readonly Dictionary<DatabaseProvider, List<string>> _migrationPatterns;

    public DatabaseProviderDetector(ILogger<DatabaseProviderDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _migrationPatterns = new Dictionary<DatabaseProvider, List<string>>
        {
            [DatabaseProvider.SqlServer] = new() { "migrationBuilder", "SqlServer", "Microsoft.EntityFrameworkCore.SqlServer" },
            [DatabaseProvider.PostgreSQL] = new() { "migrationBuilder", "Npgsql", "Npgsql.EntityFrameworkCore.PostgreSQL" },
            [DatabaseProvider.MySQL] = new() { "migrationBuilder", "MySql", "Pomelo.EntityFrameworkCore.MySql" },
            [DatabaseProvider.SQLite] = new() { "migrationBuilder", "Sqlite", "Microsoft.EntityFrameworkCore.Sqlite" }
        };
    }

    private DatabaseProvider? MatchProvider(string content) =>
        _migrationPatterns
            .Where(kvp => kvp.Value.Any(pattern => content.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
            .Select(kvp => (DatabaseProvider?)kvp.Key)
            .FirstOrDefault();

    public async Task<DatabaseProvider> DetectDatabaseProviderAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        LogDetectingProvider(_logger, projectPath);

        try
        {
            // Check project files for provider packages
            var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);

            foreach (var csprojFile in csprojFiles)
            {
                var content = await File.ReadAllTextAsync(csprojFile, cancellationToken);

                if (MatchProvider(content) is { } provider)
                {
                    LogDetectedProvider(_logger, provider);
                    return provider;
                }
            }

            // Check source files for using statements
            var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
                .Take(50); // Limit search for performance

            foreach (var csFile in csFiles)
            {
                var content = await File.ReadAllTextAsync(csFile, cancellationToken);

                if (MatchProvider(content) is { } provider)
                {
                    LogDetectedProvider(_logger, provider);
                    return provider;
                }
            }
        }
        catch (Exception ex)
        {
            LogProviderDetectionFailed(_logger, ex, projectPath);
        }

        LogProviderUnknown(_logger);
        return DatabaseProvider.Unknown;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detecting database provider for project: {ProjectPath}")]
    private static partial void LogDetectingProvider(ILogger logger, string projectPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detected database provider: {Provider}")]
    private static partial void LogDetectedProvider(ILogger logger, DatabaseProvider provider);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to detect database provider for project: {ProjectPath}")]
    private static partial void LogProviderDetectionFailed(ILogger logger, Exception exception, string projectPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not detect database provider, defaulting to Unknown")]
    private static partial void LogProviderUnknown(ILogger logger);
}

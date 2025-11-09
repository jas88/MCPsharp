using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Detects the database provider used in a project
/// </summary>
internal class DatabaseProviderDetector
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
}

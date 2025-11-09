using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Analyzes migrations to detect breaking changes
/// </summary>
internal class BreakingChangeAnalyzer
{
    private readonly ILogger<BreakingChangeAnalyzer> _logger;

    public BreakingChangeAnalyzer(ILogger<BreakingChangeAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<BreakingChange>> DetectBreakingChangesInMigrationsAsync(List<MigrationInfo> migrations, CancellationToken cancellationToken = default)
    {
        var breakingChanges = new List<BreakingChange>();

        for (int i = 0; i < migrations.Count; i++)
        {
            var migration = migrations[i];

            foreach (var operation in migration.Operations)
            {
                var breakingChange = AnalyzeOperationForBreakingChanges(operation, migration, i > 0 ? migrations[i - 1] : null);
                if (breakingChange != null)
                {
                    breakingChanges.Add(breakingChange);
                }
            }
        }

        return breakingChanges;
    }

    public List<BreakingChange> DetectBreakingChangesInRange(List<MigrationInfo> migrations, string fromMigration, string toMigration)
    {
        _logger.LogInformation("Detecting breaking changes from {From} to {To}", fromMigration, toMigration);

        var breakingChanges = new List<BreakingChange>();

        // Analyze operations for breaking changes
        for (int i = 0; i < migrations.Count; i++)
        {
            var migration = migrations[i];

            foreach (var operation in migration.Operations)
            {
                var breakingChange = AnalyzeOperationForBreakingChanges(operation, migration, i > 0 ? migrations[i - 1] : null);
                if (breakingChange != null)
                {
                    breakingChange.FromMigration = fromMigration;
                    breakingChange.ToMigration = toMigration;
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
            operation.Parameters.TryGetValue("maxLength", out var _maxLength) ||
            operation.Parameters.TryGetValue("nullable", out var _nullable))
        {
            // Type changes, reducing length, or making non-nullable are potentially breaking
            return true;
        }

        return false;
    }
}

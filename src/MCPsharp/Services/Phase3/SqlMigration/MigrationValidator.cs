using MCPsharp.Models.SqlMigration;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Validates migrations for issues and best practices
/// </summary>
internal sealed partial class MigrationValidator
{
    private readonly ILogger<MigrationValidator> _logger;

    public MigrationValidator(ILogger<MigrationValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<MigrationValidationIssue> ValidateMigrations(List<MigrationInfo> migrations)
    {
        LogValidatingMigrations(_logger, migrations.Count);

        var issues = new List<MigrationValidationIssue>();

        // Validate each migration
        foreach (var migration in migrations)
        {
            // Check for common issues
            issues.AddRange(ValidateMigrationFile(migration));
        }

        // Validate migration sequence
        issues.AddRange(ValidateMigrationSequence(migrations));

        // Check for data loss risks
        issues.AddRange(ValidateDataLossRisks(migrations));

        // Validate database provider consistency
        issues.AddRange(ValidateDatabaseProviderConsistency(migrations));

        return issues;
    }

    private static List<MigrationValidationIssue> ValidateMigrationFile(MigrationInfo migration)
    {
        var issues = new List<MigrationValidationIssue>();

        // Check for empty migrations
        if (migration.Operations.Count == 0)
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
        // This is a simplified check - in reality would need to parse the table structure
        foreach (var operation in migration.Operations.Where(o => o.Type.Equals("CreateTable", StringComparison.OrdinalIgnoreCase)
                     && !o.Parameters.ContainsKey("primaryKey")))
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

        // Check for potentially dangerous SQL operations
        var sqlStatements = migration.Operations
            .Where(o => o.Type.Equals("Sql", StringComparison.OrdinalIgnoreCase))
            .Select(o => o.SqlStatement)
            .OfType<string>()
            .Where(s => s.Length > 0);
        foreach (var statement in sqlStatements)
        {
            var sql = statement.ToLowerInvariant();
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

        return issues;
    }

    private static List<MigrationValidationIssue> ValidateMigrationSequence(List<MigrationInfo> migrations)
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

    private static List<MigrationValidationIssue> ValidateDataLossRisks(List<MigrationInfo> migrations)
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

    private static List<MigrationValidationIssue> ValidateDatabaseProviderConsistency(List<MigrationInfo> migrations)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Validating {MigrationCount} migrations")]
    private static partial void LogValidatingMigrations(ILogger logger, int migrationCount);
}

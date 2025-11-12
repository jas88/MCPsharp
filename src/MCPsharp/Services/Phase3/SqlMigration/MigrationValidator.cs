using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Validates migrations for issues and best practices
/// </summary>
internal class MigrationValidator
{
    private readonly ILogger<MigrationValidator> _logger;

    public MigrationValidator(ILogger<MigrationValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public List<MigrationValidationIssue> ValidateMigrations(List<MigrationInfo> migrations)
    {
        _logger.LogInformation("Validating {MigrationCount} migrations", migrations.Count);

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
}

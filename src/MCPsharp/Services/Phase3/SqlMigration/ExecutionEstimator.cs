using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Estimates migration execution time and resource requirements
/// </summary>
internal class ExecutionEstimator
{
    private readonly ILogger<ExecutionEstimator> _logger;

    public ExecutionEstimator(ILogger<ExecutionEstimator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MigrationExecutionEstimate EstimateMigrationExecution(string migrationName, MigrationInfo migration)
    {
        _logger.LogInformation("Estimating execution for migration: {MigrationName}", migrationName);

        var estimate = new MigrationExecutionEstimate
        {
            MigrationName = migrationName
        };

        // Estimate based on operation types and complexity
        estimate.EstimatedExecutionTime = EstimateExecutionTime(migration.Operations);
        estimate.EstimatedDiskSpace = EstimateDiskSpace(migration.Operations);
        estimate.RequiresDowntime = RequiresDowntime(migration.Operations);
        estimate.RiskFactors = IdentifyRiskFactors(migration.Operations);
        estimate.Prerequisites = IdentifyPrerequisites(migration.Operations);
        estimate.ResourceEstimates = EstimateResources(migration.Operations);

        return estimate;
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
}

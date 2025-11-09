using Microsoft.Extensions.Logging;
using MCPsharp.Models.SqlMigration;

namespace MCPsharp.Services.Phase3.SqlMigration;

/// <summary>
/// Generates reports and risk assessments for migrations
/// </summary>
internal class ReportGenerator
{
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(ILogger<ReportGenerator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public MigrationReport GenerateReport(string projectPath, MigrationAnalysisResult analysisResult, SchemaEvolution evolution, TimeSpan analysisTime)
    {
        _logger.LogInformation("Generating migration report for project: {ProjectPath}", projectPath);

        var report = new MigrationReport
        {
            Metadata = new ReportMetadata
            {
                ProjectPath = projectPath,
                GeneratedAt = DateTime.UtcNow,
                AnalyzerVersion = "1.0.0",
                Provider = analysisResult.Summary.Provider,
                TotalMigrations = analysisResult.Summary.TotalMigrations,
                AnalysisTime = analysisTime
            },
            Migrations = analysisResult.Migrations,
            BreakingChanges = analysisResult.BreakingChanges,
            Evolution = evolution,
            RiskAssessment = BuildRiskAssessment(analysisResult),
            Recommendations = BuildRecommendations(analysisResult, evolution),
            Statistics = BuildStatistics(analysisResult, evolution)
        };

        _logger.LogInformation("Migration report generated successfully");
        return report;
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
}

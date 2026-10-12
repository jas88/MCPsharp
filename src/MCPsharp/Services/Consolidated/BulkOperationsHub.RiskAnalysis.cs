using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class BulkOperationsHub
{
    private static BulkImpactAnalysis AnalyzeBulkImpact(List<BulkOperationResult> results)
    {
        return new BulkImpactAnalysis
        {
            AffectedFiles = results.Count,
            TotalChanges = results.Sum(r => r.Changes ?? 0),
            FilesWithErrors = results.Count(r => !r.Success),
            HighRiskFiles = results.Count(r => r.Changes > 100), // Arbitrary threshold
            EstimatedComplexity = results.Sum(r => r.Changes ?? 0) * 0.1 // Arbitrary complexity calculation
        };
    }

    private static BulkRiskAssessment AssessBulkRisks(List<BulkOperationResult> results)
    {
        var riskLevel = MCPsharp.Models.BulkEdit.RiskLevel.Low;
        var warnings = new List<string>();

        var errorCount = results.Count(r => !r.Success);
        var changeCount = results.Sum(r => r.Changes ?? 0);

        if (errorCount > 0)
        {
            riskLevel = MCPsharp.Models.BulkEdit.RiskLevel.High;
            warnings.Add($"{errorCount} files failed during preview");
        }
        else if (changeCount > 1000)
        {
            riskLevel = MCPsharp.Models.BulkEdit.RiskLevel.Medium;
            warnings.Add($"Large number of changes ({changeCount}) - consider breaking into smaller operations");
        }

        return new BulkRiskAssessment
        {
            RiskLevel = riskLevel,
            Warnings = warnings,
            Recommendations = GenerateRiskRecommendations(riskLevel, changeCount, errorCount)
        };
    }

    private static List<string> GenerateRiskRecommendations(MCPsharp.Models.BulkEdit.RiskLevel riskLevel, int changeCount, int errorCount)
    {
        var recommendations = new List<string>();

        if (riskLevel >= MCPsharp.Models.BulkEdit.RiskLevel.Medium)
        {
            recommendations.Add("Create a backup before proceeding");
        }

        if (changeCount > 500)
        {
            recommendations.Add("Consider breaking into smaller operations");
        }

        if (errorCount > 0)
        {
            recommendations.Add("Review and fix file access issues");
        }

        recommendations.Add("Review changes in a version control system");

        return recommendations;
    }

    private static TimeSpan EstimateExecutionTime(List<BulkOperationResult> previewResults)
    {
        // Simple estimation based on file count and changes
        var baseTime = TimeSpan.FromSeconds(1);
        var perFileTime = TimeSpan.FromMilliseconds(100);
        var perChangeTime = TimeSpan.FromMilliseconds(10);

        var totalFiles = previewResults.Count;
        var totalChanges = previewResults.Sum(r => r.Changes ?? 0);

        return baseTime + (perFileTime * totalFiles) + (perChangeTime * totalChanges);
    }
}

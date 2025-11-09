using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for priority calculation, effort estimation, and optimization planning
/// </summary>
internal class OptimizationPlanner
{
    public OptimizationPriority CalculateOptimizationPriority(ClassMetrics metrics)
    {
        if (metrics.GodClassScore.GodClassScore > 0.8) return OptimizationPriority.Critical;
        if (metrics.GodClassScore.GodClassScore > 0.6) return OptimizationPriority.High;
        if (metrics.GodClassScore.GodClassScore > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    public OptimizationPriority CalculateMethodOptimizationPriority(MethodMetrics metrics)
    {
        if (metrics.GodMethodScore.GodMethodScore > 0.8) return OptimizationPriority.Critical;
        if (metrics.GodMethodScore.GodMethodScore > 0.6) return OptimizationPriority.High;
        if (metrics.GodMethodScore.GodMethodScore > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    public OptimizationPriority CalculateFileOptimizationPriority(
        int lineCount,
        int largeClassCount,
        int largeMethodCount,
        int codeSmellCount)
    {
        var score = 0.0;
        score += lineCount / 1000.0;
        score += largeClassCount * 0.2;
        score += largeMethodCount * 0.1;
        score += codeSmellCount * 0.05;

        if (score > 1.0) return OptimizationPriority.Critical;
        if (score > 0.7) return OptimizationPriority.High;
        if (score > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    public int EstimateEffort(
        List<SplittingStrategy> splittingStrategies,
        List<RefactoringSuggestion> refactoringSuggestions)
    {
        return splittingStrategies.Sum(s => s.EstimatedEffortHours) +
               refactoringSuggestions.Sum(r => r.EstimatedEffortHours);
    }

    public int EstimateMethodEffort(List<MethodRefactoringStrategy> strategies)
    {
        return strategies.Sum(s => s.EstimatedEffortHours);
    }

    public double CalculateExpectedBenefit(ClassMetrics metrics, List<SplittingStrategy> strategies)
    {
        return strategies.Count * 0.3 + metrics.GodClassScore.GodClassScore * 0.5;
    }

    public double CalculateMethodExpectedBenefit(MethodMetrics metrics, List<MethodRefactoringStrategy> strategies)
    {
        return strategies.Count * 0.2 + metrics.GodMethodScore.GodMethodScore * 0.4;
    }

    public OptimizationPriority DetermineOverallPriority(LargeFileInfo largeFile)
    {
        if (largeFile.OptimizationPriority > 0.8) return OptimizationPriority.Critical;
        if (largeFile.OptimizationPriority > 0.6) return OptimizationPriority.High;
        if (largeFile.OptimizationPriority > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    public List<string> GeneratePrerequisites(List<OptimizationAction> actions)
    {
        var prerequisites = new List<string>();

        if (actions.Any(a => a.ActionType == OptimizationActionType.ExtractClass))
        {
            prerequisites.Add("Ensure tests exist for affected functionality");
        }

        if (actions.Any(a => a.ActionType == OptimizationActionType.ExtractClass || a.ActionType == OptimizationActionType.ExtractMethod))
        {
            prerequisites.Add("Review dependencies and access modifiers");
        }

        return prerequisites;
    }

    public List<string> GenerateRisks(List<OptimizationAction> actions)
    {
        var risks = new List<string>();

        if (actions.Any(a => a.EstimatedEffortHours > 8))
        {
            risks.Add("Large refactoring may introduce bugs");
        }

        if (actions.Any(a => a.ActionType == OptimizationActionType.ExtractClass))
        {
            risks.Add("Changes may affect dependent classes");
        }

        return risks;
    }

    public List<string> GeneratePlanRecommendations(List<OptimizationAction> actions, LargeFileInfo largeFile)
    {
        var recommendations = new List<string>();

        recommendations.Add("Start with low-effort, high-benefit actions");
        recommendations.Add("Ensure comprehensive test coverage before refactoring");
        recommendations.Add("Consider breaking down large changes into smaller increments");

        return recommendations;
    }

    public List<OptimizationRecommendation> GenerateGlobalRecommendations(List<LargeFileInfo> largeFiles, int totalFiles)
    {
        var recommendations = new List<OptimizationRecommendation>();

        if (largeFiles.Count > totalFiles * 0.1)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Title = "Improve Project Organization",
                Description = "Consider breaking down the project into smaller, more focused modules",
                Type = RecommendationType.ProjectStructure,
                Priority = OptimizationPriority.High,
                AffectedFiles = largeFiles.Select(f => f.FilePath).ToList(),
                EstimatedEffortHours = 40,
                ExpectedBenefit = 0.8
            });
        }

        return recommendations;
    }

    public OptimizationStatistics CalculateOptimizationStatistics(List<LargeFileInfo> largeFiles)
    {
        var statistics = new OptimizationStatistics
        {
            FilesNeedingOptimization = largeFiles.Count,
            ClassesNeedingSplitting = largeFiles.Sum(f => f.LargeClasses.Count),
            MethodsNeedingRefactoring = largeFiles.Sum(f => f.LargeMethods.Count),
            GodClassesDetected = 0,
            GodMethodsDetected = 0,
            AverageComplexityScore = 0,
            TotalCodeSmells = largeFiles.Sum(f => f.CodeSmells.Count),
            CodeSmellsBySeverity = new Dictionary<CodeSmellSeverity, int>(),
            FilesByPriority = new Dictionary<OptimizationPriority, int>()
        };

        // Calculate code smells by severity
        foreach (var file in largeFiles)
        {
            foreach (var smell in file.CodeSmells)
            {
                statistics.CodeSmellsBySeverity[smell.Severity] =
                    statistics.CodeSmellsBySeverity.GetValueOrDefault(smell.Severity, 0) + 1;
            }
        }

        return statistics;
    }

    public RiskAssessment AssessOptimizationRisks(List<OptimizationAction> actions)
    {
        var riskFactors = new List<RiskFactor>();
        var overallRisk = RiskLevel.Low;

        var totalEffort = actions.Sum(a => a.EstimatedEffortHours);
        var highComplexityActions = actions.Count(a => a.ExpectedBenefit > 0.8);

        if (totalEffort > 20)
        {
            riskFactors.Add(new RiskFactor
            {
                RiskType = "Large Refactoring",
                Description = "Total effort exceeds 20 hours",
                Severity = RiskLevel.Medium,
                Mitigation = "Break down into smaller, incremental changes"
            });
            overallRisk = RiskLevel.Medium;
        }

        if (highComplexityActions > 3)
        {
            riskFactors.Add(new RiskFactor
            {
                RiskType = "Multiple High-Impact Changes",
                Description = "Multiple high-benefit actions planned",
                Severity = RiskLevel.High,
                Mitigation = "Prioritize changes and implement incrementally"
            });
            overallRisk = RiskLevel.High;
        }

        return new RiskAssessment
        {
            OverallRisk = overallRisk,
            RiskFactors = riskFactors,
            MitigationStrategies = new List<string>
            {
                "Ensure comprehensive test coverage",
                "Implement changes incrementally",
                "Review changes with team before merging"
            }
        };
    }

    public List<string> GenerateEffortRecommendations(
        List<OptimizationAction> actions,
        List<string> highImpactActions,
        List<string> lowEffortHighBenefitActions)
    {
        var recommendations = new List<string>();

        if (lowEffortHighBenefitActions.Any())
        {
            recommendations.Add("Start with low-effort, high-benefit actions:");
            recommendations.AddRange(lowEffortHighBenefitActions.Select(a => $"  - {a}"));
        }

        if (highImpactActions.Any())
        {
            recommendations.Add("High-impact actions to prioritize:");
            recommendations.AddRange(highImpactActions.Select(a => $"  - {a}"));
        }

        recommendations.Add("Schedule changes to minimize disruption");
        recommendations.Add("Ensure comprehensive testing before and after each change");

        return recommendations;
    }

    public double ConvertPriorityToDouble(OptimizationPriority priority)
    {
        return priority switch
        {
            OptimizationPriority.Critical => 1.0,
            OptimizationPriority.High => 0.8,
            OptimizationPriority.Medium => 0.6,
            OptimizationPriority.Low => 0.4,
            _ => 0.2
        };
    }
}

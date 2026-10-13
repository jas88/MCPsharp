using Microsoft.CodeAnalysis;
using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Priority, effort and optimization plan construction for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    private static OptimizationEstimate EstimateOptimizationEffort(OptimizationPlan optimizationPlan)
    {
        var effortByType = new Dictionary<OptimizationActionType, int>();
        var benefits = new Dictionary<string, double>();
        var highImpactActions = new List<string>();
        var lowEffortHighBenefitActions = new List<string>();

        foreach (var action in optimizationPlan.Actions)
        {
            // Categorize effort by type
            effortByType[action.ActionType] = effortByType.GetValueOrDefault(action.ActionType, 0) + action.EstimatedEffortHours;

            // Categorize benefits
            var benefitKey = $"{action.ActionType}_{action.Priority}";
            benefits[benefitKey] = benefits.GetValueOrDefault(benefitKey, 0) + action.ExpectedBenefit;

            // Identify high impact actions
            if (action.ExpectedBenefit > 0.8 && action.Priority >= 3)
            {
                highImpactActions.Add($"{action.Title} (Benefit: {action.ExpectedBenefit:F2})");
            }

            // Identify low effort, high benefit actions
            if (action.EstimatedEffortHours <= 2 && action.ExpectedBenefit > 0.6)
            {
                lowEffortHighBenefitActions.Add($"{action.Title} ({action.EstimatedEffortHours}h, Benefit: {action.ExpectedBenefit:F2})");
            }
        }

        // Assess risks
        var riskAssessment = AssessOptimizationRisks(optimizationPlan.Actions);

        // Generate recommendations
        var recommendations = GenerateEffortRecommendations(highImpactActions, lowEffortHighBenefitActions);

        return new OptimizationEstimate
        {
            TotalEffortHours = optimizationPlan.TotalEstimatedEffortHours,
            EffortByType = effortByType,
            OverallBenefit = optimizationPlan.TotalExpectedBenefit,
            Benefits = benefits,
            HighImpactActions = highImpactActions,
            LowEffortHighBenefitActions = lowEffortHighBenefitActions,
            RiskAssessment = riskAssessment,
            Recommendations = recommendations
        };
    }

    #region Priority and Effort Calculation

    private static OptimizationPriority CalculateOptimizationPriority(ClassMetrics metrics)
    {
        if (metrics.GodClassScore.GodClassScore > 0.8) return OptimizationPriority.Critical;
        if (metrics.GodClassScore.GodClassScore > 0.6) return OptimizationPriority.High;
        if (metrics.GodClassScore.GodClassScore > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    private static OptimizationPriority CalculateMethodOptimizationPriority(MethodMetrics metrics)
    {
        if (metrics.GodMethodScore.GodMethodScore > 0.8) return OptimizationPriority.Critical;
        if (metrics.GodMethodScore.GodMethodScore > 0.6) return OptimizationPriority.High;
        if (metrics.GodMethodScore.GodMethodScore > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    private static OptimizationPriority CalculateFileOptimizationPriority(int lineCount, int largeClassCount, int largeMethodCount, int codeSmellCount)
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

    private static int EstimateEffort(List<SplittingStrategy> splittingStrategies, List<RefactoringSuggestion> refactoringSuggestions)
    {
        return splittingStrategies.Sum(s => s.EstimatedEffortHours) +
               refactoringSuggestions.Sum(r => r.EstimatedEffortHours);
    }

    private static int EstimateMethodEffort(List<MethodRefactoringStrategy> strategies)
    {
        return strategies.Sum(s => s.EstimatedEffortHours);
    }

    private static double CalculateExpectedBenefit(ClassMetrics metrics, List<SplittingStrategy> strategies)
    {
        return strategies.Count * 0.3 + metrics.GodClassScore.GodClassScore * 0.5;
    }

    private static double CalculateExpectedBenefit(double impactScore, double applicability)
    {
        // Combine impact score and applicability to get a benefit score (0.0 - 1.0)
        return (impactScore * 0.7 + applicability * 0.3);
    }

    private static double CalculateMethodExpectedBenefit(MethodMetrics metrics, List<MethodRefactoringStrategy> strategies)
    {
        return strategies.Count * 0.2 + metrics.GodMethodScore.GodMethodScore * 0.4;
    }

    #endregion

    #region Utility Methods

    private static List<string> GenerateRecommendedActions(List<SplittingStrategy> splittingStrategies, List<RefactoringSuggestion> refactoringSuggestions)
    {
        var actions = new List<string>();

        foreach (var strategy in splittingStrategies.Take(3))
        {
            actions.Add($"Consider {strategy.StrategyName}: {strategy.Description}");
        }

        foreach (var suggestion in refactoringSuggestions.Take(3))
        {
            actions.Add($"Apply {suggestion.Title}: {suggestion.Description}");
        }

        return actions;
    }

    private static List<string> GenerateImmediateActions(List<string> largeClasses, List<string> largeMethods, List<CodeSmell> codeSmells)
    {
        var actions = new List<string>();

        if (largeClasses.Count != 0)
        {
            actions.Add($"Split large classes: {string.Join(", ", largeClasses)}");
        }

        if (largeMethods.Count != 0)
        {
            actions.Add($"Refactor large methods: {string.Join(", ", largeMethods)}");
        }

        if (codeSmells.Count != 0)
        {
            actions.Add($"Address {codeSmells.Count} code smells");
        }

        return actions;
    }

    private static List<OptimizationAction> ConvertToOptimizationActions(ClassOptimizationResult classOptimization)
    {
        var actions = new List<OptimizationAction>();

        foreach (var strategy in classOptimization.SplittingStrategies)
        {
            var action = new OptimizationAction
            {
                ActionId = Guid.NewGuid().ToString(),
                Title = strategy.StrategyName,
                Description = strategy.Description,
                ActionType = OptimizationActionType.ExtractClass,
                Priority = (int)classOptimization.Priority,
                EstimatedEffortHours = strategy.EstimatedEffortHours,
                ExpectedBenefit = 0.8,
                FilePath = classOptimization.FilePath,
                StartLine = 1, // Would need actual line numbers
                EndLine = 100, // Would need actual line numbers
                Dependencies = new List<string>(),
                RefactoringSuggestions = new List<RefactoringSuggestion>(),
                IsRecommended = true
            };
            actions.Add(action);
        }

        return actions;
    }

    private static List<OptimizationAction> ConvertToOptimizationActions(MethodOptimizationResult methodOptimization)
    {
        var actions = new List<OptimizationAction>();

        foreach (var strategy in methodOptimization.RefactoringStrategies)
        {
            var action = new OptimizationAction
            {
                ActionId = Guid.NewGuid().ToString(),
                Title = strategy.StrategyName,
                Description = strategy.Description,
                ActionType = OptimizationActionType.ExtractMethod,
                Priority = (int)methodOptimization.Priority,
                EstimatedEffortHours = strategy.EstimatedEffortHours,
                ExpectedBenefit = 0.7,
                FilePath = methodOptimization.FilePath,
                StartLine = methodOptimization.StartLine,
                EndLine = methodOptimization.EndLine,
                Dependencies = new List<string>(),
                RefactoringSuggestions = new List<RefactoringSuggestion>(),
                IsRecommended = true
            };
            actions.Add(action);
        }

        return actions;
    }

    private static OptimizationAction ConvertCodeSmellToAction(CodeSmell codeSmell)
    {
        return new OptimizationAction
        {
            ActionId = Guid.NewGuid().ToString(),
            Title = $"Fix {codeSmell.SmellType}",
            Description = codeSmell.Description,
            ActionType = OptimizationActionType.ReduceComplexity,
            Priority = (int)codeSmell.Severity,
            EstimatedEffortHours = 2,
            ExpectedBenefit = codeSmell.ImpactScore,
            FilePath = codeSmell.FilePath,
            StartLine = codeSmell.StartLine,
            EndLine = codeSmell.EndLine,
            Dependencies = new List<string>(),
            RefactoringSuggestions = new List<RefactoringSuggestion>(),
            IsRecommended = codeSmell.Severity >= CodeSmellSeverity.Major
        };
    }

    private static OptimizationPriority DetermineOverallPriority(LargeFileInfo largeFile)
    {
        if (largeFile.OptimizationPriority > 0.8) return OptimizationPriority.Critical;
        if (largeFile.OptimizationPriority > 0.6) return OptimizationPriority.High;
        if (largeFile.OptimizationPriority > 0.4) return OptimizationPriority.Medium;
        return OptimizationPriority.Low;
    }

    private static List<string> GeneratePrerequisites(List<OptimizationAction> actions)
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

    private static List<string> GenerateRisks(List<OptimizationAction> actions)
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

    private static List<string> GeneratePlanRecommendations()
    {
        var recommendations = new List<string>();

        recommendations.Add("Start with low-effort, high-benefit actions");
        recommendations.Add("Ensure comprehensive test coverage before refactoring");
        recommendations.Add("Consider breaking down large changes into smaller increments");

        return recommendations;
    }

    private static List<OptimizationRecommendation> GenerateGlobalRecommendations(List<LargeFileInfo> largeFiles, int totalFiles)
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

    private static OptimizationStatistics CalculateOptimizationStatistics(List<LargeFileInfo> largeFiles)
    {
        var statistics = new OptimizationStatistics
        {
            FilesNeedingOptimization = largeFiles.Count,
            ClassesNeedingSplitting = largeFiles.Sum(f => f.LargeClasses.Count),
            MethodsNeedingRefactoring = largeFiles.Sum(f => f.LargeMethods.Count),
            GodClassesDetected = largeFiles.Sum(f => f.GodClassCount),
            GodMethodsDetected = largeFiles.Sum(f => f.GodMethodCount),
            AverageComplexityScore = largeFiles.Count == 0
                ? 0
                : largeFiles.Average(f => (double)f.OverallComplexity.CyclomaticComplexity),
            TotalCodeSmells = largeFiles.Sum(f => f.CodeSmells.Count),
            CodeSmellsBySeverity = new Dictionary<CodeSmellSeverity, int>(),
            FilesByPriority = new Dictionary<OptimizationPriority, int>()
        };

        // Calculate code smells by severity
        foreach (var file in largeFiles)
        {
            foreach (var severity in file.CodeSmells.Select(smell => smell.Severity))
            {
                statistics.CodeSmellsBySeverity[severity] =
                    statistics.CodeSmellsBySeverity.GetValueOrDefault(severity, 0) + 1;
            }
        }

        return statistics;
    }

    #endregion
}

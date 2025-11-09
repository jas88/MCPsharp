using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for converting analysis results into optimization actions
/// </summary>
internal class ActionConverter
{
    public List<OptimizationAction> ConvertToOptimizationActions(ClassOptimizationResult classOptimization)
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

    public List<OptimizationAction> ConvertToOptimizationActions(MethodOptimizationResult methodOptimization)
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
                StartLine = 1, // Would need actual line numbers
                EndLine = 50,  // Would need actual line numbers
                Dependencies = new List<string>(),
                RefactoringSuggestions = new List<RefactoringSuggestion>(),
                IsRecommended = true
            };
            actions.Add(action);
        }

        return actions;
    }

    public OptimizationAction ConvertCodeSmellToAction(CodeSmell codeSmell)
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

    public List<string> GenerateRecommendedActions(
        List<SplittingStrategy> splittingStrategies,
        List<RefactoringSuggestion> refactoringSuggestions)
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

    public List<string> GenerateImmediateActions(
        List<string> largeClasses,
        List<string> largeMethods,
        List<CodeSmell> codeSmells)
    {
        var actions = new List<string>();

        if (largeClasses.Any())
        {
            actions.Add($"Split large classes: {string.Join(", ", largeClasses)}");
        }

        if (largeMethods.Any())
        {
            actions.Add($"Refactor large methods: {string.Join(", ", largeMethods)}");
        }

        if (codeSmells.Any())
        {
            actions.Add($"Address {codeSmells.Count} code smells");
        }

        return actions;
    }
}

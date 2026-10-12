using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Splitting and refactoring strategy generation and before/after examples for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    #region Refactoring Strategy Generation

    private static List<SplittingStrategy> GenerateSplittingStrategies(ClassDeclarationSyntax classDecl, ClassMetrics metrics)
    {
        var strategies = new List<SplittingStrategy>();
        var className = classDecl.Identifier.Text;

        // Extract by responsibility
        if (metrics.Responsibilities.Count > 3)
        {
            foreach (var responsibility in metrics.Responsibilities.Take(3))
            {
                var strategy = new SplittingStrategy
                {
                    StrategyName = $"Extract {responsibility}",
                    Description = $"Extract {responsibility.ToLowerInvariant()} functionality into a separate class",
                    TargetMembers = new List<string>(),
                    NewClassName = $"{className}{responsibility.Replace(" ", "")}",
                    SplitType = SplitType.GroupByResponsibility,
                    Confidence = 0.8,
                    EstimatedEffortHours = 4,
                    Pros = new List<string> { "Single Responsibility Principle", "Better testability", "Reduced coupling" },
                    Cons = new List<string> { "Requires interface changes", "Initial complexity" }
                };
                strategies.Add(strategy);
            }
        }

        return strategies;
    }

    private static List<MethodRefactoringStrategy> GenerateMethodRefactoringStrategies(MethodMetrics metrics)
    {
        var strategies = new List<MethodRefactoringStrategy>();

        if (metrics.LineCount > GodMethodThreshold)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Extract Method",
                Description = "Extract logical sections of the method into separate methods",
                RefactoringType = MethodRefactoringType.ExtractMethod,
                TargetLines = DescribeComplexBlocks(metrics.Complexity),
                Confidence = 0.9,
                EstimatedEffortHours = 2,
                Pros = new List<string> { "Improved readability", "Better reusability", "Easier testing" },
                Cons = new List<string> { "May increase class size", "Additional method calls" }
            });
        }

        if (metrics.ParameterCount > MaxParameters)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Introduce Parameter Object",
                Description = "Group related parameters into a parameter object",
                RefactoringType = MethodRefactoringType.IntroduceParameterObject,
                TargetLines = new List<string>(),
                Confidence = 0.8,
                EstimatedEffortHours = 3,
                Pros = new List<string> { "Cleaner method signature", "Better encapsulation", "Easier maintenance" },
                Cons = new List<string> { "Requires new class", "API changes" }
            });
        }

        if (metrics.Complexity.CyclomaticComplexity > HighComplexityThreshold)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Replace Conditional with Polymorphism",
                Description = "Replace complex conditional logic with polymorphic dispatch",
                RefactoringType = MethodRefactoringType.ReplaceConditional,
                TargetLines = DescribeComplexBlocks(metrics.Complexity),
                Confidence = 0.7,
                EstimatedEffortHours = 4 + (metrics.Complexity.CyclomaticComplexity - HighComplexityThreshold) / 5,
                Pros = new List<string> { "Reduced complexity", "Better extensibility", "Cleaner code" },
                Cons = new List<string> { "Requires new classes", "More complex structure" }
            });
        }

        if (metrics.Complexity.CognitiveComplexity > CognitiveComplexityThreshold
            || metrics.Complexity.MaximumNestingDepth > MaxNestingDepthThreshold)
        {
            strategies.Add(new MethodRefactoringStrategy
            {
                StrategyName = "Simplify Nested Conditionals",
                Description = $"Flatten nesting (depth {metrics.Complexity.MaximumNestingDepth}) and reduce cognitive complexity "
                    + $"({metrics.Complexity.CognitiveComplexity}) with guard clauses and extracted helpers",
                RefactoringType = MethodRefactoringType.SimplifyConditional,
                TargetLines = DescribeComplexBlocks(metrics.Complexity),
                Confidence = 0.8,
                EstimatedEffortHours = 2 + Math.Max(0, metrics.Complexity.CognitiveComplexity - CognitiveComplexityThreshold) / 5,
                Pros = new List<string> { "Easier to read", "Fewer paths to hold in mind", "Simpler tests" },
                Cons = new List<string> { "More, smaller methods" }
            });
        }

        return strategies;
    }

    private static List<SplittingStrategy> GenerateClassSplittingStrategies(ClassDeclarationSyntax classDecl, List<string> responsibilities)
    {
        var strategies = new List<SplittingStrategy>();
        var className = classDecl.Identifier.Text;

        foreach (var responsibility in responsibilities.Take(3))
        {
            strategies.Add(new SplittingStrategy
            {
                StrategyName = $"Extract {responsibility}",
                Description = $"Extract {responsibility.ToLowerInvariant()} functionality into a separate class",
                TargetMembers = new List<string>(),
                NewClassName = $"{className}{responsibility.Replace(" ", "")}",
                SplitType = SplitType.GroupByResponsibility,
                Confidence = 0.8,
                EstimatedEffortHours = 4,
                Pros = new List<string> { "Single Responsibility Principle", "Better testability" },
                Cons = new List<string> { "Initial complexity", "Requires refactoring" }
            });
        }

        return strategies;
    }

    #endregion

    #region Code Generation Methods

    private static List<CodeExample> GenerateClassBeforeAfterExamples(List<SplittingStrategy> strategies)
    {
        var examples = new List<CodeExample>();

        foreach (var strategy in strategies.Take(2))
        {
            var example = new CodeExample
            {
                BeforeCode = "// Original large class with multiple responsibilities",
                AfterCode = $"// Refactored with {strategy.StrategyName}\n// New classes with single responsibilities",
                Explanation = strategy.Description,
                Changes = new List<string> { "Extract functionality", "Improve cohesion", "Reduce coupling" }
            };
            examples.Add(example);
        }

        return examples;
    }

    private static List<CodeExample> GenerateMethodBeforeAfterExamples(List<MethodRefactoringStrategy> strategies)
    {
        var examples = new List<CodeExample>();

        foreach (var strategy in strategies.Take(2))
        {
            var example = new CodeExample
            {
                BeforeCode = "// Original complex method",
                AfterCode = $"// Refactored with {strategy.StrategyName}\n// Simplified and more readable",
                Explanation = strategy.Description,
                Changes = new List<string> { "Extract logic", "Improve readability", "Reduce complexity" }
            };
            examples.Add(example);
        }

        return examples;
    }

    #endregion
}

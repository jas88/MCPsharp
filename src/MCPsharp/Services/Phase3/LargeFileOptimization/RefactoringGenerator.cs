using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for generating refactoring suggestions and code examples
/// </summary>
internal class RefactoringGenerator
{
    public async Task<RefactoringSuggestion?> GenerateRefactoringSuggestion(
        CodeSmell codeSmell,
        SyntaxNode root,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (codeSmell.RefactoringPatterns.Count == 0)
            return null;

        var primaryPattern = codeSmell.RefactoringPatterns.OrderByDescending(p => p.Applicability).First();

        return new RefactoringSuggestion
        {
            Title = primaryPattern.PatternName,
            Description = primaryPattern.Description,
            Type = ConvertToRefactoringType(primaryPattern.PatternName),
            FilePath = codeSmell.FilePath,
            StartLine = codeSmell.StartLine,
            EndLine = codeSmell.EndLine,
            BeforeAfter = new CodeExample
            {
                BeforeCode = codeSmell.AffectedCode,
                AfterCode = "// Refactored code would go here",
                Explanation = primaryPattern.Description,
                Changes = primaryPattern.Steps
            },
            Confidence = primaryPattern.Applicability,
            EstimatedEffortHours = primaryPattern.EstimatedEffort,
            ExpectedBenefit = CalculateExpectedBenefit(codeSmell.ImpactScore, primaryPattern.Applicability),
            Priority = CalculatePriority(codeSmell.Severity, codeSmell.ImpactScore),
            Benefits = new List<string> { "Improved code quality", "Better maintainability", "Reduced complexity" },
            Risks = new List<string> { "Potential for introducing bugs", "May require interface changes" }
        };
    }

    public async Task<List<RefactoringSuggestion>> GenerateClassRefactoringSuggestions(
        ClassDeclarationSyntax classDecl,
        SemanticModel semanticModel,
        ClassMetrics metrics,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<RefactoringSuggestion>();

        if (metrics.HasTooManyResponsibilities)
        {
            suggestions.Add(new RefactoringSuggestion
            {
                Title = "Extract Class by Responsibility",
                Description = "Split the class into smaller classes based on responsibilities",
                Type = RefactoringType.ExtractClass,
                FilePath = classDecl.SyntaxTree.FilePath,
                StartLine = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line,
                EndLine = classDecl.GetLocation().GetLineSpan().EndLinePosition.Line,
                BeforeAfter = new CodeExample
                {
                    BeforeCode = "// Original class with multiple responsibilities",
                    AfterCode = "// Split into focused classes with single responsibilities",
                    Explanation = "Applying Single Responsibility Principle",
                    Changes = new List<string> { "Identify responsibilities", "Create new classes", "Move related methods" }
                },
                Confidence = 0.85,
                EstimatedEffortHours = 6,
                ExpectedBenefit = CalculateClassExpectedBenefit(metrics),
                Priority = CalculateClassPriority(metrics),
                Benefits = new List<string> { "Single Responsibility Principle", "Better testability", "Reduced coupling" },
                Risks = new List<string> { "Requires careful dependency management" }
            });
        }

        return suggestions;
    }

    public List<CodeExample> GenerateClassBeforeAfterExamples(
        ClassDeclarationSyntax classDecl,
        List<SplittingStrategy> strategies)
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

    public List<CodeExample> GenerateMethodBeforeAfterExamples(
        MethodDeclarationSyntax methodDecl,
        List<MethodRefactoringStrategy> strategies)
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

    public RefactoringType ConvertToRefactoringType(string patternName)
    {
        return patternName.ToLower() switch
        {
            "extract method" => RefactoringType.ExtractMethod,
            "extract class" => RefactoringType.ExtractClass,
            "extract interface" => RefactoringType.ExtractInterface,
            "move method" => RefactoringType.MoveMethod,
            "move field" => RefactoringType.MoveField,
            "rename method" => RefactoringType.RenameMethod,
            "replace conditional with polymorphism" => RefactoringType.ReplaceConditionalWithPolymorphism,
            "replace magic number" => RefactoringType.ReplaceMagicNumber,
            "introduce parameter object" => RefactoringType.IntroduceParameterObject,
            _ => RefactoringType.ExtractMethod
        };
    }

    private double CalculateExpectedBenefit(double impactScore, double applicability)
    {
        // Combine impact score and applicability to get a benefit score (0.0 - 1.0)
        return (impactScore * 0.7 + applicability * 0.3);
    }

    private int CalculatePriority(CodeSmellSeverity severity, double impactScore)
    {
        // Calculate priority (1-5) based on severity and impact
        var severityWeight = severity switch
        {
            CodeSmellSeverity.Blocker => 5,
            CodeSmellSeverity.Critical => 4,
            CodeSmellSeverity.Major => 3,
            CodeSmellSeverity.Minor => 2,
            CodeSmellSeverity.Info => 1,
            _ => 1
        };

        // Adjust by impact score (0.0 - 1.0)
        var impactAdjustment = impactScore > 0.7 ? 1 : 0;
        return Math.Min(5, severityWeight + impactAdjustment);
    }

    private double CalculateClassExpectedBenefit(ClassMetrics metrics)
    {
        // Calculate benefit based on class metrics
        var benefit = 0.0;

        if (metrics.IsTooLarge)
            benefit += 0.3;

        if (metrics.HasTooManyResponsibilities)
            benefit += 0.4;

        if (metrics.GodClassScore.Severity >= GodClassSeverity.High)
            benefit += 0.3;

        return Math.Min(1.0, benefit);
    }

    private int CalculateClassPriority(ClassMetrics metrics)
    {
        // Calculate priority (1-5) based on class metrics
        var priority = 1;

        if (metrics.GodClassScore.Severity == GodClassSeverity.Critical)
            priority = 5;
        else if (metrics.GodClassScore.Severity == GodClassSeverity.High)
            priority = 4;
        else if (metrics.HasTooManyResponsibilities)
            priority = 3;
        else if (metrics.IsTooLarge)
            priority = 2;

        return priority;
    }
}

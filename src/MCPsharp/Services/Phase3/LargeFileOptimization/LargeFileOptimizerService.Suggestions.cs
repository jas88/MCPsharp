using MCPsharp.Models.LargeFileOptimization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Refactoring suggestion generation and risk assessment for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    #region Refactoring Suggestion Generation

    private static RefactoringSuggestion? GenerateRefactoringSuggestion(CodeSmell codeSmell)
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

    private static RefactoringType ConvertToRefactoringType(string patternName)
    {
        return patternName.ToLowerInvariant() switch
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

    private static List<RefactoringSuggestion> GenerateClassRefactoringSuggestions(ClassDeclarationSyntax classDecl, ClassMetrics metrics)
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

    private static int CalculatePriority(CodeSmellSeverity severity, double impactScore)
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

    private static double CalculateClassExpectedBenefit(ClassMetrics metrics)
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

    private static int CalculateClassPriority(ClassMetrics metrics)
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

    #endregion

    #region Risk Assessment

    private static RiskAssessment AssessOptimizationRisks(List<OptimizationAction> actions)
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

    private static List<string> GenerateEffortRecommendations(List<string> highImpactActions, List<string> lowEffortHighBenefitActions)
    {
        var recommendations = new List<string>();

        if (lowEffortHighBenefitActions.Count != 0)
        {
            recommendations.Add("Start with low-effort, high-benefit actions:");
            recommendations.AddRange(lowEffortHighBenefitActions.Select(a => $"  - {a}"));
        }

        if (highImpactActions.Count != 0)
        {
            recommendations.Add("High-impact actions to prioritize:");
            recommendations.AddRange(highImpactActions.Select(a => $"  - {a}"));
        }

        recommendations.Add("Schedule changes to minimize disruption");
        recommendations.Add("Ensure comprehensive testing before and after each change");

        return recommendations;
    }

    #endregion
}

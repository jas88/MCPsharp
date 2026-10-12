using MCPsharp.Models;
using MCPsharp.Services.Phase3.DuplicateDetection;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Refactoring suggestion generation, prioritisation and risk assessment
/// </summary>
public partial class DuplicateCodeDetectorService
{
    private List<RefactoringSuggestion> GenerateRefactoringSuggestions(
        List<DuplicateGroup> duplicateGroups,
        RefactoringOptions options,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<RefactoringSuggestion>();

        foreach (var group in duplicateGroups.Take(options.MaxSuggestions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var groupSuggestions = GenerateSuggestionsForDuplicateGroup(group, options);
            suggestions.AddRange(groupSuggestions);
        }

        return suggestions.OrderByDescending(s => s.Priority)
                         .ThenByDescending(s => s.EstimatedBenefit)
                         .ToList();
    }

    private List<RefactoringSuggestion> GenerateSuggestionsForDuplicateGroup(
        DuplicateGroup group,
        RefactoringOptions options)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var wantsExtractMethod = options.RefactoringTypes.HasFlag(RefactoringTypes.ExtractMethod);
        var wantsUtilityClass = options.RefactoringTypes.HasFlag(RefactoringTypes.UtilityClass);

        // Data-flow based dependency validation before suggesting any "extract shared method" refactoring
        var blockAnalyses = wantsExtractMethod || wantsUtilityClass ? AnalyzeGroupBlocks(group) : null;
        var safety = blockAnalyses != null ? ExtractionDependencyAnalyzer.AnalyzeGroup(blockAnalyses) : null;

        if (wantsExtractMethod && group.DuplicationType is DuplicateType.Method or DuplicateType.Constructor or DuplicateType.CodeBlock)
        {
            suggestions.Add(CreateExtractMethodSuggestion(group, safety));
        }

        if (wantsExtractMethod && group.DuplicationType == DuplicateType.Method)
        {
            suggestions.Add(CreateParameterizeMethodSuggestion(group, safety));
        }

        if (group.DuplicationType == DuplicateType.Class)
        {
            if (options.RefactoringTypes.HasFlag(RefactoringTypes.ExtractBaseClass))
            {
                suggestions.Add(CreateExtractBaseClassSuggestion(group));
            }

            if (options.RefactoringTypes.HasFlag(RefactoringTypes.Composition))
            {
                suggestions.Add(CreateCompositionSuggestion(group));
            }
        }

        if (wantsUtilityClass)
        {
            var staticSafety = blockAnalyses != null ? ExtractionDependencyAnalyzer.AnalyzeGroup(blockAnalyses, requireStaticTarget: true) : null;
            suggestions.Add(CreateUtilityClassSuggestion(group, staticSafety));
        }

        return suggestions;
    }

    private static RefactoringRisk AdjustRiskForSafety(RefactoringRisk risk, ExtractionSafetyResult? safety) =>
        safety is { IsSafe: false } && risk < RefactoringRisk.High ? RefactoringRisk.High : risk;

    private static List<string> ExtractionPrerequisites(ExtractionSafetyResult? safety, params string[] defaults)
    {
        if (safety == null)
        {
            return defaults.ToList();
        }

        var prerequisites = new List<string>();
        var signature = safety.Blocks.Select(b => b.SuggestedSignature).FirstOrDefault(s => s != null);
        if (signature != null)
        {
            prerequisites.Add($"Shared method signature: {signature}");
        }

        prerequisites.AddRange(safety.UnsafeReasons.Select(r => $"Resolve before extracting: {r}"));
        prerequisites.AddRange(safety.Warnings);
        return prerequisites.Count > 0 ? prerequisites : defaults.ToList();
    }

    private static RefactoringSuggestion CreateExtractMethodSuggestion(DuplicateGroup group, ExtractionSafetyResult? safety)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.ExtractMethod,
            Title = "Extract Common Method",
            Description = $"Extract the duplicate code into a shared method to eliminate {group.CodeBlocks.Count} instances of duplication.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.2, // 0.2 hours per line
            EstimatedBenefit = group.LineCount * 0.1, // 0.1 hours benefit per line
            Risk = AdjustRiskForSafety(CalculateRisk(group), safety),
            IsBreakingChange = false,
            ImplementationSteps = CreateExtractMethodSteps(group),
            Prerequisites = ExtractionPrerequisites(safety, "Identify common parameters", "Determine appropriate method name"),
            ExtractionSafety = safety,
            SideEffects = new List<string> { "May introduce method parameter complexity", "Requires updating all call sites" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "ExtractMethodPattern",
                Confidence = 0.85,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["ComplexityReduction"] = group.Complexity * 0.5,
                    ["DuplicationElimination"] = group.CodeBlocks.Count - 1
                }
            }
        };
    }

    private static RefactoringSuggestion CreateParameterizeMethodSuggestion(DuplicateGroup group, ExtractionSafetyResult? safety)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.ParameterizeMethod,
            Title = "Parameterize Similar Methods",
            Description = $"Combine {group.CodeBlocks.Count} similar methods into a single parameterized method.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.3,
            EstimatedBenefit = group.LineCount * 0.15,
            Risk = AdjustRiskForSafety(RefactoringRisk.Medium, safety),
            IsBreakingChange = true,
            ImplementationSteps = CreateParameterizeMethodSteps(group),
            Prerequisites = ExtractionPrerequisites(safety, "Analyze differences between methods", "Identify parameterization points"),
            ExtractionSafety = safety,
            SideEffects = new List<string> { "Changes method signatures", "Requires updating all call sites" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "ParameterizeMethodPattern",
                Confidence = 0.75,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["ParameterComplexity"] = EstimateParameterComplexity(group),
                    ["SignatureChanges"] = group.CodeBlocks.Count
                }
            }
        };
    }

    private static RefactoringSuggestion CreateExtractBaseClassSuggestion(DuplicateGroup group)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.ExtractBaseClass,
            Title = "Extract Base Class",
            Description = $"Extract common functionality from {group.CodeBlocks.Count} classes into a shared base class.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.4,
            EstimatedBenefit = group.LineCount * 0.2,
            Risk = RefactoringRisk.High,
            IsBreakingChange = true,
            ImplementationSteps = CreateExtractBaseClassSteps(group),
            Prerequisites = new List<string> { "Verify inheritance hierarchy", "Check for existing base classes" },
            SideEffects = new List<string> { "Changes inheritance structure", "May affect polymorphic behavior" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "ExtractBaseClassPattern",
                Confidence = 0.70,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["InheritanceDepth"] = EstimatedInheritanceDepth,
                    ["AffectedClasses"] = group.CodeBlocks.Count
                }
            }
        };
    }

    private static RefactoringSuggestion CreateCompositionSuggestion(DuplicateGroup group)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.Composition,
            Title = "Use Composition Instead of Inheritance",
            Description = $"Replace duplicate functionality with a composable component shared by {group.CodeBlocks.Count} classes.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.35,
            EstimatedBenefit = group.LineCount * 0.18,
            Risk = RefactoringRisk.Medium,
            IsBreakingChange = true,
            ImplementationSteps = CreateCompositionSteps(group),
            Prerequisites = new List<string> { "Design component interface", "Identify injection points" },
            SideEffects = new List<string> { "Requires dependency injection setup", "Changes object composition" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "CompositionPattern",
                Confidence = 0.80,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["ComponentComplexity"] = group.Complexity,
                    ["DependencyInjection"] = true
                }
            }
        };
    }

    private static RefactoringSuggestion CreateUtilityClassSuggestion(DuplicateGroup group, ExtractionSafetyResult? safety)
    {
        return new RefactoringSuggestion
        {
            SuggestionId = Guid.NewGuid().ToString(),
            RefactoringType = RefactoringType.UtilityClass,
            Title = "Extract Utility Class",
            Description = $"Create a utility class to contain common functionality used by {group.CodeBlocks.Count} locations.",
            DuplicateGroupIds = new List<string> { group.GroupId },
            Priority = CalculatePriority(group),
            EstimatedEffort = group.LineCount * 0.25,
            EstimatedBenefit = group.LineCount * 0.12,
            Risk = AdjustRiskForSafety(RefactoringRisk.Low, safety),
            IsBreakingChange = false,
            ImplementationSteps = CreateUtilityClassSteps(group),
            Prerequisites = ExtractionPrerequisites(safety, "Determine utility class name and namespace"),
            ExtractionSafety = safety,
            SideEffects = new List<string> { "Introduces new class", "May affect encapsulation" },
            Metadata = new RefactoringMetadata
            {
                GeneratedAt = DateTime.UtcNow,
                GenerationAlgorithm = "UtilityClassPattern",
                Confidence = 0.90,
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["MethodCount"] = group.CodeBlocks.Count,
                    ["StaticMembers"] = true
                }
            }
        };
    }

    private static RefactoringPriority CalculatePriority(DuplicateGroup group)
    {
        var priorityScore = 0;

        // Higher priority for more instances
        priorityScore += Math.Min(group.CodeBlocks.Count * 10, 50);

        // Higher priority for larger blocks
        priorityScore += Math.Min(group.LineCount * 2, 30);

        // Higher priority for higher complexity
        priorityScore += Math.Min(group.Complexity * 5, 20);

        return priorityScore switch
        {
            >= 80 => RefactoringPriority.Critical,
            >= 60 => RefactoringPriority.High,
            >= 40 => RefactoringPriority.Medium,
            _ => RefactoringPriority.Low
        };
    }

    private static RefactoringRisk CalculateRisk(DuplicateGroup group)
    {
        var riskScore = 0;

        // Higher risk for larger groups
        if (group.CodeBlocks.Count > 5)
            riskScore += 20;

        // Higher risk for public accessibility
        if (group.CodeBlocks.Any(cb => cb.Accessibility == MCPsharp.Models.Accessibility.Public))
            riskScore += 15;

        // Higher risk for high complexity
        if (group.Complexity > 10)
            riskScore += 15;

        // Higher risk for distributed across many files
        var fileCount = group.CodeBlocks.Select(cb => cb.FilePath).Distinct().Count();
        if (fileCount > 3)
            riskScore += 10;

        return riskScore switch
        {
            >= 40 => RefactoringRisk.VeryHigh,
            >= 30 => RefactoringRisk.High,
            >= 20 => RefactoringRisk.Medium,
            _ => RefactoringRisk.Low
        };
    }
}

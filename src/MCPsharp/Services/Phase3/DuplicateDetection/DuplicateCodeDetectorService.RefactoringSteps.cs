using Microsoft.CodeAnalysis;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Step-by-step plans for each refactoring suggestion type
/// </summary>
public partial class DuplicateCodeDetectorService
{
    private static List<RefactoringStep> CreateExtractMethodSteps(DuplicateGroup group)
    {
        var steps = new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create new shared method with appropriate signature",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate class/file" },
                EstimatedMinutes = 15,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common logic to new method",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Update all call sites to use new method",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 5,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };

        return steps;
    }

    private static List<RefactoringStep> CreateParameterizeMethodSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Analyze differences between similar methods",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = 20,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Design parameterized method signature",
                OperationType = RefactoringOperationType.ChangeSignature,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = 15,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Implement parameterized method",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 3,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            },
            new RefactoringStep
            {
                Description = "Replace old methods with calls to parameterized method",
                OperationType = RefactoringOperationType.InlineCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 8,
                IsOptional = false,
                Dependencies = new List<int> { 2 }
            }
        };
    }

    private static List<RefactoringStep> CreateExtractBaseClassSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create base class with common functionality",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate namespace" },
                EstimatedMinutes = 30,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common methods to base class",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Update derived classes to inherit from base class",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 10,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };
    }

    private static List<RefactoringStep> CreateCompositionSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create component interface and implementation",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate namespace" },
                EstimatedMinutes = 25,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common functionality to component",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Add component injection to consuming classes",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 8,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };
    }

    private static List<RefactoringStep> CreateUtilityClassSteps(DuplicateGroup group)
    {
        return new List<RefactoringStep>
        {
            new RefactoringStep
            {
                Description = "Create utility class with static methods",
                OperationType = RefactoringOperationType.CreateFile,
                TargetFiles = new List<string> { "Determine appropriate namespace" },
                EstimatedMinutes = 20,
                IsOptional = false,
                Dependencies = new List<int>()
            },
            new RefactoringStep
            {
                Description = "Move common logic to utility methods",
                OperationType = RefactoringOperationType.ExtractCode,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.LineCount * 2,
                IsOptional = false,
                Dependencies = new List<int> { 0 }
            },
            new RefactoringStep
            {
                Description = "Replace duplicate code with utility method calls",
                OperationType = RefactoringOperationType.ModifyFile,
                TargetFiles = group.CodeBlocks.Select(cb => cb.FilePath).ToList(),
                EstimatedMinutes = group.CodeBlocks.Count * 5,
                IsOptional = false,
                Dependencies = new List<int> { 1 }
            }
        };
    }
}

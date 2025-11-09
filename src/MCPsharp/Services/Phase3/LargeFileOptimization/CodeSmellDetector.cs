using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for detecting various code smell anti-patterns
/// </summary>
internal class CodeSmellDetector
{
    private const int MaxParameters = 7;
    private const int GodMethodThreshold = 50;
    private const int GodClassThreshold = 20;

    public async Task<List<CodeSmell>> AnalyzeCodeSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();

        // Analyze different types of code smells
        codeSmells.AddRange(await DetectLongParameterListSmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectLongMethodSmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectLargeClassSmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectComplexConditionalSmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectDuplicateCodeSmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectMagicNumberSmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectFeatureEnvySmells(root, semanticModel, filePath, cancellationToken));
        codeSmells.AddRange(await DetectDataClumpsSmells(root, semanticModel, filePath, cancellationToken));

        return codeSmells.OrderByDescending(cs => cs.ImpactScore).ToList();
    }

    private async Task<List<CodeSmell>> DetectLongParameterListSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();
        var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methodDeclarations)
        {
            if (method.ParameterList.Parameters.Count > MaxParameters)
            {
                var codeSmell = new CodeSmell
                {
                    SmellType = "Long Parameter List",
                    Description = $"Method has {method.ParameterList.Parameters.Count} parameters (threshold: {MaxParameters})",
                    Severity = DetermineCodeSmellSeverity(method.ParameterList.Parameters.Count - MaxParameters, MaxParameters),
                    FilePath = filePath,
                    StartLine = method.GetLocation().GetLineSpan().StartLinePosition.Line,
                    EndLine = method.GetLocation().GetLineSpan().EndLinePosition.Line,
                    AffectedCode = method.GetText().ToString(),
                    Suggestion = "Consider using a parameter object or reducing the number of parameters",
                    ImpactScore = Math.Min(1.0, (method.ParameterList.Parameters.Count - MaxParameters) / (double)MaxParameters),
                    RefactoringPatterns = new List<RefactoringPattern>
                    {
                        new RefactoringPattern
                        {
                            PatternName = "Introduce Parameter Object",
                            Description = "Group related parameters into a parameter object",
                            Steps = new List<string> { "Create parameter object class", "Replace parameters with object", "Update method calls" },
                            Applicability = 0.9,
                            EstimatedEffort = 3
                        }
                    }
                };

                codeSmells.Add(codeSmell);
            }
        }

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectLongMethodSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();
        var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methodDeclarations)
        {
            var lineCount = method.GetText().Lines.Count;
            if (lineCount > GodMethodThreshold)
            {
                var codeSmell = new CodeSmell
                {
                    SmellType = "Long Method",
                    Description = $"Method has {lineCount} lines (threshold: {GodMethodThreshold})",
                    Severity = DetermineCodeSmellSeverity(lineCount - GodMethodThreshold, GodMethodThreshold),
                    FilePath = filePath,
                    StartLine = method.GetLocation().GetLineSpan().StartLinePosition.Line,
                    EndLine = method.GetLocation().GetLineSpan().EndLinePosition.Line,
                    AffectedCode = method.GetText().ToString(),
                    Suggestion = "Consider extracting smaller methods from this large method",
                    ImpactScore = Math.Min(1.0, (lineCount - GodMethodThreshold) / (double)GodMethodThreshold),
                    RefactoringPatterns = new List<RefactoringPattern>
                    {
                        new RefactoringPattern
                        {
                            PatternName = "Extract Method",
                            Description = "Extract logical sections into separate methods",
                            Steps = new List<string> { "Identify logical groups", "Extract into new methods", "Replace with method calls" },
                            Applicability = 0.95,
                            EstimatedEffort = 2
                        }
                    }
                };

                codeSmells.Add(codeSmell);
            }
        }

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectLargeClassSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();
        var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classDeclarations)
        {
            var methodCount = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
            if (methodCount > GodClassThreshold)
            {
                var codeSmell = new CodeSmell
                {
                    SmellType = "Large Class",
                    Description = $"Class has {methodCount} methods (threshold: {GodClassThreshold})",
                    Severity = DetermineCodeSmellSeverity(methodCount - GodClassThreshold, GodClassThreshold),
                    FilePath = filePath,
                    StartLine = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line,
                    EndLine = classDecl.GetLocation().GetLineSpan().EndLinePosition.Line,
                    AffectedCode = classDecl.GetText().ToString(),
                    Suggestion = "Consider splitting this class into smaller, more focused classes",
                    ImpactScore = Math.Min(1.0, (methodCount - GodClassThreshold) / (double)GodClassThreshold),
                    RefactoringPatterns = new List<RefactoringPattern>
                    {
                        new RefactoringPattern
                        {
                            PatternName = "Extract Class",
                            Description = "Extract related functionality into separate classes",
                            Steps = new List<string> { "Identify responsibilities", "Create new classes", "Move methods and fields", "Update references" },
                            Applicability = 0.9,
                            EstimatedEffort = 8
                        }
                    }
                };

                codeSmells.Add(codeSmell);
            }
        }

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectComplexConditionalSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();

        // This is a simplified implementation
        // In practice, you'd analyze nested conditionals, switch statements, etc.

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectDuplicateCodeSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();

        // This is a simplified implementation
        // In practice, you'd use duplicate code detection algorithms

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectMagicNumberSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();

        // This is a simplified implementation
        // In practice, you'd identify numeric literals that should be constants

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectFeatureEnvySmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();

        // This is a simplified implementation
        // In practice, you'd analyze method calls to other classes

        return codeSmells;
    }

    private async Task<List<CodeSmell>> DetectDataClumpsSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();

        // This is a simplified implementation
        // In practice, you'd identify groups of parameters that always appear together

        return codeSmells;
    }

    public CodeSmellSeverity DetermineCodeSmellSeverity(int excess, int threshold)
    {
        var ratio = excess / (double)threshold;

        if (ratio <= 0.2) return CodeSmellSeverity.Minor;
        if (ratio <= 0.5) return CodeSmellSeverity.Major;
        if (ratio <= 1.0) return CodeSmellSeverity.Critical;
        return CodeSmellSeverity.Blocker;
    }
}

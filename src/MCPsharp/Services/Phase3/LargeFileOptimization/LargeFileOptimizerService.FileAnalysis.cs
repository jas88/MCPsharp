using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Per-file size categorisation and large-file analysis for <see cref="LargeFileOptimizerService"/>.
/// </summary>
public partial class LargeFileOptimizerService
{
    #region Helper Methods

    private static bool IsFileInProject(string filePath, string projectPath)
    {
        return PathContainment.IsWithin(projectPath, filePath);
    }

    private static double ConvertToDouble(OptimizationPriority priority)
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

    private static FileSizeCategory CategorizeFileSize(int lineCount)
    {
        if (lineCount < 200)
            return FileSizeCategory.Small;
        if (lineCount < 500)
            return FileSizeCategory.Medium;
        if (lineCount < 1000)
            return FileSizeCategory.Large;
        if (lineCount < 2000)
            return FileSizeCategory.VeryLarge;
        return FileSizeCategory.Enormous;
    }

    private async Task<LargeFileInfo?> AnalyzeLargeFile(string filePath, int lineCount, int characterCount, CancellationToken cancellationToken)
    {
        try
        {
            var document = _workspace.GetDocument(filePath);
            if (document == null)
                return null;

            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (syntaxTree == null || semanticModel == null)
                return null;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var largeClasses = new List<string>();
            var largeMethods = new List<string>();

            var godClassCount = 0;
            var godMethodCount = 0;

            // Find large classes and god classes (same scoring as DetectGodClassesAsync)
            var classDeclarations = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            foreach (var classDecl in classDeclarations)
            {
                var classLineCount = classDecl.GetText().Lines.Count;
                if (classLineCount > 300) // Large class threshold
                {
                    largeClasses.Add($"{classDecl.Identifier.Text} ({classLineCount} lines)");
                }

                if (AnalyzeGodClass(classDecl, semanticModel, filePath).Severity != GodClassSeverity.None)
                {
                    godClassCount++;
                }
            }

            // Find large methods and god methods (same scoring as DetectGodMethodsAsync)
            var methodDeclarations = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
            foreach (var methodDecl in methodDeclarations)
            {
                var methodLineCount = methodDecl.GetText().Lines.Count;
                if (methodLineCount > 50) // Large method threshold
                {
                    largeMethods.Add($"{methodDecl.Identifier.Text} ({methodLineCount} lines)");
                }

                if (AnalyzeGodMethod(methodDecl, filePath).Severity != GodMethodSeverity.None)
                {
                    godMethodCount++;
                }
            }

            // Analyze code smells
            var codeSmells = await AnalyzeCodeSmellsAsync(filePath, cancellationToken);

            // Calculate overall complexity
            var overallComplexity = CalculateFileComplexity(root, cancellationToken);

            // Calculate optimization priority
            var priorityScore = CalculateFileOptimizationPriority(lineCount, largeClasses.Count, largeMethods.Count, codeSmells.Count);
            var priority = ConvertToDouble(priorityScore);

            // Generate immediate actions
            var immediateActions = GenerateImmediateActions(largeClasses, largeMethods, codeSmells);

            return new LargeFileInfo
            {
                FilePath = filePath,
                LineCount = lineCount,
                CharacterCount = characterCount,
                SizeCategory = CategorizeFileSize(lineCount),
                LargeClasses = largeClasses,
                LargeMethods = largeMethods,
                OverallComplexity = overallComplexity,
                CodeSmells = codeSmells,
                OptimizationPriority = priority,
                ImmediateActions = immediateActions,
                GodClassCount = godClassCount,
                GodMethodCount = godMethodCount
            };
        }
        catch (Exception ex)
        {
            // Log error and continue
            if (_logger is not null)
                LogFileAnalysisError(_logger, ex, filePath);
            return null;
        }
    }

    #endregion
}

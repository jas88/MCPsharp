using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Filtering, hashing and syntax context helpers
/// </summary>
public partial class DuplicateCodeDetectorService
{
    private static bool ShouldExcludeFile(string filePath, DuplicateDetectionOptions options)
    {
        if (options.ExcludedPaths.Any(pattern => filePath.Contains(pattern)))
            return true;

        // Skip generated files by default
        if (options.IgnoreGeneratedCode && filePath.Contains("Generated"))
            return true;

        // Skip test files if requested
        if (options.IgnoreTestCode && (filePath.Contains("Test") || filePath.Contains(".Tests.")))
            return true;

        return false;
    }

    private static string NormalizeCode(string code, DuplicateDetectionOptions options) =>
        DuplicateDetection.DuplicateDetectionUtils.NormalizeCode(code, options);

    private static string ComputeHash(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash);
    }

    private static string GetOptionsHash(DuplicateDetectionOptions options)
    {
        var key = $"{options.MinBlockSize}-{options.MaxBlockSize}-{options.DetectionTypes}-{options.IgnoreGeneratedCode}-{options.IgnoreTestCode}-{options.SimilarityThreshold}";
        return ComputeHash(key);
    }

    private static bool IsGeneratedCode(SyntaxNode node)
    {
        // Check for generated code attributes
        var attributes = node.DescendantNodesAndSelf().OfType<AttributeSyntax>();
        foreach (var attribute in attributes)
        {
            var attributeName = attribute.Name.ToString();
            if (attributeName.Contains("Generated") ||
                attributeName.Contains("CompilerGenerated") ||
                attributeName.Contains("GeneratedCode"))
            {
                return true;
            }
        }

        // Check for common generated file patterns in file path
        var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);
        var filePath = lineSpan.Path ?? "";

        return filePath.Contains("Generated") ||
               filePath.Contains("TemporaryGeneratedFile") ||
               filePath.Contains(".g.cs") ||
               filePath.Contains(".designer.cs");
    }

    private static bool IsTestCode(string filePath)
    {
        return filePath.Contains("Test") ||
               filePath.Contains(".Tests.") ||
               filePath.Contains("Spec") ||
               filePath.Contains("UnitTest");
    }

    private static string GetContainingType(SyntaxNode node)
    {
        var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        return classDecl?.Identifier.Text ?? "";
    }

    private static string GetNamespace(SyntaxNode node)
    {
        var namespaceDecl = node.FirstAncestorOrSelf<NamespaceDeclarationSyntax>();
        return namespaceDecl?.Name.ToString() ?? "";
    }

    private static MCPsharp.Models.Accessibility GetAccessibility(SyntaxNode node)
    {
        var modifiers = node.ChildTokens().FirstOrDefault(t => t.IsKind(SyntaxKind.PublicKeyword) ||
                                                               t.IsKind(SyntaxKind.PrivateKeyword) ||
                                                               t.IsKind(SyntaxKind.InternalKeyword) ||
                                                               t.IsKind(SyntaxKind.ProtectedKeyword));

        if (modifiers.IsKind(SyntaxKind.PublicKeyword))
            return MCPsharp.Models.Accessibility.Public;
        if (modifiers.IsKind(SyntaxKind.PrivateKeyword))
            return MCPsharp.Models.Accessibility.Private;
        if (modifiers.IsKind(SyntaxKind.InternalKeyword))
            return MCPsharp.Models.Accessibility.Internal;
        if (modifiers.IsKind(SyntaxKind.ProtectedKeyword))
            return MCPsharp.Models.Accessibility.Protected;

        return MCPsharp.Models.Accessibility.Private; // Default
    }

    private static CodeContext? ExtractContext(SyntaxNode node, int contextLines)
    {
        if (contextLines <= 0)
            return null;

        var syntaxTree = node.SyntaxTree;
        var lineSpan = syntaxTree.GetLineSpan(node.Span);
        var startLine = lineSpan.StartLinePosition.Line;
        var endLine = lineSpan.EndLinePosition.Line;

        var fullText = syntaxTree.GetText();
        var lines = fullText.Lines;

        var beforeLines = new List<string>();
        var afterLines = new List<string>();

        // Get context lines before
        for (int i = Math.Max(0, startLine - contextLines); i < startLine; i++)
        {
            beforeLines.Add(lines[i].ToString());
        }

        // Get context lines after
        for (int i = endLine + 1; i < Math.Min(lines.Count - 1, endLine + contextLines + 1); i++)
        {
            afterLines.Add(lines[i].ToString());
        }

        return new CodeContext
        {
            BeforeLines = beforeLines,
            AfterLines = afterLines,
            BeforeLineCount = beforeLines.Count,
            AfterLineCount = afterLines.Count
        };
    }

    private static DuplicateType DetermineDuplicateType(CodeBlock codeBlock)
    {
        return codeBlock.ElementType switch
        {
            CodeElementType.Method => DuplicateType.Method,
            CodeElementType.Class => DuplicateType.Class,
            CodeElementType.Property => DuplicateType.Property,
            CodeElementType.Constructor => DuplicateType.Constructor,
            CodeElementType.CodeBlock => DuplicateType.CodeBlock,
            _ => DuplicateType.Unknown
        };
    }

    private static DuplicationImpact CalculateDuplicationImpact(List<CodeBlock> blocks, double similarity)
    {
        var avgComplexity = blocks.Average(cb => cb.Complexity.OverallScore);
        var blockCount = blocks.Count;
        var avgLines = blocks.Average(cb => cb.EndLine - cb.StartLine + 1);

        // Calculate impact scores
        var maintenanceImpact = Math.Min(1.0, (blockCount * avgComplexity * similarity) / 50);
        var readabilityImpact = Math.Min(1.0, (blockCount * 0.3 + avgLines * 0.01) * similarity);
        var performanceImpact = Math.Min(1.0, avgComplexity * 0.05 * similarity);
        var testabilityImpact = Math.Min(1.0, blockCount * 0.2 * similarity);

        var overallImpact = (maintenanceImpact + readabilityImpact + performanceImpact + testabilityImpact) / 4;

        var impactLevel = overallImpact switch
        {
            >= 0.8 => ImpactLevel.Critical,
            >= 0.6 => ImpactLevel.Significant,
            >= 0.4 => ImpactLevel.Moderate,
            >= 0.2 => ImpactLevel.Minor,
            _ => ImpactLevel.Negligible
        };

        return new DuplicationImpact
        {
            MaintenanceImpact = maintenanceImpact,
            ReadabilityImpact = readabilityImpact,
            PerformanceImpact = performanceImpact,
            TestabilityImpact = testabilityImpact,
            OverallImpact = overallImpact,
            ImpactLevel = impactLevel
        };
    }

    private static double EstimateParameterComplexity(DuplicateGroup group)
    {
        // Simplified estimation based on line count and complexity
        return group.LineCount * 0.1 + group.Complexity * 0.2;
    }

    // Simplified estimation - assume moderate inheritance depth
    private const int EstimatedInheritanceDepth = 2;
}

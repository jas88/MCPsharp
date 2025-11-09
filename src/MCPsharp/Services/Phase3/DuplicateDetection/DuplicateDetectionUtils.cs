using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Utility methods for duplicate detection operations
/// </summary>
public static class DuplicateDetectionUtils
{
    public static string NormalizeCode(string code, DuplicateDetectionOptions options)
    {
        if (options.IgnoreTrivialDifferences)
        {
            // Remove comments
            code = Regex.Replace(code, @"//.*$", "", RegexOptions.Multiline);
            code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);

            // Normalize whitespace
            code = Regex.Replace(code, @"\s+", " ");
            code = code.Trim();
        }

        if (options.IgnoreIdentifiers)
        {
            // Replace identifiers with placeholder
            code = Regex.Replace(code, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b", "IDENTIFIER");
        }

        return code;
    }

    public static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    public static string GetOptionsHash(DuplicateDetectionOptions options)
    {
        var key = $"{options.MinBlockSize}-{options.MaxBlockSize}-{options.DetectionTypes}-{options.IgnoreGeneratedCode}-{options.IgnoreTestCode}-{options.SimilarityThreshold}";
        return ComputeHash(key);
    }

    public static int CalculateMaxDepth(SyntaxNode node)
    {
        var maxDepth = 0;

        void VisitNode(SyntaxNode n, int currentDepth)
        {
            maxDepth = Math.Max(maxDepth, currentDepth);
            foreach (var child in n.ChildNodes())
            {
                VisitNode(child, currentDepth + 1);
            }
        }

        VisitNode(node, 0);
        return maxDepth;
    }

    public static bool IsNumericType(Type type)
    {
        return type == typeof(sbyte) || type == typeof(byte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    public static bool IsGeneratedCode(SyntaxNode node, SemanticModel semanticModel)
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

    public static bool IsTestCode(SyntaxNode node, string filePath)
    {
        return filePath.Contains("Test") ||
               filePath.Contains(".Tests.") ||
               filePath.Contains("Spec") ||
               filePath.Contains("UnitTest");
    }

    public static DuplicateType DetermineDuplicateType(CodeBlock codeBlock)
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

    public static DuplicationImpact CalculateDuplicationImpact(List<CodeBlock> blocks, double similarity)
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

    public static HotspotRiskLevel CalculateHotspotRiskLevel(double score, int count)
    {
        if (score >= 100 || count >= 10)
            return HotspotRiskLevel.Critical;

        if (score >= 50 || count >= 5)
            return HotspotRiskLevel.High;

        if (score >= 20 || count >= 3)
            return HotspotRiskLevel.Medium;

        return HotspotRiskLevel.Low;
    }

    public static double EstimateParameterComplexity(DuplicateGroup group)
    {
        // Simplified estimation based on line count and complexity
        return group.LineCount * 0.1 + group.Complexity * 0.2;
    }

    public static int EstimateInheritanceDepth(DuplicateGroup group)
    {
        // Simplified estimation - assume moderate inheritance depth
        return 2;
    }
}

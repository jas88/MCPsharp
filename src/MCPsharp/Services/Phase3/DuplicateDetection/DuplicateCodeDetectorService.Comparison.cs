using Microsoft.CodeAnalysis;
using MCPsharp.Models;
using MCPsharp.Services.Phase3.DuplicateDetection;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Exact duplicate grouping, pairwise comparison and duplication metrics
/// </summary>
public partial class DuplicateCodeDetectorService
{
    private static List<DuplicateGroup> FindExactDuplicatesInternal(
        List<CodeBlock> codeBlocks,
        DuplicateDetectionOptions options)
    {
        var duplicateGroups = new List<DuplicateGroup>();
        var hashGroups = codeBlocks.GroupBy(cb => cb.CodeHash);

        foreach (var hashGroup in hashGroups)
        {
            if (hashGroup.Count() < 2) continue; // Need at least 2 blocks to be a duplicate

            var blocksList = hashGroup.ToList();
            var similarityScore = 1.0; // Exact duplicates have 100% similarity
            var lineCount = blocksList[0].EndLine - blocksList[0].StartLine + 1;
            var complexity = blocksList[0].Complexity.OverallScore;
            var duplicateType = DetermineDuplicateType(blocksList[0]);

            duplicateGroups.Add(new DuplicateGroup
            {
                GroupId = Guid.NewGuid().ToString(),
                CodeBlocks = blocksList,
                SimilarityScore = similarityScore,
                DuplicationType = duplicateType,
                LineCount = lineCount,
                Complexity = (int)complexity,
                IsExactDuplicate = true,
                CloneType = CloneType.Exact,
                Impact = CalculateDuplicationImpact(blocksList, similarityScore),
                Metadata = new DuplicateMetadata
                {
                    DetectedAt = DateTime.UtcNow,
                    DetectionAlgorithm = "ExactHashMatch",
                    DetectionConfiguration = GetOptionsHash(options),
                    AdditionalMetadata = new Dictionary<string, object>
                    {
                        ["HashAlgorithm"] = "SHA256",
                        ["NormalizationLevel"] = options.IgnoreTrivialDifferences ? "Full" : "Partial"
                    }
                }
            });
        }

        return duplicateGroups.OrderByDescending(g => g.Complexity).ToList();
    }

    private static List<CodeDifference> FindCodeDifferences(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CloneAlignment? alignment)
    {
        // Token-level diff over normalised tokens, with statement-level grouping of insertions/deletions
        if (codeBlock1.Fingerprint != null && codeBlock2.Fingerprint != null)
        {
            alignment ??= CloneComparer.Align(codeBlock1.Fingerprint, codeBlock2.Fingerprint);
            if (alignment != null)
            {
                return CloneDiffer.FindDifferences(codeBlock1.Fingerprint, codeBlock2.Fingerprint, alignment).ToList();
            }
        }

        // Fingerprints unavailable or the blocks are too different to align: report a single coarse difference
        var differences = new List<CodeDifference>();
        if (codeBlock1.NormalizedCode != codeBlock2.NormalizedCode)
        {
            differences.Add(new CodeDifference
            {
                Type = DifferenceType.Structural,
                Description = "Code blocks have different structure",
                Location1 = new CodeLocation { Line = codeBlock1.StartLine, Column = codeBlock1.StartColumn, Length = codeBlock1.EndLine - codeBlock1.StartLine + 1 },
                Location2 = new CodeLocation { Line = codeBlock2.StartLine, Column = codeBlock2.StartColumn, Length = codeBlock2.EndLine - codeBlock2.StartLine + 1 },
                Text1 = codeBlock1.NormalizedCode,
                Text2 = codeBlock2.NormalizedCode,
                Impact = CloneDiffer.StructuralDifferenceImpact
            });
        }

        return differences;
    }

    private static List<CommonPattern> FindCommonPatterns(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2)
    {
        var patterns = new List<CommonPattern>();

        // Analyze control flow patterns
        var commonFlowPatterns = codeBlock1.AstStructure.ControlFlowPatterns
            .Intersect(codeBlock2.AstStructure.ControlFlowPatterns)
            .ToList();

        foreach (var pattern in commonFlowPatterns)
        {
            patterns.Add(new CommonPattern
            {
                Type = PatternType.ControlFlow,
                Description = $"Common {pattern} pattern",
                Frequency = 1,
                Confidence = 0.8
            });
        }

        // Analyze data flow patterns
        var commonDataPatterns = codeBlock1.AstStructure.DataFlowPatterns
            .Intersect(codeBlock2.AstStructure.DataFlowPatterns)
            .ToList();

        foreach (var pattern in commonDataPatterns)
        {
            patterns.Add(new CommonPattern
            {
                Type = PatternType.DataFlow,
                Description = $"Common {pattern} pattern",
                Frequency = 1,
                Confidence = 0.7
            });
        }

        return patterns;
    }

    private static DuplicateType? DeterminePairDuplicateType(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2)
    {
        if (codeBlock1.ElementType != codeBlock2.ElementType)
            return null;

        return codeBlock1.ElementType switch
        {
            CodeElementType.Method => DuplicateType.Method,
            CodeElementType.Class => DuplicateType.Class,
            CodeElementType.Property => DuplicateType.Property,
            CodeElementType.Constructor => DuplicateType.Constructor,
            CodeElementType.CodeBlock => DuplicateType.CodeBlock,
            _ => DuplicateType.Unknown
        };
    }

    private static DuplicationMetrics CalculateDuplicationMetrics(
        List<DuplicateGroup> duplicateGroups,
        List<CodeBlock> allCodeBlocks)
    {
        var filesWithDuplicates = duplicateGroups
            .SelectMany(g => g.CodeBlocks)
            .Select(cb => cb.FilePath)
            .Distinct()
            .Count();

        var totalLines = allCodeBlocks.Sum(cb => cb.EndLine - cb.StartLine + 1);
        var duplicateLines = duplicateGroups
            .SelectMany(g => g.CodeBlocks)
            .Sum(cb => cb.EndLine - cb.StartLine + 1);

        var duplicationPercentage = totalLines > 0 ? (double)duplicateLines / totalLines * 100 : 0.0;

        // Calculate duplication by type
        var duplicationByType = duplicateGroups
            .GroupBy(g => g.DuplicationType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate duplication by file
        var duplicationByFile = duplicateGroups
            .SelectMany(g => g.CodeBlocks)
            .GroupBy(cb => cb.FilePath)
            .ToDictionary(
                g => g.Key,
                g => new FileDuplicationMetrics
                {
                    FilePath = g.Key,
                    DuplicateGroups = g.Select(cb => cb.ElementName).Distinct().Count(),
                    DuplicateLines = g.Sum(cb => cb.EndLine - cb.StartLine + 1),
                    DuplicationPercentage = totalLines > 0 ? (double)g.Sum(cb => cb.EndLine - cb.StartLine + 1) / totalLines * 100 : 0.0,
                    DuplicationTypes = g.Select(cb => DetermineDuplicateType(cb)).Distinct().ToList(),
                    AverageComplexity = g.Average(cb => cb.Complexity.OverallScore)
                });

        // Calculate duplication by similarity ranges
        var similarityRanges = new Dictionary<string, int>
        {
            ["90-100%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.9),
            ["80-89%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.8 && g.SimilarityScore < 0.9),
            ["70-79%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.7 && g.SimilarityScore < 0.8),
            ["60-69%"] = duplicateGroups.Count(g => g.SimilarityScore >= 0.6 && g.SimilarityScore < 0.7),
            ["<60%"] = duplicateGroups.Count(g => g.SimilarityScore < 0.6)
        };

        var similarities = duplicateGroups.Select(g => g.SimilarityScore).ToList();
        var avgSimilarity = similarities.Count != 0 ? similarities.Average() : 0.0;
        var maxSimilarity = similarities.Count != 0 ? similarities.Max() : 0.0;
        var minSimilarity = similarities.Count != 0 ? similarities.Min() : 0.0;

        // Estimate costs (simplified calculation)
        var estimatedMaintenanceCost = duplicateLines * 0.5; // 0.5 hours per line of duplicate code
        var estimatedRefactoringSavings = duplicateLines * 0.3; // 0.3 hours saved per line refactored

        return new DuplicationMetrics
        {
            TotalDuplicateGroups = duplicateGroups.Count,
            ExactDuplicateGroups = duplicateGroups.Count(g => g.IsExactDuplicate),
            NearMissDuplicateGroups = duplicateGroups.Count(g => !g.IsExactDuplicate),
            TotalDuplicateLines = duplicateLines,
            DuplicationPercentage = duplicationPercentage,
            FilesWithDuplicates = filesWithDuplicates,
            DuplicationByType = duplicationByType,
            DuplicationByFile = duplicationByFile,
            DuplicationBySimilarity = similarityRanges,
            AverageSimilarity = avgSimilarity,
            MaxSimilarity = maxSimilarity,
            MinSimilarity = minSimilarity,
            EstimatedMaintenanceCost = estimatedMaintenanceCost,
            EstimatedRefactoringSavings = estimatedRefactoringSavings
        };
    }
}

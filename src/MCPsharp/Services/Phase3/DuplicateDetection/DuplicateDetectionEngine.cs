using Microsoft.Extensions.Logging;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Engine for finding exact and near duplicates using various algorithms
/// </summary>
public class DuplicateDetectionEngine
{
    private readonly SimilarityCalculatorService _similarityCalculator;
    private readonly ILogger<DuplicateDetectionEngine> _logger;

    public DuplicateDetectionEngine(
        SimilarityCalculatorService similarityCalculator,
        ILogger<DuplicateDetectionEngine> logger)
    {
        _similarityCalculator = similarityCalculator;
        _logger = logger;
    }

    public async Task<List<DuplicateGroup>> FindExactDuplicatesAsync(
        List<CodeBlock> codeBlocks,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var duplicateGroups = new List<DuplicateGroup>();
        var hashGroups = codeBlocks.GroupBy(cb => cb.CodeHash);

        foreach (var hashGroup in hashGroups)
        {
            if (hashGroup.Count() < 2) continue; // Need at least 2 blocks to be a duplicate

            var blocksList = hashGroup.ToList();
            var similarityScore = 1.0; // Exact duplicates have 100% similarity
            var lineCount = blocksList.First().EndLine - blocksList.First().StartLine + 1;
            var complexity = blocksList.First().Complexity.OverallScore;
            var duplicateType = DuplicateDetectionUtils.DetermineDuplicateType(blocksList.First());

            duplicateGroups.Add(new DuplicateGroup
            {
                GroupId = Guid.NewGuid().ToString(),
                CodeBlocks = blocksList,
                SimilarityScore = similarityScore,
                DuplicationType = duplicateType,
                LineCount = lineCount,
                Complexity = (int)complexity,
                IsExactDuplicate = true,
                Impact = DuplicateDetectionUtils.CalculateDuplicationImpact(blocksList, similarityScore),
                Metadata = new DuplicateMetadata
                {
                    DetectedAt = DateTime.UtcNow,
                    DetectionAlgorithm = "ExactHashMatch",
                    DetectionConfiguration = DuplicateDetectionUtils.GetOptionsHash(options),
                    AdditionalMetadata = new Dictionary<string, object>
                    {
                        ["HashAlgorithm"] = "SHA256",
                        ["NormalizationLevel"] = options.IgnoreTrivialDifferences ? "Full" : "Partial"
                    }
                }
            });
        }

        return await Task.FromResult(duplicateGroups.OrderByDescending(g => g.Complexity).ToList());
    }

    public async Task<List<DuplicateGroup>> FindNearDuplicatesAsync(
        List<CodeBlock> codeBlocks,
        double similarityThreshold,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        var duplicateGroups = new List<DuplicateGroup>();
        var processedPairs = new HashSet<string>();

        // Group by element type and similar size for efficiency
        var typeGroups = codeBlocks.GroupBy(cb => cb.ElementType);

        foreach (var typeGroup in typeGroups)
        {
            var blocksBySize = typeGroup.GroupBy(cb => cb.EndLine - cb.StartLine + 1);

            foreach (var sizeGroup in blocksBySize)
            {
                var blocksList = sizeGroup.ToList();

                for (int i = 0; i < blocksList.Count; i++)
                {
                    for (int j = i + 1; j < blocksList.Count; j++)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return duplicateGroups;

                        var block1 = blocksList[i];
                        var block2 = blocksList[j];

                        // Skip if already processed this pair
                        var pairKey = $"{Math.Min(block1.CodeHash.GetHashCode(), block2.CodeHash.GetHashCode())}_{Math.Max(block1.CodeHash.GetHashCode(), block2.CodeHash.GetHashCode())}";
                        if (processedPairs.Contains(pairKey))
                            continue;

                        processedPairs.Add(pairKey);

                        // Calculate similarity
                        var similarity = await _similarityCalculator.CalculateSimilarityAsync(block1, block2, options, cancellationToken);

                        if (similarity >= similarityThreshold)
                        {
                            // Find or create duplicate group
                            var existingGroup = FindOrCreateNearDuplicateGroup(duplicateGroups, block1, block2, similarity, options);

                            if (existingGroup == null)
                            {
                                duplicateGroups.Add(CreateNearDuplicateGroup(block1, block2, similarity, options));
                            }
                        }
                    }
                }
            }
        }

        return await Task.FromResult(duplicateGroups.OrderByDescending(g => g.SimilarityScore).ThenByDescending(g => g.Complexity).ToList());
    }

    private DuplicateGroup? FindOrCreateNearDuplicateGroup(
        List<DuplicateGroup> groups,
        CodeBlock block1,
        CodeBlock block2,
        double similarity,
        DuplicateDetectionOptions options)
    {
        // Find if either block is already in a group
        var existingGroup = groups.FirstOrDefault(g =>
            g.CodeBlocks.Any(cb => cb.CodeHash == block1.CodeHash || cb.CodeHash == block2.CodeHash));

        if (existingGroup != null)
        {
            // Check if the other block should be added to this group
            if (!existingGroup.CodeBlocks.Any(cb => cb.CodeHash == block1.CodeHash))
            {
                var updatedBlocks = existingGroup.CodeBlocks.Append(block1).ToList();
                // Update group properties based on all blocks
                existingGroup = UpdateDuplicateGroup(existingGroup, updatedBlocks);
            }
            else if (!existingGroup.CodeBlocks.Any(cb => cb.CodeHash == block2.CodeHash))
            {
                var updatedBlocks = existingGroup.CodeBlocks.Append(block2).ToList();
                existingGroup = UpdateDuplicateGroup(existingGroup, updatedBlocks);
            }
        }

        return existingGroup;
    }

    private DuplicateGroup CreateNearDuplicateGroup(
        CodeBlock block1,
        CodeBlock block2,
        double similarity,
        DuplicateDetectionOptions options)
    {
        var blocks = new List<CodeBlock> { block1, block2 };
        var lineCount = (block1.EndLine - block1.StartLine + 1 + block2.EndLine - block2.StartLine + 1) / 2;
        var complexity = (int)((block1.Complexity.OverallScore + block2.Complexity.OverallScore) / 2);
        var duplicateType = DuplicateDetectionUtils.DetermineDuplicateType(block1);

        return new DuplicateGroup
        {
            GroupId = Guid.NewGuid().ToString(),
            CodeBlocks = blocks,
            SimilarityScore = similarity,
            DuplicationType = duplicateType,
            LineCount = lineCount,
            Complexity = complexity,
            IsExactDuplicate = false,
            Impact = DuplicateDetectionUtils.CalculateDuplicationImpact(blocks, similarity),
            Metadata = new DuplicateMetadata
            {
                DetectedAt = DateTime.UtcNow,
                DetectionAlgorithm = "NearDuplicateDetection",
                DetectionConfiguration = DuplicateDetectionUtils.GetOptionsHash(options),
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["SimilarityThreshold"] = options.SimilarityThreshold,
                    ["StructuralWeight"] = 0.7,
                    ["TokenWeight"] = 0.3
                }
            }
        };
    }

    private DuplicateGroup UpdateDuplicateGroup(DuplicateGroup group, List<CodeBlock> updatedBlocks)
    {
        var lineCount = (int)updatedBlocks.Average(cb => cb.EndLine - cb.StartLine + 1);
        var complexity = (int)updatedBlocks.Average(cb => cb.Complexity.OverallScore);
        var avgSimilarity = updatedBlocks.Count > 1 ? 0.95 : 1.0;
        var impact = DuplicateDetectionUtils.CalculateDuplicationImpact(updatedBlocks, avgSimilarity);

        // Create new group with updated values (init-only properties)
        return new DuplicateGroup
        {
            GroupId = group.GroupId,
            CodeBlocks = updatedBlocks,
            SimilarityScore = group.SimilarityScore,
            DuplicationType = group.DuplicationType,
            LineCount = lineCount,
            Complexity = complexity,
            IsExactDuplicate = group.IsExactDuplicate,
            Impact = impact,
            Metadata = group.Metadata
        };
    }

    public async Task<DuplicateType?> DetermineDuplicateTypeAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        double similarity,
        CancellationToken cancellationToken)
    {
        if (codeBlock1.ElementType != codeBlock2.ElementType)
            return null;

        return await Task.FromResult(codeBlock1.ElementType switch
        {
            CodeElementType.Method => DuplicateType.Method,
            CodeElementType.Class => DuplicateType.Class,
            CodeElementType.Property => DuplicateType.Property,
            CodeElementType.Constructor => DuplicateType.Constructor,
            CodeElementType.CodeBlock => DuplicateType.CodeBlock,
            _ => DuplicateType.Unknown
        });
    }

    public async Task<List<CodeDifference>> FindCodeDifferencesAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CodeComparisonOptions options,
        CancellationToken cancellationToken)
    {
        var differences = new List<CodeDifference>();

        // TODO: Enhance detailed difference analysis
        // Future enhancement: Use syntax tree diffing to identify exact changes
        if (codeBlock1.NormalizedCode != codeBlock2.NormalizedCode)
        {
            differences.Add(new CodeDifference
            {
                Type = DifferenceType.Structural,
                Description = "Code blocks have different structure",
                Location1 = new CodeLocation
                {
                    Line = codeBlock1.StartLine,
                    Column = codeBlock1.StartColumn,
                    Length = codeBlock1.EndLine - codeBlock1.StartLine + 1
                },
                Location2 = new CodeLocation
                {
                    Line = codeBlock2.StartLine,
                    Column = codeBlock2.StartColumn,
                    Length = codeBlock2.EndLine - codeBlock2.StartLine + 1
                },
                Text1 = codeBlock1.NormalizedCode,
                Text2 = codeBlock2.NormalizedCode,
                Impact = 0.5
            });
        }

        return await Task.FromResult(differences);
    }

    public async Task<List<CommonPattern>> FindCommonPatternsAsync(
        CodeBlock codeBlock1,
        CodeBlock codeBlock2,
        CancellationToken cancellationToken)
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

        return await Task.FromResult(patterns);
    }
}

using Microsoft.CodeAnalysis;
using MCPsharp.Models;
using MCPsharp.Services.Phase3.DuplicateDetection;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Type-2 (renamed) and Type-3 (near-miss) clone detection based on normalised token fingerprints
/// </summary>
public partial class DuplicateCodeDetectorService
{
    /// <summary>Share of the overall similarity contributed by the normalised-token (semantic) similarity</summary>
    internal const double SemanticSimilarityWeight = 0.5;

    /// <summary>Relative weight of AST node-sequence similarity within the remaining share</summary>
    internal const double DefaultStructuralWeight = 0.7;

    /// <summary>Relative weight of token-category similarity within the remaining share</summary>
    internal const double DefaultTokenWeight = 0.3;

    /// <summary>Similarity at or above which two blocks are reported as duplicates when comparing a single pair</summary>
    internal const double DefaultDuplicateThreshold = 0.8;

    /// <summary>Upper bound on candidate pairs aligned during near-miss detection, keeping large projects tractable</summary>
    internal const int MaxNearMissComparisons = 500_000;

    /// <summary>
    /// Builds fingerprints for the blocks of one syntax tree, sharing identifier classifications between
    /// overlapping blocks (class, method and nested block) so names are bound only once.
    /// </summary>
    private sealed class FingerprintContext(SemanticModel? semanticModel)
    {
        private readonly Dictionary<SyntaxNode, bool> _typeNameCache = new();

        public CloneFingerprint Build(SyntaxNode node) =>
            CloneFingerprintBuilder.Build(node, semanticModel, _typeNameCache);
    }

    private sealed record SimilarityBreakdown(
        double Overall,
        double Structural,
        double Token,
        double Semantic,
        CloneAlignment? Alignment);

    private readonly record struct SimilarityEdge(int First, int Second, double Similarity, CloneType CloneType);

    private static double MinimumComponentSimilarity(double threshold, double weight) =>
        weight <= 0 ? 0.0 : Math.Max(0.0, (threshold - (1.0 - weight)) / weight);

    /// <summary>
    /// Computes the weighted similarity of two blocks. When <paramref name="threshold"/> is positive the
    /// computation stops early (returning an overall similarity of zero) once the threshold is provably unreachable.
    /// </summary>
    private static SimilarityBreakdown ComputeSimilarity(
        CodeBlock block1,
        CodeBlock block2,
        double structuralWeight,
        double tokenWeight,
        bool ignoreIdentifiers,
        double threshold)
    {
        var legacyTotal = structuralWeight + tokenWeight;
        var remaining = 1.0 - SemanticSimilarityWeight;
        var structuralShare = legacyTotal > 0 ? remaining * structuralWeight / legacyTotal : remaining / 2;
        var tokenShare = legacyTotal > 0 ? remaining * tokenWeight / legacyTotal : remaining / 2;
        var below = new SimilarityBreakdown(0.0, 0.0, 0.0, 0.0, null);

        CloneAlignment? alignment = null;
        double semantic;
        if (block1.Fingerprint != null && block2.Fingerprint != null)
        {
            alignment = CloneComparer.Align(block1.Fingerprint, block2.Fingerprint,
                MinimumComponentSimilarity(threshold, SemanticSimilarityWeight));
            if (alignment == null)
            {
                return below;
            }

            semantic = alignment.SemanticSimilarity;
        }
        else
        {
            semantic = LegacySemanticSimilarity(block1, block2);
        }

        var structural = SequenceSimilarity(
            block1.AstStructure.NodeTypes,
            block2.AstStructure.NodeTypes,
            block1.AstStructure.StructuralHash == block2.AstStructure.StructuralHash,
            MinimumComponentSimilarity(threshold, structuralShare));
        if (structural == null)
        {
            return below;
        }

        var token = SequenceSimilarity(
            TokenCategories(block1, ignoreIdentifiers),
            TokenCategories(block2, ignoreIdentifiers),
            false,
            MinimumComponentSimilarity(threshold, tokenShare));
        if (token == null)
        {
            return below;
        }

        var overall = (semantic * SemanticSimilarityWeight) + (structural.Value * structuralShare) + (token.Value * tokenShare);
        return new SimilarityBreakdown(overall, structural.Value, token.Value, semantic, alignment);
    }

    private static double? SequenceSimilarity(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second,
        bool knownIdentical,
        double minimumSimilarity)
    {
        if (knownIdentical)
        {
            return 1.0;
        }

        if (minimumSimilarity <= 0.0)
        {
            return MyersDiff.Similarity(first, second, 0.0, CloneComparer.MaxEditDistance) ?? 0.0;
        }

        return MyersDiff.Similarity(first, second, minimumSimilarity, CloneComparer.MaxEditDistance);
    }

    private static List<string> TokenCategories(CodeBlock block, bool ignoreIdentifiers) =>
        (block.Tokens ?? Array.Empty<CodeToken>())
            .Where(t => t.Type is not (TokenType.Whitespace or TokenType.Comment) &&
                        !(ignoreIdentifiers && t.Type == TokenType.Identifier))
            .Select(t => t.Type == TokenType.Unknown ? t.Text : t.Type.ToString())
            .ToList();

    // Fallback used only for blocks without fingerprints (e.g. constructed by callers)
    private static double LegacySemanticSimilarity(CodeBlock codeBlock1, CodeBlock codeBlock2)
    {
        if (codeBlock1.ElementType != codeBlock2.ElementType)
            return 0.0;

        return string.Equals(codeBlock1.NormalizedCode, codeBlock2.NormalizedCode, StringComparison.Ordinal) ? 1.0 : 0.0;
    }

    private static CloneType? DeterminePairCloneType(CodeBlock block1, CodeBlock block2, bool isDuplicate)
    {
        if (block1.CodeHash == block2.CodeHash)
        {
            return CloneType.Exact;
        }

        if (block1.Fingerprint != null && block2.Fingerprint != null)
        {
            var cloneType = CloneComparer.Classify(block1.Fingerprint, block2.Fingerprint);
            if (cloneType != CloneType.NearMiss)
            {
                return cloneType;
            }
        }

        return isDuplicate ? CloneType.NearMiss : null;
    }

    private static List<DuplicateGroup> FindNearDuplicatesInternal(
        List<CodeBlock> codeBlocks,
        double similarityThreshold,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        // Identical blocks are represented once; exact groups are reported separately
        var classes = codeBlocks
            .GroupBy(cb => (cb.ElementType, cb.CodeHash))
            .Select(g => g.ToList())
            .ToList();
        var representatives = classes.Select(c => c[0]).ToList();
        var components = new DisjointSet(representatives.Count);
        var edges = new List<SimilarityEdge>();

        AddRenamedCloneEdges(representatives, components, edges, options);
        AddNearMissEdges(representatives, components, edges, similarityThreshold, options, cancellationToken);

        var groups = new List<DuplicateGroup>();
        foreach (var component in edges.GroupBy(e => components.Find(e.First)))
        {
            var memberIndices = component.SelectMany(e => new[] { e.First, e.Second }).Distinct().Order().ToList();
            var blocks = memberIndices.SelectMany(i => classes[i]).ToList();
            var similarity = component.Average(e => e.Similarity);
            var cloneType = component.Any(e => e.CloneType == CloneType.NearMiss) ? CloneType.NearMiss : CloneType.Renamed;
            groups.Add(CreateNearDuplicateGroup(blocks, similarity, cloneType, options));
        }

        return groups.OrderByDescending(g => g.SimilarityScore).ThenByDescending(g => g.Complexity).ToList();
    }

    private static void AddRenamedCloneEdges(
        List<CodeBlock> representatives,
        DisjointSet components,
        List<SimilarityEdge> edges,
        DuplicateDetectionOptions options)
    {
        // Type-2 clones share the hash of their alpha-renamed token sequence: O(n) bucketing
        var buckets = Enumerable.Range(0, representatives.Count)
            .Where(i => representatives[i].Fingerprint != null)
            .GroupBy(i => (representatives[i].ElementType, representatives[i].Fingerprint!.RenamedHash))
            .Where(g => g.Skip(1).Any());

        foreach (var bucket in buckets)
        {
            var members = bucket.ToList();
            for (var k = 1; k < members.Count; k++)
            {
                var similarity = ComputeSimilarity(
                    representatives[members[0]], representatives[members[k]],
                    DefaultStructuralWeight, DefaultTokenWeight, options.IgnoreIdentifiers, 0.0);
                edges.Add(new SimilarityEdge(members[0], members[k], similarity.Overall, CloneType.Renamed));
                components.Union(members[0], members[k]);
            }
        }
    }

    private static void AddNearMissEdges(
        List<CodeBlock> representatives,
        DisjointSet components,
        List<SimilarityEdge> edges,
        double similarityThreshold,
        DuplicateDetectionOptions options,
        CancellationToken cancellationToken)
    {
        // Sound pruning bound: the semantic component must reach this value for the overall score to reach the threshold
        var semanticBound = MinimumComponentSimilarity(similarityThreshold, SemanticSimilarityWeight);
        var histograms = new Dictionary<int, Dictionary<string, int>>();
        var comparisons = 0;

        Dictionary<string, int> HistogramOf(int index)
        {
            if (!histograms.TryGetValue(index, out var histogram))
            {
                histogram = CloneComparer.BuildShapeHistogram(representatives[index].Fingerprint!);
                histograms[index] = histogram;
            }

            return histogram;
        }

        var byType = Enumerable.Range(0, representatives.Count)
            .Where(i => representatives[i].Fingerprint != null)
            .GroupBy(i => representatives[i].ElementType);

        foreach (var typeGroup in byType)
        {
            // Sorting by length lets the length bound terminate the inner loop early
            var sorted = typeGroup.OrderBy(i => representatives[i].Fingerprint!.Tokens.Count).ToList();
            for (var a = 0; a < sorted.Count; a++)
            {
                var first = sorted[a];
                var firstCount = representatives[first].Fingerprint!.Tokens.Count;

                for (var b = a + 1; b < sorted.Count; b++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var second = sorted[b];
                    var secondCount = representatives[second].Fingerprint!.Tokens.Count;

                    if (CloneComparer.LengthUpperBound(firstCount, secondCount) < semanticBound)
                    {
                        break;
                    }

                    if (components.Find(first) == components.Find(second) ||
                        CloneComparer.SimilarityUpperBound(HistogramOf(first), firstCount, HistogramOf(second), secondCount) < semanticBound)
                    {
                        continue;
                    }

                    if (++comparisons > MaxNearMissComparisons)
                    {
                        return;
                    }

                    var similarity = ComputeSimilarity(
                        representatives[first], representatives[second],
                        DefaultStructuralWeight, DefaultTokenWeight, options.IgnoreIdentifiers, similarityThreshold);
                    if (similarity.Overall >= similarityThreshold)
                    {
                        edges.Add(new SimilarityEdge(first, second, similarity.Overall, CloneType.NearMiss));
                        components.Union(first, second);
                    }
                }
            }
        }
    }

    private static DuplicateGroup CreateNearDuplicateGroup(
        List<CodeBlock> blocks,
        double similarity,
        CloneType cloneType,
        DuplicateDetectionOptions options)
    {
        var lineCount = (int)blocks.Average(cb => cb.EndLine - cb.StartLine + 1);
        var complexity = (int)blocks.Average(cb => cb.Complexity.OverallScore);

        return new DuplicateGroup
        {
            GroupId = Guid.NewGuid().ToString(),
            CodeBlocks = blocks,
            SimilarityScore = similarity,
            DuplicationType = DetermineDuplicateType(blocks[0]),
            LineCount = lineCount,
            Complexity = complexity,
            IsExactDuplicate = false,
            CloneType = cloneType,
            Impact = CalculateDuplicationImpact(blocks, similarity),
            Metadata = new DuplicateMetadata
            {
                DetectedAt = DateTime.UtcNow,
                DetectionAlgorithm = cloneType == CloneType.Renamed ? "AlphaRenamedHashMatch" : "NormalizedTokenAlignment",
                DetectionConfiguration = GetOptionsHash(options),
                AdditionalMetadata = new Dictionary<string, object>
                {
                    ["SimilarityThreshold"] = options.SimilarityThreshold,
                    ["ComparisonMethod"] = "AST+Token+NormalizedToken",
                    ["CloneType"] = cloneType.ToString()
                }
            }
        };
    }
}

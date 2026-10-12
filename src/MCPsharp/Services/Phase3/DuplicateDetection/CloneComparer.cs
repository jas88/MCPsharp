using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Token alignment of two fingerprinted code blocks
/// </summary>
public sealed class CloneAlignment
{
    /// <summary>
    /// Edit script over the shape token sequences
    /// </summary>
    public required IReadOnlyList<DiffOperation> Operations { get; init; }

    /// <summary>
    /// Number of aligned tokens
    /// </summary>
    public required int MatchedTokens { get; init; }

    /// <summary>
    /// Dice similarity of the shape token sequences (0.0-1.0)
    /// </summary>
    public required double ShapeSimilarity { get; init; }

    /// <summary>
    /// Fraction of aligned identifier pairs consistent with a single bijective renaming (0.0-1.0)
    /// </summary>
    public required double RenameConsistency { get; init; }

    /// <summary>
    /// Semantic similarity: shape similarity penalised by inconsistent renaming (0.0-1.0)
    /// </summary>
    public required double SemanticSimilarity { get; init; }
}

/// <summary>
/// Compares clone fingerprints: alignment, semantic similarity and clone classification
/// </summary>
public static class CloneComparer
{
    /// <summary>
    /// Fraction of similarity removed when identifier renaming is completely inconsistent
    /// </summary>
    public const double InconsistentRenamePenalty = 0.5;

    /// <summary>
    /// Upper bound on the edit distance explored when aligning two blocks, bounding time and memory
    /// </summary>
    public const int MaxEditDistance = 2000;

    /// <summary>
    /// Aligns two fingerprints.
    /// </summary>
    /// <param name="first">First fingerprint</param>
    /// <param name="second">Second fingerprint</param>
    /// <param name="minimumSimilarity">
    /// Minimum semantic similarity of interest; alignment is abandoned (returning <c>null</c>)
    /// as soon as it is provably below this value
    /// </param>
    public static CloneAlignment? Align(CloneFingerprint first, CloneFingerprint second, double minimumSimilarity = 0.0)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var shapes1 = first.Tokens.Select(t => t.Shape).ToList();
        var shapes2 = second.Tokens.Select(t => t.Shape).ToList();
        var total = shapes1.Count + shapes2.Count;

        if (total == 0)
        {
            return new CloneAlignment
            {
                Operations = Array.Empty<DiffOperation>(),
                MatchedTokens = 0,
                ShapeSimilarity = 1.0,
                RenameConsistency = 1.0,
                SemanticSimilarity = 1.0
            };
        }

        // Semantic similarity never exceeds shape similarity, so the same bound prunes the diff
        var allowedEdits = Math.Min(MaxEditDistance, MyersDiff.MaxEditsForSimilarity(total, minimumSimilarity));
        var operations = MyersDiff.Compute(shapes1, shapes2, allowedEdits);
        if (operations == null)
        {
            return null;
        }

        var matched = operations.Count(op => op.Kind == DiffOperationKind.Equal);
        var shapeSimilarity = 2.0 * matched / total;
        var consistency = CalculateRenameConsistency(first, second, operations);
        var semantic = shapeSimilarity * (1.0 - (InconsistentRenamePenalty * (1.0 - consistency)));

        if (semantic < minimumSimilarity)
        {
            return null;
        }

        return new CloneAlignment
        {
            Operations = operations,
            MatchedTokens = matched,
            ShapeSimilarity = shapeSimilarity,
            RenameConsistency = consistency,
            SemanticSimilarity = semantic
        };
    }

    /// <summary>
    /// Classifies the clone relationship between two fingerprints.
    /// </summary>
    public static CloneType Classify(CloneFingerprint first, CloneFingerprint second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Tokens.Count == second.Tokens.Count &&
            first.Tokens.Zip(second.Tokens).All(pair => string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal)))
        {
            return CloneType.Exact;
        }

        return string.Equals(first.RenamedHash, second.RenamedHash, StringComparison.Ordinal)
            ? CloneType.Renamed
            : CloneType.NearMiss;
    }

    /// <summary>
    /// Builds a multiset of shape tokens, used for a cheap similarity upper bound.
    /// </summary>
    public static Dictionary<string, int> BuildShapeHistogram(CloneFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var shape in fingerprint.Tokens.Select(token => token.Shape))
        {
            histogram[shape] = histogram.GetValueOrDefault(shape) + 1;
        }

        return histogram;
    }

    /// <summary>
    /// Upper bound on the Dice similarity of two token sequences given their shape multisets
    /// (the longest common subsequence cannot exceed the multiset intersection).
    /// </summary>
    public static double SimilarityUpperBound(
        IReadOnlyDictionary<string, int> first,
        int firstCount,
        IReadOnlyDictionary<string, int> second,
        int secondCount)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var total = firstCount + secondCount;
        if (total == 0)
        {
            return 1.0;
        }

        var (smaller, larger) = first.Count <= second.Count ? (first, second) : (second, first);
        var intersection = 0;
        foreach (var (shape, count) in smaller)
        {
            if (larger.TryGetValue(shape, out var otherCount))
            {
                intersection += Math.Min(count, otherCount);
            }
        }

        return 2.0 * intersection / total;
    }

    /// <summary>
    /// Upper bound on the Dice similarity of two sequences given only their lengths.
    /// </summary>
    public static double LengthUpperBound(int firstCount, int secondCount)
    {
        var total = firstCount + secondCount;
        return total == 0 ? 1.0 : 2.0 * Math.Min(firstCount, secondCount) / total;
    }

    private static double CalculateRenameConsistency(
        CloneFingerprint first,
        CloneFingerprint second,
        IReadOnlyList<DiffOperation> operations)
    {
        var forward = new Dictionary<string, string>(StringComparer.Ordinal);
        var backward = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = 0;
        var consistent = 0;

        foreach (var operation in operations)
        {
            if (operation.Kind != DiffOperationKind.Equal)
            {
                continue;
            }

            var token1 = first.Tokens[operation.IndexA];
            var token2 = second.Tokens[operation.IndexB];
            if (token1.Kind != NormalizedTokenKind.Identifier)
            {
                continue;
            }

            pairs++;
            var forwardOk = !forward.TryGetValue(token1.Text, out var mapped) ||
                            string.Equals(mapped, token2.Text, StringComparison.Ordinal);
            var backwardOk = !backward.TryGetValue(token2.Text, out var reverse) ||
                             string.Equals(reverse, token1.Text, StringComparison.Ordinal);

            if (forwardOk && backwardOk)
            {
                consistent++;
                forward.TryAdd(token1.Text, token2.Text);
                backward.TryAdd(token2.Text, token1.Text);
            }
        }

        return pairs == 0 ? 1.0 : (double)consistent / pairs;
    }
}

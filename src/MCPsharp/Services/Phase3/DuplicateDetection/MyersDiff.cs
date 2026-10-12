namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Kind of a single diff operation
/// </summary>
public enum DiffOperationKind
{
    /// <summary>
    /// Elements match
    /// </summary>
    Equal,

    /// <summary>
    /// Element present only in the first sequence
    /// </summary>
    Delete,

    /// <summary>
    /// Element present only in the second sequence
    /// </summary>
    Insert
}

/// <summary>
/// A single diff operation. For <see cref="DiffOperationKind.Delete"/>, <see cref="IndexB"/> is the
/// position in the second sequence at which the deletion occurs; for <see cref="DiffOperationKind.Insert"/>,
/// <see cref="IndexA"/> is the corresponding position in the first sequence.
/// </summary>
public readonly record struct DiffOperation(DiffOperationKind Kind, int IndexA, int IndexB);

/// <summary>
/// Myers O((N+M)D) shortest-edit-script diff with a bounded edit distance
/// </summary>
public static class MyersDiff
{
    /// <summary>
    /// Computes the shortest edit script between two sequences.
    /// </summary>
    /// <param name="first">First sequence</param>
    /// <param name="second">Second sequence</param>
    /// <param name="maxEditDistance">Maximum number of insertions plus deletions to explore</param>
    /// <returns>The edit script in order, or <c>null</c> if the edit distance exceeds <paramref name="maxEditDistance"/></returns>
    public static IReadOnlyList<DiffOperation>? Compute(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second,
        int maxEditDistance)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var n = first.Count;
        var m = second.Count;
        var max = Math.Min(n + m, Math.Max(0, maxEditDistance));
        var offset = max + 1;
        var v = new int[(2 * max) + 3];
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            trace.Add(Snapshot(v, offset, d));

            for (var k = -d; k <= d; k += 2)
            {
                var x = (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;

                while (x < n && y < m && string.Equals(first[x], second[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    return Backtrack(trace, n, m, d);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Dice similarity (2 * matches / total length) of two sequences, or <c>null</c> when it is
    /// below <paramref name="minimumSimilarity"/> or the edit distance exceeds <paramref name="maxEditDistance"/>.
    /// </summary>
    public static double? Similarity(
        IReadOnlyList<string> first,
        IReadOnlyList<string> second,
        double minimumSimilarity,
        int maxEditDistance)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        var total = first.Count + second.Count;
        if (total == 0)
        {
            return 1.0;
        }

        var allowed = Math.Min(maxEditDistance, MaxEditsForSimilarity(total, minimumSimilarity));
        var script = Compute(first, second, allowed);
        if (script == null)
        {
            return null;
        }

        var matches = script.Count(op => op.Kind == DiffOperationKind.Equal);
        return 2.0 * matches / total;
    }

    /// <summary>
    /// Largest edit distance compatible with a minimum Dice similarity for sequences of the given combined length
    /// </summary>
    public static int MaxEditsForSimilarity(int combinedLength, double minimumSimilarity)
    {
        var bounded = Math.Clamp(minimumSimilarity, 0.0, 1.0);
        return (int)Math.Floor((1.0 - bounded) * combinedLength);
    }

    private static int[] Snapshot(int[] v, int offset, int d)
    {
        // Entries k = -d..d of the state before iteration d
        var snapshot = new int[(2 * d) + 1];
        for (var k = -d; k <= d; k++)
        {
            var index = offset + k;
            snapshot[k + d] = index >= 0 && index < v.Length ? v[index] : 0;
        }

        return snapshot;
    }

    private static List<DiffOperation> Backtrack(List<int[]> trace, int n, int m, int editDistance)
    {
        var operations = new List<DiffOperation>(n + m);
        var x = n;
        var y = m;

        for (var d = editDistance; d > 0; d--)
        {
            var snapshot = trace[d];
            var k = x - y;
            var previousK = (k == -d || (k != d && snapshot[k - 1 + d] < snapshot[k + 1 + d]))
                ? k + 1
                : k - 1;
            var previousX = snapshot[previousK + d];
            var previousY = previousX - previousK;

            while (x > previousX && y > previousY)
            {
                operations.Add(new DiffOperation(DiffOperationKind.Equal, x - 1, y - 1));
                x--;
                y--;
            }

            operations.Add(x == previousX
                ? new DiffOperation(DiffOperationKind.Insert, x, y - 1)
                : new DiffOperation(DiffOperationKind.Delete, x - 1, y));

            x = previousX;
            y = previousY;
        }

        while (x > 0 && y > 0)
        {
            operations.Add(new DiffOperation(DiffOperationKind.Equal, x - 1, y - 1));
            x--;
            y--;
        }

        operations.Reverse();
        return operations;
    }
}

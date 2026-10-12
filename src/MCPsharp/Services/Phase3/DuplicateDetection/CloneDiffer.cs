using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Produces token- and statement-level differences between two aligned clone fingerprints
/// </summary>
public static class CloneDiffer
{
    /// <summary>Impact of a consistent identifier rename</summary>
    public const double IdentifierDifferenceImpact = 0.1;

    /// <summary>Impact of a changed literal value</summary>
    public const double LiteralDifferenceImpact = 0.2;

    /// <summary>Impact of a changed type</summary>
    public const double TypeDifferenceImpact = 0.3;

    /// <summary>Impact of a changed operator or keyword</summary>
    public const double LogicalDifferenceImpact = 0.5;

    /// <summary>Impact of a partial structural change</summary>
    public const double StructuralDifferenceImpact = 0.5;

    /// <summary>Impact of an added or removed statement</summary>
    public const double StatementDifferenceImpact = 0.6;

    private const int MaxReportedTextLength = 200;

    /// <summary>
    /// Computes the differences between two fingerprints given their alignment.
    /// Identifier, literal and type substitutions are coalesced per distinct (old, new) pair.
    /// </summary>
    public static IReadOnlyList<CodeDifference> FindDifferences(
        CloneFingerprint first,
        CloneFingerprint second,
        CloneAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(alignment);

        var operations = alignment.Operations.ToList();
        NormalizeRuns(operations, first, second);

        var substitutions = new List<Substitution>();
        var substitutionIndex = new Dictionary<(DifferenceType Type, string Text1, string Text2), Substitution>();
        var differences = new List<CodeDifference>();

        var position = 0;
        while (position < operations.Count)
        {
            var operation = operations[position];
            if (operation.Kind == DiffOperationKind.Equal)
            {
                RecordSubstitution(first.Tokens[operation.IndexA], second.Tokens[operation.IndexB], substitutions, substitutionIndex);
                position++;
                continue;
            }

            var end = position;
            while (end < operations.Count && operations[end].Kind != DiffOperationKind.Equal)
            {
                end++;
            }

            differences.AddRange(DescribeHunk(operations, position, end, first, second));
            position = end;
        }

        differences.AddRange(substitutions.Select(s => s.ToDifference()));

        return differences
            .OrderBy(d => d.Location1.Line)
            .ThenBy(d => d.Location1.Column)
            .ToList();
    }

    private static void RecordSubstitution(
        NormalizedToken token1,
        NormalizedToken token2,
        List<Substitution> substitutions,
        Dictionary<(DifferenceType Type, string Text1, string Text2), Substitution> index)
    {
        if (string.Equals(token1.Text, token2.Text, StringComparison.Ordinal))
        {
            return;
        }

        var type = token1.Kind switch
        {
            NormalizedTokenKind.Identifier => DifferenceType.Identifier,
            NormalizedTokenKind.TypeName => DifferenceType.Type,
            NormalizedTokenKind.StringLiteral or NormalizedTokenKind.NumericLiteral => DifferenceType.Literal,
            _ => DifferenceType.Logical
        };

        var key = (type, token1.Text, token2.Text);
        if (index.TryGetValue(key, out var existing))
        {
            existing.Occurrences++;
            return;
        }

        var substitution = new Substitution(type, token1, token2);
        index[key] = substitution;
        substitutions.Add(substitution);
    }

    private static List<CodeDifference> DescribeHunk(
        List<DiffOperation> operations,
        int start,
        int end,
        CloneFingerprint first,
        CloneFingerprint second)
    {
        var deleted = new List<int>();
        var inserted = new List<int>();
        for (var i = start; i < end; i++)
        {
            if (operations[i].Kind == DiffOperationKind.Delete)
            {
                deleted.Add(operations[i].IndexA);
            }
            else
            {
                inserted.Add(operations[i].IndexB);
            }
        }

        // Position in the other sequence where the hunk occurs
        var anchorA = operations[start].Kind == DiffOperationKind.Insert ? operations[start].IndexA : deleted[0];
        var anchorB = operations[start].Kind == DiffOperationKind.Delete ? operations[start].IndexB : inserted[0];
        var results = new List<CodeDifference>();

        var removedStatements = deleted.Count > 0 ? MaximalCoveredStatements(first, deleted[0], deleted[^1]) : [];
        var addedStatements = inserted.Count > 0 ? MaximalCoveredStatements(second, inserted[0], inserted[^1]) : [];

        foreach (var statement in removedStatements)
        {
            results.Add(new CodeDifference
            {
                Type = DifferenceType.StatementRemoved,
                Description = $"Statement removed: {statement.Text}",
                Location1 = new CodeLocation { Line = statement.Line, Column = statement.Column, Length = statement.Text.Length },
                Location2 = LocationAt(second, anchorB, 0),
                Text1 = statement.Text,
                Text2 = string.Empty,
                Impact = StatementDifferenceImpact
            });
        }

        foreach (var statement in addedStatements)
        {
            results.Add(new CodeDifference
            {
                Type = DifferenceType.StatementAdded,
                Description = $"Statement added: {statement.Text}",
                Location1 = LocationAt(first, anchorA, 0),
                Location2 = new CodeLocation { Line = statement.Line, Column = statement.Column, Length = statement.Text.Length },
                Text1 = string.Empty,
                Text2 = statement.Text,
                Impact = StatementDifferenceImpact
            });
        }

        var residual1 = deleted.Where(i => !IsCovered(i, removedStatements)).Select(i => first.Tokens[i]).ToList();
        var residual2 = inserted.Where(i => !IsCovered(i, addedStatements)).Select(i => second.Tokens[i]).ToList();
        if (residual1.Count > 0 || residual2.Count > 0)
        {
            results.Add(DescribeResidualChange(residual1, residual2, first, second, anchorA, anchorB));
        }

        return results;
    }

    private static CodeDifference DescribeResidualChange(
        List<NormalizedToken> residual1,
        List<NormalizedToken> residual2,
        CloneFingerprint first,
        CloneFingerprint second,
        int anchorA,
        int anchorB)
    {
        var text1 = Truncate(string.Join(" ", residual1.Select(t => t.Text)));
        var text2 = Truncate(string.Join(" ", residual2.Select(t => t.Text)));
        var isOperatorChange = residual1.Count > 0 && residual2.Count > 0 &&
                               residual1.Concat(residual2).All(t => t.Kind is NormalizedTokenKind.Punctuation or NormalizedTokenKind.Keyword);

        string description;
        if (residual1.Count > 0 && residual2.Count > 0)
        {
            description = isOperatorChange
                ? $"Operator/keyword changed from '{text1}' to '{text2}'"
                : $"Code changed from '{text1}' to '{text2}'";
        }
        else if (residual1.Count > 0)
        {
            description = $"Code removed: '{text1}'";
        }
        else
        {
            description = $"Code added: '{text2}'";
        }

        return new CodeDifference
        {
            Type = isOperatorChange ? DifferenceType.Logical : DifferenceType.Structural,
            Description = description,
            Location1 = residual1.Count > 0 ? LocationOf(residual1[0], text1.Length) : LocationAt(first, anchorA, 0),
            Location2 = residual2.Count > 0 ? LocationOf(residual2[0], text2.Length) : LocationAt(second, anchorB, 0),
            Text1 = text1,
            Text2 = text2,
            Impact = isOperatorChange ? LogicalDifferenceImpact : StructuralDifferenceImpact
        };
    }

    /// <summary>
    /// Statements whose tokens lie entirely within [start, end] and whose parent does not.
    /// </summary>
    internal static List<StatementSpan> MaximalCoveredStatements(CloneFingerprint fingerprint, int start, int end)
    {
        var covered = fingerprint.Statements
            .Where(s => s.StartTokenIndex >= start && s.EndTokenIndex <= end)
            .ToList();
        var coveredIndices = covered.Select(s => s.Index).ToHashSet();
        return covered.Where(s => !coveredIndices.Contains(s.ParentIndex)).ToList();
    }

    private static bool IsCovered(int tokenIndex, List<StatementSpan> statements) =>
        statements.Exists(s => tokenIndex >= s.StartTokenIndex && tokenIndex <= s.EndTokenIndex);

    private static int CoveredTokenCount(CloneFingerprint fingerprint, int start, int end) =>
        MaximalCoveredStatements(fingerprint, start, end).Sum(s => s.EndTokenIndex - s.StartTokenIndex + 1);

    private static CodeLocation LocationOf(NormalizedToken token, int length) =>
        new() { Line = token.Line, Column = token.Column, Length = length };

    private static CodeLocation LocationAt(CloneFingerprint fingerprint, int tokenIndex, int length)
    {
        if (fingerprint.Tokens.Count == 0)
        {
            return new CodeLocation { Line = 1, Column = 1, Length = length };
        }

        if (tokenIndex < fingerprint.Tokens.Count)
        {
            return LocationOf(fingerprint.Tokens[tokenIndex], length);
        }

        var last = fingerprint.Tokens[^1];
        return new CodeLocation { Line = last.Line, Column = last.Column + last.Text.Length, Length = length };
    }

    private static string Truncate(string text) =>
        text.Length <= MaxReportedTextLength ? text : string.Concat(text.AsSpan(0, MaxReportedTextLength), "...");

    /// <summary>
    /// Slides pure insertion/deletion runs along runs of equal shapes so that they align with
    /// statement boundaries where possible (diff output is otherwise ambiguous for repetitive code).
    /// </summary>
    private static void NormalizeRuns(List<DiffOperation> operations, CloneFingerprint first, CloneFingerprint second)
    {
        var position = 0;
        while (position < operations.Count)
        {
            if (operations[position].Kind == DiffOperationKind.Equal)
            {
                position++;
                continue;
            }

            var kind = operations[position].Kind;
            var end = position;
            while (end < operations.Count && operations[end].Kind == kind)
            {
                end++;
            }

            var isPure = end >= operations.Count || operations[end].Kind == DiffOperationKind.Equal;
            if (isPure)
            {
                position = kind == DiffOperationKind.Insert
                    ? SlideRun(operations, position, end - position, kind, second, first)
                    : SlideRun(operations, position, end - position, kind, first, second);
            }
            else
            {
                while (end < operations.Count && operations[end].Kind != DiffOperationKind.Equal)
                {
                    end++;
                }

                position = end;
            }
        }
    }

    private static int SlideRun(
        List<DiffOperation> operations,
        int start,
        int length,
        DiffOperationKind kind,
        CloneFingerprint fingerprint,
        CloneFingerprint other)
    {
        int RunIndex(DiffOperation op) => kind == DiffOperationKind.Insert ? op.IndexB : op.IndexA;
        int OtherIndex(DiffOperation op) => kind == DiffOperationKind.Insert ? op.IndexA : op.IndexB;
        string ShapeAt(int index) => fingerprint.Tokens[index].Shape;

        // Slide to the topmost equivalent position
        while (start > 0 &&
               operations[start - 1].Kind == DiffOperationKind.Equal &&
               string.Equals(ShapeAt(RunIndex(operations[start - 1])), ShapeAt(RunIndex(operations[start + length - 1])), StringComparison.Ordinal))
        {
            RotateUp(operations, start, length, kind);
            start--;
        }

        // Evaluate every downward shift: prefer covering whole statements, then fewest token substitutions
        var top = RunIndex(operations[start]);
        var anchor = OtherIndex(operations[start]);
        var maxShift = 0;
        while (start + length + maxShift < operations.Count &&
               operations[start + length + maxShift].Kind == DiffOperationKind.Equal &&
               RunIndex(operations[start + length + maxShift]) == top + length + maxShift &&
               string.Equals(ShapeAt(top + maxShift), ShapeAt(top + length + maxShift), StringComparison.Ordinal))
        {
            maxShift++;
        }

        int Mismatches(int shift)
        {
            var count = 0;
            for (var t = 0; t < maxShift; t++)
            {
                var runToken = fingerprint.Tokens[t < shift ? top + t : top + length + t];
                var otherToken = other.Tokens[anchor + t];
                if (!string.Equals(runToken.Text, otherToken.Text, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        var bestShift = 0;
        var bestCovered = CoveredTokenCount(fingerprint, top, top + length - 1);
        var bestMismatches = maxShift > 0 ? Mismatches(0) : 0;
        for (var shift = 1; shift <= maxShift; shift++)
        {
            var covered = CoveredTokenCount(fingerprint, top + shift, top + shift + length - 1);
            if (covered < bestCovered)
            {
                continue;
            }

            var mismatches = Mismatches(shift);
            if (covered > bestCovered || mismatches < bestMismatches)
            {
                bestCovered = covered;
                bestMismatches = mismatches;
                bestShift = shift;
            }
        }

        for (var shift = 0; shift < bestShift; shift++)
        {
            RotateDown(operations, start, length, kind);
            start++;
        }

        return start + length;
    }

    // Moves the Equal operation preceding the run to just after it, shifting the run up by one element
    private static void RotateUp(List<DiffOperation> operations, int start, int length, DiffOperationKind kind)
    {
        var equal = operations[start - 1];
        var last = operations[start + length - 1];
        for (var t = 0; t < length; t++)
        {
            operations[start - 1 + t] = kind == DiffOperationKind.Insert
                ? new DiffOperation(kind, equal.IndexA, equal.IndexB + t)
                : new DiffOperation(kind, equal.IndexA + t, equal.IndexB);
        }

        operations[start - 1 + length] = kind == DiffOperationKind.Insert
            ? new DiffOperation(DiffOperationKind.Equal, equal.IndexA, last.IndexB)
            : new DiffOperation(DiffOperationKind.Equal, last.IndexA, equal.IndexB);
    }

    // Moves the Equal operation following the run to just before it, shifting the run down by one element
    private static void RotateDown(List<DiffOperation> operations, int start, int length, DiffOperationKind kind)
    {
        var first = operations[start];
        var equal = operations[start + length];
        operations[start] = kind == DiffOperationKind.Insert
            ? new DiffOperation(DiffOperationKind.Equal, equal.IndexA, first.IndexB)
            : new DiffOperation(DiffOperationKind.Equal, first.IndexA, equal.IndexB);

        for (var t = 0; t < length; t++)
        {
            operations[start + 1 + t] = kind == DiffOperationKind.Insert
                ? new DiffOperation(kind, equal.IndexA + 1, first.IndexB + 1 + t)
                : new DiffOperation(kind, first.IndexA + 1 + t, equal.IndexB + 1);
        }
    }

    private sealed class Substitution(DifferenceType type, NormalizedToken token1, NormalizedToken token2)
    {
        public int Occurrences { get; set; } = 1;

        public CodeDifference ToDifference()
        {
            var subject = type switch
            {
                DifferenceType.Identifier => "Identifier",
                DifferenceType.Type => "Type",
                DifferenceType.Literal => "Literal",
                _ => "Token"
            };
            var verb = type == DifferenceType.Identifier ? "renamed" : "changed";
            var suffix = Occurrences > 1 ? $" ({Occurrences} occurrences)" : string.Empty;

            return new CodeDifference
            {
                Type = type,
                Description = $"{subject} '{token1.Text}' {verb} to '{token2.Text}'{suffix}",
                Location1 = LocationOf(token1, token1.Text.Length),
                Location2 = LocationOf(token2, token2.Text.Length),
                Text1 = token1.Text,
                Text2 = token2.Text,
                Impact = type switch
                {
                    DifferenceType.Identifier => IdentifierDifferenceImpact,
                    DifferenceType.Type => TypeDifferenceImpact,
                    DifferenceType.Literal => LiteralDifferenceImpact,
                    _ => LogicalDifferenceImpact
                }
            };
        }
    }
}

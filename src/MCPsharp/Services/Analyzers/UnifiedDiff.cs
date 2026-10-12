using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Formats Roslyn text changes as a unified diff without context lines: one hunk per group of
/// changed lines, removed lines prefixed with '-' and added lines with '+'.
/// </summary>
internal static class UnifiedDiff
{
    public static string Create(string path, SourceText oldText, IEnumerable<TextChange> changes)
    {
        var ordered = changes.OrderBy(c => c.Span.Start).ToList();
        var newText = oldText.WithChanges(ordered);
        var builder = new StringBuilder();
        builder.Append("--- ").Append(path).Append('\n');
        builder.Append("+++ ").Append(path).Append('\n');

        Hunk? hunk = null;
        var delta = 0;
        foreach (var change in ordered)
        {
            var newLength = change.NewText?.Length ?? 0;
            var newStart = change.Span.Start + delta;
            delta += newLength - change.Span.Length;

            var next = new Hunk(
                oldText.Lines.GetLineFromPosition(change.Span.Start).LineNumber,
                oldText.Lines.GetLineFromPosition(change.Span.End).LineNumber,
                newText.Lines.GetLineFromPosition(newStart).LineNumber,
                newText.Lines.GetLineFromPosition(newStart + newLength).LineNumber);

            if (hunk is { } current && next.OldFirst <= current.OldLast + 1)
            {
                hunk = new Hunk(
                    current.OldFirst,
                    Math.Max(current.OldLast, next.OldLast),
                    current.NewFirst,
                    Math.Max(current.NewLast, next.NewLast));
                continue;
            }

            if (hunk is { } finished)
            {
                AppendHunk(builder, oldText, newText, finished);
            }

            hunk = next;
        }

        if (hunk is { } last)
        {
            AppendHunk(builder, oldText, newText, last);
        }

        return builder.ToString();
    }

    private static void AppendHunk(StringBuilder builder, SourceText oldText, SourceText newText, Hunk hunk)
    {
        var oldCount = hunk.OldLast - hunk.OldFirst + 1;
        var newCount = hunk.NewLast - hunk.NewFirst + 1;
        builder.Append("@@ -").Append(hunk.OldFirst + 1).Append(',').Append(oldCount)
            .Append(" +").Append(hunk.NewFirst + 1).Append(',').Append(newCount).Append(" @@\n");

        for (var line = hunk.OldFirst; line <= hunk.OldLast; line++)
        {
            builder.Append('-').Append(oldText.Lines[line].ToString()).Append('\n');
        }

        for (var line = hunk.NewFirst; line <= hunk.NewLast; line++)
        {
            builder.Append('+').Append(newText.Lines[line].ToString()).Append('\n');
        }
    }

    /// <summary>0-based inclusive line ranges of a hunk in the old and new text</summary>
    private readonly record struct Hunk(int OldFirst, int OldLast, int NewFirst, int NewLast);
}

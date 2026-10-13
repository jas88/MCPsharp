using System.Text.RegularExpressions;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

public sealed partial class BulkEditService
{
    /// <summary>
    /// Apply a text-level refactoring. Symbol renames are applied as whole-word replacements;
    /// other refactoring types require semantic analysis and leave the content unchanged.
    /// </summary>
    private static string ApplyRefactoring(string content, BulkRefactorPattern refactorPattern)
    {
        if (refactorPattern.RefactorType != BulkRefactorType.RenameSymbol ||
            string.IsNullOrEmpty(refactorPattern.TargetPattern))
        {
            return content;
        }

        var wholeWord = $@"\b{Regex.Escape(refactorPattern.TargetPattern)}\b";
        return Regex.Replace(
            content,
            wholeWord,
            refactorPattern.ReplacementPattern.Replace("$", "$$", StringComparison.Ordinal),
            RegexOptions.None,
            BulkEditRegexTimeout);
    }

    /// <summary>
    /// Apply line/column (0-indexed) text edits to content. Edits are applied from the end of the
    /// document backwards so earlier positions stay valid. Positions beyond the end of a line or of
    /// the document are clamped to the nearest valid position (LSP semantics).
    /// </summary>
    private static string ApplyTextEdits(
        string content,
        IEnumerable<TextEdit> edits,
        List<FileChange> changes,
        CancellationToken cancellationToken)
    {
        var editList = edits.ToList();
        var lines = content.Split('\n').ToList();

        foreach (var edit in editList.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (startLine, startColumn) = ClampPosition(lines, edit.StartLine, edit.StartColumn);
            var (endLine, endColumn) = edit is InsertEdit
                ? (startLine, startColumn)
                : ClampPosition(lines, edit.EndLine, edit.EndColumn);

            if (endLine < startLine || (endLine == startLine && endColumn < startColumn))
            {
                (endLine, endColumn) = (startLine, startColumn);
            }

            var newText = edit is DeleteEdit ? string.Empty : edit.NewText;
            var merged = lines[startLine][..startColumn] + newText + lines[endLine][endColumn..];

            lines.RemoveRange(startLine, endLine - startLine + 1);
            lines.InsertRange(startLine, merged.Split('\n'));
        }

        changes.AddRange(editList.Select(ConvertTextEditToFileChange));
        return string.Join('\n', lines);
    }

    private static (int Line, int Column) ClampPosition(List<string> lines, int line, int column)
    {
        if (line >= lines.Count)
        {
            var last = lines.Count - 1;
            return (last, lines[last].Length);
        }

        var clampedLine = Math.Max(0, line);
        return (clampedLine, Math.Clamp(column, 0, lines[clampedLine].Length));
    }

    private static FileChange ConvertTextEditToFileChange(TextEdit textEdit)
    {
        return textEdit switch
        {
            ReplaceEdit replace => new FileChange
            {
                ChangeType = FileChangeType.Replace,
                StartLine = replace.StartLine,
                StartColumn = replace.StartColumn,
                EndLine = replace.EndLine,
                EndColumn = replace.EndColumn,
                NewText = replace.NewText
            },
            InsertEdit insert => new FileChange
            {
                ChangeType = FileChangeType.Insert,
                StartLine = insert.StartLine,
                StartColumn = insert.StartColumn,
                EndLine = insert.StartLine,
                EndColumn = insert.StartColumn,
                NewText = insert.NewText
            },
            DeleteEdit delete => new FileChange
            {
                ChangeType = FileChangeType.Delete,
                StartLine = delete.StartLine,
                StartColumn = delete.StartColumn,
                EndLine = delete.EndLine,
                EndColumn = delete.EndColumn
            },
            _ => throw new ArgumentException($"Unknown edit type: {textEdit.GetType()}")
        };
    }

    private static string GenerateDiff(List<FileChange> changes)
    {
        // Simple diff generation - in a real implementation, you'd use a proper diff library
        var diffLines = new List<string> { "--- Original", "+++ Modified" };

        foreach (var change in changes.Take(10)) // Limit preview size
        {
            diffLines.Add($"@@ -{change.StartLine},{change.EndLine - change.StartLine + 1} " +
                         $"+{change.StartLine},{change.EndLine - change.StartLine + 1} @@");

            if (!string.IsNullOrEmpty(change.OriginalText))
            {
                diffLines.Add($"-{change.OriginalText}");
            }

            if (!string.IsNullOrEmpty(change.NewText))
            {
                diffLines.Add($"+{change.NewText}");
            }
        }

        if (changes.Count > 10)
        {
            diffLines.Add($"... and {changes.Count - 10} more changes");
        }

        return string.Join("\n", diffLines);
    }

    private static int GetLineNumber(string content, int index)
    {
        var lineCount = 1;
        for (int i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineCount++;
            }
        }
        return lineCount - 1; // 0-indexed
    }

    private static int GetColumnNumber(string content, int index)
    {
        var column = 0;
        for (int i = index - 1; i >= 0; i--)
        {
            if (content[i] == '\n')
            {
                break;
            }
            column++;
        }
        return column;
    }
}

using MCPsharp.Models;

namespace MCPsharp.Services;

public partial class FileOperationsService
{
    /// <summary>
    /// Validate that edit positions are within bounds
    /// </summary>
    private static void ValidateEditPosition(List<string> lines, TextEdit edit)
    {
        var (startLine, startColumn, endLine, endColumn) = GetEditBounds(edit);

        // Validate line indices
        if (startLine < 0 || startLine >= lines.Count)
            throw new ArgumentOutOfRangeException(nameof(edit),
                $"StartLine {startLine} is out of range. File has {lines.Count} lines (0-{lines.Count - 1}).");

        if (endLine < 0 || endLine >= lines.Count)
            throw new ArgumentOutOfRangeException(nameof(edit),
                $"EndLine {endLine} is out of range. File has {lines.Count} lines (0-{lines.Count - 1}).");

        // Get line lengths for validation
        var startLineLength = lines[startLine].Length;
        var endLineLength = lines[endLine].Length;

        // Validate column indices
        if (startColumn < 0 || startColumn > startLineLength)
            throw new ArgumentOutOfRangeException(nameof(edit),
                $"StartColumn {startColumn} is out of range for line {startLine}. " +
                $"Line {startLine} has {startLineLength} characters. Valid positions: 0-{startLineLength - 1} for replacements, 0-{startLineLength} for insertions. " +
                $"Remember: MCPsharp uses 0-based indexing where column 0 is before the first character.");

        if (endColumn < 0 || endColumn > endLineLength)
            throw new ArgumentOutOfRangeException(nameof(edit),
                $"EndColumn {endColumn} is out of range for line {endLine}. " +
                $"Line {endLine} has {endLineLength} characters. Valid positions: 0-{endLineLength - 1} for replacements, 0-{endLineLength} for insertions. " +
                $"Remember: MCPsharp uses 0-based indexing where column 0 is before the first character.");

        // Validate that start position comes before end position
        if (startLine > endLine || (startLine == endLine && startColumn > endColumn))
            throw new ArgumentException($"Invalid edit range: start position ({startLine},{startColumn}) " +
                $"is after end position ({endLine},{endColumn}).");
    }

    /// <summary>
    /// Extract line and column bounds from any edit type
    /// </summary>
    private static (int startLine, int startColumn, int endLine, int endColumn) GetEditBounds(TextEdit edit)
    {
        return edit switch
        {
            ReplaceEdit replace => (replace.StartLine, replace.StartColumn, replace.EndLine, replace.EndColumn),
            InsertEdit insert => (insert.StartLine, insert.StartColumn, insert.StartLine, insert.StartColumn),
            DeleteEdit delete => (delete.StartLine, delete.StartColumn, delete.EndLine, delete.EndColumn),
            _ => throw new ArgumentException($"Unknown edit type: {edit.GetType().Name}")
        };
    }

    private static void ApplyEdit(List<string> lines, TextEdit edit)
    {
        // Validate edit position before applying
        ValidateEditPosition(lines, edit);

        switch (edit)
        {
            case ReplaceEdit replace:
                ApplyReplaceEdit(lines, replace);
                break;
            case InsertEdit insert:
                ApplyInsertEdit(lines, insert);
                break;
            case DeleteEdit delete:
                ApplyDeleteEdit(lines, delete);
                break;
            default:
                throw new ArgumentException($"Unknown edit type: {edit.GetType().Name}");
        }
    }

    private static void ApplyReplaceEdit(List<string> lines, ReplaceEdit edit)
    {
        if (edit.StartLine == edit.EndLine)
        {
            // Single line replace
            var line = lines[edit.StartLine];
            var before = line[..edit.StartColumn];
            var after = line[edit.EndColumn..];
            lines[edit.StartLine] = before + edit.NewText + after;
        }
        else
        {
            // Multi-line replace
            var firstLine = lines[edit.StartLine][..edit.StartColumn];
            var lastLine = lines[edit.EndLine][edit.EndColumn..];

            // Remove lines in between
            lines.RemoveRange(edit.StartLine, edit.EndLine - edit.StartLine + 1);

            // Insert new content
            var newLines = edit.NewText.Split('\n');
            if (newLines.Length == 1)
            {
                lines.Insert(edit.StartLine, firstLine + newLines[0] + lastLine);
            }
            else
            {
                lines.Insert(edit.StartLine, firstLine + newLines[0]);
                for (int i = 1; i < newLines.Length - 1; i++)
                {
                    lines.Insert(edit.StartLine + i, newLines[i]);
                }
                lines.Insert(edit.StartLine + newLines.Length - 1, newLines[^1] + lastLine);
            }
        }
    }

    private static void ApplyInsertEdit(List<string> lines, InsertEdit edit)
    {
        var line = lines[edit.StartLine];
        var before = line[..edit.StartColumn];
        var after = line[edit.StartColumn..];

        var newLines = edit.NewText.Split('\n');
        if (newLines.Length == 1)
        {
            lines[edit.StartLine] = before + newLines[0] + after;
        }
        else
        {
            lines[edit.StartLine] = before + newLines[0];
            for (int i = 1; i < newLines.Length - 1; i++)
            {
                lines.Insert(edit.StartLine + i, newLines[i]);
            }
            lines.Insert(edit.StartLine + newLines.Length - 1, newLines[^1] + after);
        }
    }

    private static void ApplyDeleteEdit(List<string> lines, DeleteEdit edit)
    {
        if (edit.StartLine == edit.EndLine)
        {
            // Single line delete
            var line = lines[edit.StartLine];
            var before = line[..edit.StartColumn];
            var after = line[edit.EndColumn..];
            lines[edit.StartLine] = before + after;
        }
        else
        {
            // Multi-line delete
            var firstLine = lines[edit.StartLine][..edit.StartColumn];
            var lastLine = lines[edit.EndLine][edit.EndColumn..];

            lines.RemoveRange(edit.StartLine, edit.EndLine - edit.StartLine + 1);
            lines.Insert(edit.StartLine, firstLine + lastLine);
        }
    }

    private static (int line, int column) GetEditStartPosition(TextEdit edit)
    {
        return edit switch
        {
            ReplaceEdit replace => (replace.StartLine, replace.StartColumn),
            InsertEdit insert => (insert.StartLine, insert.StartColumn),
            DeleteEdit delete => (delete.StartLine, delete.StartColumn),
            _ => throw new ArgumentException($"Unknown edit type: {edit.GetType().Name}")
        };
    }
}

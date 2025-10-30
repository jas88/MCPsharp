namespace MCPsharp.Models;

/// <summary>
/// Represents a text edit operation on a file
/// </summary>
public abstract class TextEdit
{
    /// <summary>
    /// The type of edit operation
    /// </summary>
    public abstract string Type { get; }
}

/// <summary>
/// Replace text in a specific range
/// </summary>
public class ReplaceEdit : TextEdit
{
    public override string Type => "replace";

    /// <summary>
    /// Starting line (0-indexed)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Starting column (0-indexed)
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// Ending line (0-indexed)
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Ending column (0-indexed)
    /// </summary>
    public required int EndColumn { get; init; }

    /// <summary>
    /// New text to replace the range
    /// </summary>
    public required string NewText { get; init; }
}

/// <summary>
/// Insert text at a specific position
/// </summary>
public class InsertEdit : TextEdit
{
    public override string Type => "insert";

    /// <summary>
    /// Line to insert at (0-indexed)
    /// </summary>
    public required int Line { get; init; }

    /// <summary>
    /// Column to insert at (0-indexed)
    /// </summary>
    public required int Column { get; init; }

    /// <summary>
    /// Text to insert
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// Delete text in a specific range
/// </summary>
public class DeleteEdit : TextEdit
{
    public override string Type => "delete";

    /// <summary>
    /// Starting line (0-indexed)
    /// </summary>
    public required int StartLine { get; init; }

    /// <summary>
    /// Starting column (0-indexed)
    /// </summary>
    public required int StartColumn { get; init; }

    /// <summary>
    /// Ending line (0-indexed)
    /// </summary>
    public required int EndLine { get; init; }

    /// <summary>
    /// Ending column (0-indexed)
    /// </summary>
    public required int EndColumn { get; init; }
}

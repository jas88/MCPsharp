namespace MCPsharp.Models;

/// <summary>
/// Represents a text edit operation on a file
/// </summary>
public abstract class TextEdit
{
    /// <summary>
    /// The file path this edit applies to
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Starting line (0-indexed)
    /// </summary>
    public required int StartLine { get; set; }

    /// <summary>
    /// Starting column (0-indexed)
    /// </summary>
    public required int StartColumn { get; set; }

    /// <summary>
    /// Ending line (0-indexed)
    /// </summary>
    public required int EndLine { get; set; }

    /// <summary>
    /// Ending column (0-indexed)
    /// </summary>
    public required int EndColumn { get; set; }

    /// <summary>
    /// New text to replace the range
    /// </summary>
    public required string NewText { get; set; }

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
}

/// <summary>
/// Insert text at a specific position
/// </summary>
public class InsertEdit : TextEdit
{
    public override string Type => "insert";
}

/// <summary>
/// Delete text in a specific range
/// </summary>
public class DeleteEdit : TextEdit
{
    public override string Type => "delete";
}

/// <summary>
/// Request for editing a file
/// </summary>
public class FileEditRequest
{
    /// <summary>
    /// Path to the file to edit
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// List of edits to apply to the file
    /// </summary>
    public required IReadOnlyList<TextEdit> Edits { get; init; }

    /// <summary>
    /// Whether to create a backup before editing
    /// </summary>
    public bool CreateBackup { get; init; } = true;

    /// <summary>
    /// Cancellation token
    /// </summary>
    public CancellationToken CancellationToken { get; init; } = default;
}

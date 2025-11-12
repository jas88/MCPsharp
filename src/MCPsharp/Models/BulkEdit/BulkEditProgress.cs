namespace MCPsharp.Models.BulkEdit;

/// <summary>
/// Progress information for bulk edit operations
/// </summary>
public class BulkEditProgress
{
    /// <summary>
    /// Current operation being performed
    /// </summary>
    public required string CurrentOperation { get; init; }

    /// <summary>
    /// Number of files processed so far
    /// </summary>
    public required int ProcessedFiles { get; init; }

    /// <summary>
    /// Total number of files to process
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Percentage complete (0-100)
    /// </summary>
    public required int PercentageComplete { get; init; }

    /// <summary>
    /// Time elapsed so far
    /// </summary>
    public required TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Estimated remaining time
    /// </summary>
    public required TimeSpan? EstimatedRemainingTime { get; init; }

    /// <summary>
    /// Current file being processed
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Additional status information
    /// </summary>
    public string? Status { get; init; }
}

/// <summary>
/// Type of change being made to a file
/// </summary>
public enum ChangeType
{
    /// <summary>
    /// Text replacement
    /// </summary>
    Replacement,

    /// <summary>
    /// Insertion of new content
    /// </summary>
    Insertion,

    /// <summary>
    /// Deletion of content
    /// </summary>
    Deletion,

    /// <summary>
    /// Structural refactoring
    /// </summary>
    Refactoring,

    /// <summary>
    /// Formatting changes only
    /// </summary>
    Formatting,

    /// <summary>
    /// Multiple types of changes
    /// </summary>
    Mixed
}

/// <summary>
/// Individual file edit operation for conditional edits
/// </summary>
public class FileEdit
{
    /// <summary>
    /// Type of edit operation to perform
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Text to replace (for replace operations)
    /// </summary>
    public string? OldText { get; init; }

    /// <summary>
    /// Text to insert (for insert/replace operations)
    /// </summary>
    public string? NewText { get; init; }

    /// <summary>
    /// Starting line number (0-indexed)
    /// </summary>
    public int? StartLine { get; init; }

    /// <summary>
    /// Ending line number (0-indexed)
    /// </summary>
    public int? EndLine { get; init; }

    /// <summary>
    /// Starting column number (0-indexed)
    /// </summary>
    public int? StartColumn { get; init; }

    /// <summary>
    /// Ending column number (0-indexed)
    /// </summary>
    public int? EndColumn { get; init; }

    /// <summary>
    /// Regular expression pattern for matching
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Replacement pattern for regex operations
    /// </summary>
    public string? Replacement { get; init; }

    /// <summary>
    /// Additional parameters for the edit operation
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Multi-file operation for batch editing (input from JSON)
/// </summary>
public class MultiFileOperation
{
    /// <summary>
    /// File pattern this operation applies to
    /// </summary>
    public required string FilePattern { get; init; }

    /// <summary>
    /// List of edits to apply to matching files (from JSON)
    /// </summary>
    public required IReadOnlyList<FileEdit> Edits { get; init; }

    /// <summary>
    /// Order priority for this operation (lower = earlier)
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// List of operations this operation depends on
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }
}

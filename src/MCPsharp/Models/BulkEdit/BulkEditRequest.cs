namespace MCPsharp.Models.BulkEdit;

/// <summary>
/// Request for bulk edit operations across multiple files
/// </summary>
public class BulkEditRequest
{
    /// <summary>
    /// Operation type to perform
    /// </summary>
    public required BulkEditOperationType OperationType { get; init; }

    /// <summary>
    /// Files to include in the bulk edit (glob patterns or absolute paths)
    /// </summary>
    public required IReadOnlyList<string> Files { get; init; }

    /// <summary>
    /// Files to exclude from the bulk edit (glob patterns or absolute paths)
    /// </summary>
    public IReadOnlyList<string>? ExcludedFiles { get; init; }

    /// <summary>
    /// Search pattern (for regex/replace operations)
    /// </summary>
    public string? SearchPattern { get; init; }

    /// <summary>
    /// Replacement text (for replace operations)
    /// </summary>
    public string? ReplacementText { get; init; }

    /// <summary>
    /// Regular expression pattern for advanced search/replace
    /// </summary>
    public string? RegexPattern { get; init; }

    /// <summary>
    /// Regex replacement pattern
    /// </summary>
    public string? RegexReplacement { get; init; }

    /// <summary>
    /// Condition for conditional edits
    /// </summary>
    public BulkEditCondition? Condition { get; init; }

    /// <summary>
    /// Refactoring pattern for structured code changes
    /// </summary>
    public BulkRefactorPattern? RefactorPattern { get; init; }

    /// <summary>
    /// Multiple edit operations for coordinated multi-file edits
    /// </summary>
    public IReadOnlyList<MultiFileEditOperation>? MultiFileEdits { get; init; }

    /// <summary>
    /// Options for the bulk edit operation
    /// </summary>
    public BulkEditOptions Options { get; init; } = new BulkEditOptions();
}

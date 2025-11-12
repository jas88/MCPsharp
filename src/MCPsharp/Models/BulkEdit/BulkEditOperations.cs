namespace MCPsharp.Models.BulkEdit;

/// <summary>
/// Types of bulk edit operations
/// </summary>
public enum BulkEditOperationType
{
    /// <summary>
    /// Replace text using regex patterns
    /// </summary>
    BulkReplace,

    /// <summary>
    /// Edit files based on content conditions
    /// </summary>
    ConditionalEdit,

    /// <summary>
    /// Pattern-based code refactoring
    /// </summary>
    BatchRefactor,

    /// <summary>
    /// Coordinated edits across multiple files
    /// </summary>
    MultiFileEdit,

    /// <summary>
    /// Preview changes without applying them
    /// </summary>
    PreviewChanges,

    /// <summary>
    /// Rollback a previous bulk edit operation
    /// </summary>
    RollbackOperation
}

/// <summary>
/// Condition for conditional edits
/// </summary>
public class BulkEditCondition
{
    /// <summary>
    /// Type of condition to check
    /// </summary>
    public required BulkConditionType ConditionType { get; init; }

    /// <summary>
    /// Pattern to match against
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Whether the condition should be inverted
    /// </summary>
    public bool Negate { get; init; }

    /// <summary>
    /// Additional parameters for the condition
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Types of conditions for conditional edits
/// </summary>
public enum BulkConditionType
{
    /// <summary>
    /// File contains specific text
    /// </summary>
    FileContains,

    /// <summary>
    /// File matches regex pattern
    /// </summary>
    FileMatches,

    /// <summary>
    /// File size is within range
    /// </summary>
    FileSize,

    /// <summary>
    /// File was modified after specific date
    /// </summary>
    FileModifiedAfter,

    /// <summary>
    /// File has specific extension
    /// </summary>
    FileExtension,

    /// <summary>
    /// File is in specific directory
    /// </summary>
    FileInDirectory,

    /// <summary>
    /// Custom condition logic
    /// </summary>
    Custom
}

/// <summary>
/// Pattern for batch refactoring operations
/// </summary>
public class BulkRefactorPattern
{
    /// <summary>
    /// Type of refactoring to perform
    /// </summary>
    public required BulkRefactorType RefactorType { get; init; }

    /// <summary>
    /// Target pattern to match
    /// </summary>
    public required string TargetPattern { get; init; }

    /// <summary>
    /// Replacement pattern
    /// </summary>
    public required string ReplacementPattern { get; init; }

    /// <summary>
    /// Additional refactoring parameters
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Types of batch refactoring operations
/// </summary>
public enum BulkRefactorType
{
    /// <summary>
    /// Rename a symbol across all files
    /// </summary>
    RenameSymbol,

    /// <summary>
    /// Change method signature
    /// </summary>
    ChangeMethodSignature,

    /// <summary>
    /// Extract code to method
    /// </summary>
    ExtractMethod,

    /// <summary>
    /// Inline method
    /// </summary>
    InlineMethod,

    /// <summary>
    /// Move namespace
    /// </summary>
    MoveNamespace,

    /// <summary>
    /// Add using statements
    /// </summary>
    AddUsing,

    /// <summary>
    /// Remove unused using statements
    /// </summary>
    RemoveUnusedUsings,

    /// <summary>
    /// Format code according to style rules
    /// </summary>
    FormatCode,

    /// <summary>
    /// Apply code fix pattern
    /// </summary>
    ApplyCodeFix
}

/// <summary>
/// Multi-file edit operation for coordinated changes
/// </summary>
public class MultiFileEditOperation
{
    /// <summary>
    /// File pattern this operation applies to
    /// </summary>
    public required string FilePattern { get; init; }

    /// <summary>
    /// List of edits to apply to matching files
    /// </summary>
    public required IReadOnlyList<TextEdit> Edits { get; init; }

    /// <summary>
    /// Order priority for this operation (lower = earlier)
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Whether this operation depends on other operations completing first
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }
}

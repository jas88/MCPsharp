namespace MCPsharp.Models;

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

/// <summary>
/// Options for bulk edit operations
/// </summary>
public class BulkEditOptions
{
    /// <summary>
    /// Maximum number of files to process in parallel
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to create backups before editing
    /// </summary>
    public bool CreateBackups { get; init; } = true;

    /// <summary>
    /// Directory to store backups in
    /// </summary>
    public string? BackupDirectory { get; init; }

    /// <summary>
    /// Whether to run in preview mode (don't apply changes)
    /// </summary>
    public bool PreviewMode { get; init; } = false;

    /// <summary>
    /// Whether to validate changes before applying
    /// </summary>
    public bool ValidateChanges { get; init; } = true;

    /// <summary>
    /// Maximum file size to process (in bytes)
    /// </summary>
    public long MaxFileSize { get; init; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Whether to include hidden files
    /// </summary>
    public bool IncludeHiddenFiles { get; init; } = false;

    /// <summary>
    /// Whether to stop on first error
    /// </summary>
    public bool StopOnFirstError { get; init; } = false;

    /// <summary>
    /// Timeout for individual file operations (in seconds)
    /// </summary>
    public int FileOperationTimeout { get; init; } = 30;

    /// <summary>
    /// Regular expression options for regex operations
    /// </summary>
    public System.Text.RegularExpressions.RegexOptions RegexOptions { get; init; } =
        System.Text.RegularExpressions.RegexOptions.Multiline |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant;

    /// <summary>
    /// Whether to preserve file timestamps
    /// </summary>
    public bool PreserveTimestamps { get; init; } = false;

    /// <summary>
    /// Custom progress reporter callback
    /// </summary>
    public Action<BulkEditProgress>? ProgressReporter { get; init; }

    /// <summary>
    /// Cancellation token for the operation
    /// </summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>
    /// Files to exclude from the operation
    /// </summary>
    public List<string> ExcludedFiles { get; init; } = new();
}
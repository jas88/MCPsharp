namespace MCPsharp.Models.BulkEdit;

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

/// <summary>
/// Options for conditional edit operations
/// </summary>
public class ConditionalEditOptions
{
    /// <summary>
    /// Whether to negate the condition (apply edits when condition is NOT met)
    /// </summary>
    public bool Negate { get; init; } = false;

    /// <summary>
    /// Whether to create backups before editing
    /// </summary>
    public bool CreateBackups { get; init; } = true;

    /// <summary>
    /// Maximum number of files to process in parallel
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to run in preview mode (don't apply changes)
    /// </summary>
    public bool PreviewMode { get; init; } = false;

    /// <summary>
    /// Files to exclude from the operation
    /// </summary>
    public IReadOnlyList<string>? ExcludedFiles { get; init; }

    /// <summary>
    /// Case sensitivity for pattern matching
    /// </summary>
    public bool CaseSensitive { get; init; } = true;

    /// <summary>
    /// Whether to use multiline mode for regex patterns
    /// </summary>
    public bool Multiline { get; init; } = true;

    /// <summary>
    /// Additional parameters for the conditional operation
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Options for batch refactoring operations
/// </summary>
public class RefactorOptions
{
    /// <summary>
    /// Whether to create backups before refactoring
    /// </summary>
    public bool CreateBackups { get; init; } = true;

    /// <summary>
    /// Maximum number of files to process in parallel
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to run in preview mode (don't apply changes)
    /// </summary>
    public bool PreviewMode { get; init; } = false;

    /// <summary>
    /// Whether to validate changes before applying
    /// </summary>
    public bool ValidateChanges { get; init; } = true;

    /// <summary>
    /// Whether to preserve file timestamps
    /// </summary>
    public bool PreserveTimestamps { get; init; } = false;

    /// <summary>
    /// Additional parameters for the refactoring operation
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Options for multi-file edit operations
/// </summary>
public class MultiFileEditOptions
{
    /// <summary>
    /// Whether to create backups before editing
    /// </summary>
    public bool CreateBackups { get; init; } = true;

    /// <summary>
    /// Maximum number of files to process in parallel
    /// </summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Whether to run in preview mode (don't apply changes)
    /// </summary>
    public bool PreviewMode { get; init; } = false;

    /// <summary>
    /// Whether to validate dependencies before applying changes
    /// </summary>
    public bool ValidateDependencies { get; init; } = true;

    /// <summary>
    /// Whether to stop on first error
    /// </summary>
    public bool StopOnFirstError { get; init; } = false;

    /// <summary>
    /// Maximum number of retry attempts for failed operations
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Delay between retry attempts in milliseconds
    /// </summary>
    public int RetryDelay { get; init; } = 1000;

    /// <summary>
    /// Additional parameters for the multi-file operation
    /// </summary>
    public Dictionary<string, object>? Parameters { get; init; }
}

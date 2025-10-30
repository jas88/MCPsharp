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

/// <summary>
/// Result of a bulk edit operation
/// </summary>
public class BulkEditResult
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Total number of files processed
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Number of files successfully modified
    /// </summary>
    public required int ModifiedFiles { get; init; }

    /// <summary>
    /// Number of files skipped
    /// </summary>
    public required int SkippedFiles { get; init; }

    /// <summary>
    /// Number of files that failed to process
    /// </summary>
    public required int FailedFiles { get; init; }

    /// <summary>
    /// List of individual file results
    /// </summary>
    public required IReadOnlyList<FileBulkEditResult> FileResults { get; init; }

    /// <summary>
    /// List of errors that occurred during processing
    /// </summary>
    public required IReadOnlyList<BulkEditError> Errors { get; init; }

    /// <summary>
    /// Total time taken for the operation
    /// </summary>
    public required TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Session ID for rollback purposes
    /// </summary>
    public string? RollbackSessionId { get; init; }

    /// <summary>
    /// Summary message
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Unique identifier for the operation
    /// </summary>
    public required string OperationId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the operation started
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// When the operation ended
    /// </summary>
    public required DateTime EndTime { get; init; }

    /// <summary>
    /// Summary of the operation results
    /// </summary>
    public BulkEditSummary? SummaryData { get; init; }

    /// <summary>
    /// Rollback information for the operation
    /// </summary>
    public RollbackInfo? RollbackInfo { get; init; }
}

// Note: BulkFileEditResult class removed to avoid duplication with FileBulkEditResult
// All file edit operations should use FileBulkEditResult consistently

/// <summary>
/// Error that occurred during bulk edit operation
/// </summary>
public class BulkEditError
{
    /// <summary>
    /// File path where the error occurred
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Error message
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Exception details if available
    /// </summary>
    public string? ExceptionDetails { get; init; }

    /// <summary>
    /// Error severity
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// Timestamp when the error occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// Preview of bulk edit changes without applying them
/// </summary>
public class BulkEditPreview
{
    /// <summary>
    /// List of files that would be modified
    /// </summary>
    public required IReadOnlyList<FileChangePreview> FileChanges { get; init; }

    /// <summary>
    /// Total number of files that would be affected
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Total number of changes across all files
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Estimated impact and risk assessment
    /// </summary>
    public required ImpactAssessment Impact { get; init; }

    /// <summary>
    /// Validation issues found during preview
    /// </summary>
    public required IReadOnlyList<ValidationIssue> ValidationIssues { get; init; }

    /// <summary>
    /// Summary of the preview
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Preview of changes for a single file
/// </summary>
public class FileChangePreview
{
    /// <summary>
    /// Path to the file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// List of changes that would be made
    /// </summary>
    public required IReadOnlyList<FileChange> Changes { get; init; }

    /// <summary>
    /// Number of changes
    /// </summary>
    public required int ChangeCount { get; init; }

    /// <summary>
    /// Risk level for this file
    /// </summary>
    public ValidationSeverity RiskLevel { get; init; } = ValidationSeverity.Warning;

    /// <summary>
    /// Whether the file would be backed up before changes
    /// </summary>
    public required bool WillCreateBackup { get; init; }
}

/// <summary>
/// Impact assessment for bulk changes
/// </summary>
public class ImpactAssessment
{
    /// <summary>
    /// Overall risk level
    /// </summary>
    public ValidationSeverity OverallRisk { get; init; } = ValidationSeverity.Warning;

    /// <summary>
    /// Number of critical files that would be modified
    /// </summary>
    public required int CriticalFilesCount { get; init; }

    /// <summary>
    /// List of potentially risky changes
    /// </summary>
    public required IReadOnlyList<string> RiskFactors { get; init; }

    /// <summary>
    /// Estimated time to apply changes
    /// </summary>
    public required TimeSpan EstimatedDuration { get; init; }

    /// <summary>
    /// Recommendations before applying changes
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; }
}

/// <summary>
/// Session for tracking rollback information
/// </summary>
public class RollbackSession
{
    /// <summary>
    /// Unique identifier for the rollback session
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Original operation type that created this session
    /// </summary>
    public required BulkEditOperationType OperationType { get; init; }

    /// <summary>
    /// When the session was created
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the session expires
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// List of files that were modified in the original operation
    /// </summary>
    public required IReadOnlyList<RollbackFileInfo> ModifiedFiles { get; init; }

    /// <summary>
    /// Directory where backups are stored
    /// </summary>
    public required string BackupDirectory { get; init; }

    /// <summary>
    /// Whether the session is still active
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// Description of the original operation
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Metadata about the original operation
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Information about a file that can be rolled back
/// </summary>
public class RollbackFileInfo
{
    /// <summary>
    /// Path to the original file
    /// </summary>
    public required string OriginalPath { get; set; }

    /// <summary>
    /// Path to the backup file
    /// </summary>
    public required string BackupPath { get; set; }

    /// <summary>
    /// Whether the file was newly created (should be deleted on rollback)
    /// </summary>
    public required bool WasCreated { get; set; }

    /// <summary>
    /// Whether the file was deleted (should be restored on rollback)
    /// </summary>
    public required bool WasDeleted { get; set; }

    /// <summary>
    /// Checksum of the original file for integrity verification
    /// </summary>
    public string? OriginalChecksum { get; set; }

    /// <summary>
    /// Checksum of the backup file for integrity verification
    /// </summary>
    public string? BackupChecksum { get; set; }

    /// <summary>
    /// Size of the backup file in bytes
    /// </summary>
    public long BackupSize { get; set; }

    // Additional properties for compatibility with RollbackService

    /// <summary>
    /// Whether the backup file exists
    /// </summary>
    public bool BackupExists => !string.IsNullOrEmpty(BackupPath) && File.Exists(BackupPath);

    /// <summary>
    /// Whether the file integrity was verified
    /// </summary>
    public bool IntegrityVerified { get; set; } = false;

    /// <summary>
    /// Expected checksum for verification
    /// </summary>
    public string? ExpectedChecksum { get; set; }

    /// <summary>
    /// Actual checksum of backup file
    /// </summary>
    public string? ActualChecksum { get; set; }

    /// <summary>
    /// Error message if verification failed
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Result of a rollback operation
/// </summary>
public class RollbackResult
{
    /// <summary>
    /// Whether the rollback was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Session ID that was rolled back
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Total number of files that needed to be rolled back
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Number of files successfully rolled back
    /// </summary>
    public required int SuccessfulRollbacks { get; init; }

    /// <summary>
    /// Number of files that failed to rollback
    /// </summary>
    public required int FailedRollbacks { get; init; }

    /// <summary>
    /// List of individual file rollback results
    /// </summary>
    public required IReadOnlyList<FileRollbackResult> FileResults { get; init; }

    /// <summary>
    /// List of errors that occurred during rollback
    /// </summary>
    public required IReadOnlyList<RollbackError> Errors { get; init; }

    /// <summary>
    /// Time taken for the rollback operation
    /// </summary>
    public required TimeSpan ElapsedTime { get; init; }

    /// <summary>
    /// Whether backup files were cleaned up after successful rollback
    /// </summary>
    public required bool CleanupCompleted { get; init; }

    /// <summary>
    /// Summary message
    /// </summary>
    public string? Summary { get; init; }
}

/// <summary>
/// Result of rolling back a single file
/// </summary>
public class FileRollbackResult
{
    /// <summary>
    /// Path to the file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the rollback was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Type of rollback operation performed
    /// </summary>
    public required RollbackOperationType OperationType { get; init; }

    /// <summary>
    /// Error message if rollback failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Time taken for this file rollback
    /// </summary>
    public required TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// Type of rollback operation performed
/// </summary>
public enum RollbackOperationType
{
    /// <summary>
    /// Restored file from backup
    /// </summary>
    RestoredFromBackup,

    /// <summary>
    /// Deleted newly created file
    /// </summary>
    DeletedNewFile,

    /// <summary>
    /// Restored previously deleted file
    /// </summary>
    RestoredDeletedFile,

    /// <summary>
    /// No action needed (file was unchanged)
    /// </summary>
    NoAction
}

/// <summary>
/// Error that occurred during rollback
/// </summary>
public class RollbackError
{
    /// <summary>
    /// File path where the error occurred
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Error message
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Exception details if available
    /// </summary>
    public string? ExceptionDetails { get; init; }

    /// <summary>
    /// Whether the error is recoverable
    /// </summary>
    public required bool IsRecoverable { get; init; }

    /// <summary>
    /// Timestamp when the error occurred
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

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
/// Result of previewing bulk changes before applying them
/// </summary>
public class PreviewResult
{
    /// <summary>
    /// Whether the preview was generated successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if preview generation failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Files that would be modified
    /// </summary>
    public required IReadOnlyList<FilePreviewResult> FilePreviews { get; init; } = new List<FilePreviewResult>();

    /// <summary>
    /// Summary of the preview results
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Impact assessment for the changes
    /// </summary>
    public required ImpactEstimate Impact { get; init; }

    /// <summary>
    /// Unique identifier for this preview session
    /// </summary>
    public required string PreviewId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the preview was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Preview of changes for a single file
/// </summary>

/// <summary>
/// Summary of preview results
/// </summary>
public class PreviewSummary
{
    /// <summary>
    /// Total files in the preview
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Files that would be changed
    /// </summary>
    public required int FilesToChange { get; init; }

    /// <summary>
    /// Files that would be skipped
    /// </summary>
    public required int FilesToSkip { get; init; }

    /// <summary>
    /// Total changes across all files
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Number of lines added
    /// </summary>
    public required int LinesAdded { get; init; }

    /// <summary>
    /// Number of lines removed
    /// </summary>
    public required int LinesRemoved { get; init; }

    /// <summary>
    /// Change in file size in bytes
    /// </summary>
    public required long SizeChange { get; init; }
}

/// <summary>
/// Risk item for impact assessment
/// </summary>
public class RiskItem
{
    /// <summary>
    /// Type of risk
    /// </summary>
    public required RiskType Type { get; init; }

    /// <summary>
    /// Description of the risk
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Files affected by this risk
    /// </summary>
    public required IReadOnlyList<string> AffectedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Severity of the risk
    /// </summary>
    public required RiskSeverity Severity { get; init; }

    /// <summary>
    /// Mitigation strategy
    /// </summary>
    public string? Mitigation { get; init; }
}

/// <summary>
/// Types of risks
/// </summary>
public enum RiskType
{
    /// <summary>
    /// Compilation breaking changes
    /// </summary>
    Compilation,

    /// <summary>
    /// Dependency issues
    /// </summary>
    Dependencies,

    /// <summary>
    /// Performance impact
    /// </summary>
    Performance,

    /// <summary>
    /// File access issues
    /// </summary>
    FileAccessIssue,

    /// <summary>
    /// Other types of risks
    /// </summary>
    Other
}

/// <summary>
/// Risk severity levels
/// </summary>
public enum RiskSeverity
{
    /// <summary>
    /// Low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Critical risk
    /// </summary>
    Critical
}

/// <summary>
/// Change risk levels (different from RiskLevel for compatibility)
/// </summary>
public enum ChangeRiskLevel
{
    /// <summary>
    /// Very low risk
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk
    /// </summary>
    High,

    /// <summary>
    /// Critical risk
    /// </summary>
    Critical
}

/// <summary>
/// Complexity estimate for operations
/// </summary>
public class ComplexityEstimate
{
    /// <summary>
    /// Complexity score (0-100)
    /// </summary>
    public required int Score { get; init; }

    /// <summary>
    /// Complexity level description
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// Factors contributing to complexity
    /// </summary>
    public required IReadOnlyList<string> Factors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Estimated time to complete
    /// </summary>
    public required TimeSpan EstimatedTime { get; init; }
}

/// <summary>
/// Information about a rollback session
/// </summary>
public class RollbackInfo
{
    /// <summary>
    /// Unique identifier for the rollback session
    /// </summary>
    public required string RollbackId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Original operation that can be rolled back
    /// </summary>
    public required string OriginalOperationId { get; set; }

    /// <summary>
    /// Type of operation that was performed
    /// </summary>
    public required string OperationType { get; set; }

    /// <summary>
    /// Files that were modified
    /// </summary>
    public required IReadOnlyList<string> ModifiedFiles { get; set; } = new List<string>();

    /// <summary>
    /// Backup locations for each file
    /// </summary>
    public required IReadOnlyDictionary<string, string> BackupLocations { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// When the original operation was performed
    /// </summary>
    public required DateTime OperationTimestamp { get; set; }

    /// <summary>
    /// When this rollback expires (if applicable)
    /// </summary>
    public required DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Size of all backups combined
    /// </summary>
    public required long TotalBackupSize { get; set; }

    /// <summary>
    /// Whether rollback is still possible
    /// </summary>
    public required bool IsRollbackPossible { get; set; }

    /// <summary>
    /// Directory where rollback files are stored
    /// </summary>
    public required string RollbackDirectory { get; set; }

    /// <summary>
    /// List of file information for rollback - this can be set directly by RollbackService
    /// </summary>
    public IReadOnlyList<RollbackFileInfo> Files
    {
        get => _files ?? ModifiedFiles.Select(file => new RollbackFileInfo
        {
            OriginalPath = file,
            BackupPath = BackupLocations.GetValueOrDefault(file, string.Empty),
            WasCreated = false,
            WasDeleted = false
        }).ToList();
        set => _files = value;
    }
    private IReadOnlyList<RollbackFileInfo>? _files;

    // Additional properties expected by RollbackService

    /// <summary>
    /// Operation ID (alias for OriginalOperationId for RollbackService compatibility)
    /// </summary>
    public string OperationId => OriginalOperationId;

    /// <summary>
    /// Rollback size (alias for TotalBackupSize for RollbackService compatibility)
    /// </summary>
    public long RollbackSize => TotalBackupSize;

    /// <summary>
    /// Whether rollback can be performed (alias for IsRollbackPossible for RollbackService compatibility)
    /// </summary>
    public bool CanRollback => IsRollbackPossible && (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);

    // Additional properties for compatibility with other code

    /// <summary>
    /// Timestamp (alias for OperationTimestamp for compatibility)
    /// </summary>
    public DateTime Timestamp => OperationTimestamp;

    /// <summary>
    /// Number of files affected (alias for ModifiedFiles.Count for compatibility)
    /// </summary>
    public int FilesAffected => ModifiedFiles.Count;

    /// <summary>
    /// Total number of changes (estimated based on files for compatibility)
    /// </summary>
    public int ChangesCount => Files.Count;

    /// <summary>
    /// Description of the operation (generated from operation type for compatibility)
    /// </summary>
    public string Description => $"{OperationType} operation affecting {FilesAffected} files";
}

/// <summary>
/// Estimate of the impact and complexity of a bulk operation
/// </summary>
public class ImpactEstimate
{
    /// <summary>
    /// Overall risk level for the operation
    /// </summary>
    public required ChangeRiskLevel OverallRisk { get; init; }

    /// <summary>
    /// List of specific risks identified
    /// </summary>
    public required IReadOnlyList<RiskItem> Risks { get; init; } = Array.Empty<RiskItem>();

    /// <summary>
    /// Complexity estimate for the operation
    /// </summary>
    public required ComplexityEstimate Complexity { get; init; }

    /// <summary>
    /// Recommendations for safe execution
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    // Legacy properties for backward compatibility

    /// <summary>
    /// Overall complexity level (legacy)
    /// </summary>
    public ComplexityLevel ComplexityLevel => OverallRisk switch
    {
        ChangeRiskLevel.Critical => ComplexityLevel.VeryComplex,
        ChangeRiskLevel.High => ComplexityLevel.Complex,
        ChangeRiskLevel.Medium => ComplexityLevel.Moderate,
        _ => ComplexityLevel.Simple
    };

    /// <summary>
    /// Estimated number of files to be modified (legacy)
    /// </summary>
    public int EstimatedFilesAffected => Risks.SelectMany(r => r.AffectedFiles).Distinct().Count();

    /// <summary>
    /// Estimated number of total changes (legacy)
    /// </summary>
    public int EstimatedTotalChanges => Risks.Count;

    /// <summary>
    /// Estimated time to complete the operation (legacy)
    /// </summary>
    public TimeSpan EstimatedDuration => Complexity.EstimatedTime;

    /// <summary>
    /// Risk assessment (legacy)
    /// </summary>
    public RiskLevel RiskLevel => OverallRisk switch
    {
        ChangeRiskLevel.Critical => RiskLevel.Critical,
        ChangeRiskLevel.High => RiskLevel.High,
        ChangeRiskLevel.Medium => RiskLevel.Medium,
        _ => RiskLevel.Low
    };

    /// <summary>
    /// Files that might be affected but are uncertain (legacy)
    /// </summary>
    public IReadOnlyList<string> UncertainFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Potential breaking changes (legacy)
    /// </summary>
    public IReadOnlyList<string> BreakingChanges { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Dependencies that might be affected (legacy)
    /// </summary>
    public IReadOnlyList<string> AffectedDependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Confidence level in the estimate (legacy)
    /// </summary>
    public double ConfidenceLevel { get; init; } = 0.8;
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
/// Complexity levels for operations
/// </summary>
public enum ComplexityLevel
{
    /// <summary>
    /// Simple, low-risk operation
    /// </summary>
    Simple,

    /// <summary>
    /// Moderate complexity
    /// </summary>
    Moderate,

    /// <summary>
    /// Complex operation requiring care
    /// </summary>
    Complex,

    /// <summary>
    /// Very complex, high-risk operation
    /// </summary>
    VeryComplex
}

/// <summary>
/// Risk levels for operations
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Very low risk
    /// </summary>
    Low,

    /// <summary>
    /// Moderate risk
    /// </summary>
    Medium,

    /// <summary>
    /// High risk operation
    /// </summary>
    High,

    /// <summary>
    /// Very high risk, potentially breaking
    /// </summary>
    Critical
}

/// <summary>
/// Result of editing a single file in a bulk operation
/// </summary>
public class FileBulkEditResult
{
    /// <summary>
    /// Path to the file that was edited
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the edit was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Number of changes made to the file
    /// </summary>
    public required int ChangesCount { get; init; }

    /// <summary>
    /// Number of changes applied
    /// </summary>
    public required int ChangesApplied { get; init; }

    /// <summary>
    /// Change count (alias for ChangesApplied for compatibility)
    /// </summary>
    public int ChangeCount => ChangesApplied;

    /// <summary>
    /// Whether the file was skipped
    /// </summary>
    public required bool Skipped { get; init; }

    /// <summary>
    /// Reason for skipping the file
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Error message if the edit failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Size of the file before editing
    /// </summary>
    public required long OriginalSize { get; init; }

    /// <summary>
    /// Size of the file after editing
    /// </summary>
    public required long NewSize { get; init; }

    /// <summary>
    /// When the processing started
    /// </summary>
    public required DateTime ProcessStartTime { get; init; }

    /// <summary>
    /// When the processing ended
    /// </summary>
    public required DateTime ProcessEndTime { get; init; }

    /// <summary>
    /// Time taken to edit this file
    /// </summary>
    public required TimeSpan EditDuration { get; init; }

    /// <summary>
    /// Processing duration (alias for EditDuration)
    /// </summary>
    public TimeSpan ProcessDuration => EditDuration;

    /// <summary>
    /// Processing time (alias for EditDuration for service compatibility)
    /// </summary>
    public TimeSpan ProcessingTime => EditDuration;

    /// <summary>
    /// List of changes made to the file
    /// </summary>
    public IReadOnlyList<FileChange>? Changes { get; init; }

    /// <summary>
    /// Whether a backup was created
    /// </summary>
    public required bool BackupCreated { get; init; }

    /// <summary>
    /// Path to the backup file if created
    /// </summary>
    public string? BackupPath { get; init; }

    /// <summary>
    /// Hash of the original file for integrity verification
    /// </summary>
    public string? OriginalHash { get; init; }

    /// <summary>
    /// Hash of the modified file for integrity verification
    /// </summary>
    public string? NewHash { get; init; }
}

/// <summary>
/// Summary of bulk edit operation statistics
/// </summary>
public class BulkEditSummary
{
    /// <summary>
    /// Total number of files matched by the operation criteria
    /// </summary>
    public required int TotalFilesMatched { get; init; }

    /// <summary>
    /// Total number of files processed
    /// </summary>
    public required int TotalFilesProcessed { get; init; }

    /// <summary>
    /// Number of files successfully modified
    /// </summary>
    public required int SuccessfulFiles { get; init; }

    /// <summary>
    /// Number of files that failed to process
    /// </summary>
    public required int FailedFiles { get; init; }

    /// <summary>
    /// Number of files that were skipped
    /// </summary>
    public required int SkippedFiles { get; init; }

    /// <summary>
    /// Total number of changes applied across all files
    /// </summary>
    public required int TotalChangesApplied { get; init; }

    /// <summary>
    /// Total bytes processed
    /// </summary>
    public required long TotalBytesProcessed { get; init; }

    /// <summary>
    /// Total bytes written to disk
    /// </summary>
    public required long TotalBytesWritten { get; init; }

    /// <summary>
    /// Number of backup files created
    /// </summary>
    public required int BackupsCreated { get; init; }

    /// <summary>
    /// Average processing time per file
    /// </summary>
    public required TimeSpan AverageProcessingTime { get; init; }

    /// <summary>
    /// Files processed per second
    /// </summary>
    public required double FilesPerSecond { get; init; }

    /// <summary>
    /// Number of files that would be changed (for preview operations)
    /// </summary>
    public int FilesToChange { get; init; }

    /// <summary>
    /// Number of files that would be skipped (for preview operations)
    /// </summary>
    public int FilesToSkip { get; init; }

    /// <summary>
    /// Number of lines added (for preview operations)
    /// </summary>
    public int LinesAdded { get; init; }

    /// <summary>
    /// Number of lines removed (for preview operations)
    /// </summary>
    public int LinesRemoved { get; init; }

    /// <summary>
    /// When the operation started
    /// </summary>
    public DateTime? StartTime { get; init; }

    /// <summary>
    /// When the operation ended
    /// </summary>
    public DateTime? EndTime { get; init; }

    /// <summary>
    /// Total duration of the operation
    /// </summary>
    public TimeSpan? TotalDuration { get; init; }
}

/// <summary>
/// Result of previewing changes to a single file
/// </summary>
public class FilePreviewResult
{
    /// <summary>
    /// Path to the file being previewed
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the preview was generated successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if preview generation failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// List of changes that would be made
    /// </summary>
    public required IReadOnlyList<FileChange> Changes { get; init; } = new List<FileChange>();

    /// <summary>
    /// Total number of changes
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Estimated risk level for this file
    /// </summary>
    public required RiskLevel RiskLevel { get; init; }

    /// <summary>
    /// Lines that would be affected
    /// </summary>
    public required IReadOnlyList<int> AffectedLines { get; init; } = new List<int>();

    /// <summary>
    /// Whether the file is writable
    /// </summary>
    public required bool IsWritable { get; init; }

    /// <summary>
    /// Whether the file is under source control
    /// </summary>
    public required bool IsUnderSourceControl { get; init; }

    /// <summary>
    /// Size of the file in bytes
    /// </summary>
    public required long FileSize { get; init; }

    /// <summary>
    /// When the preview was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the file would be changed
    /// </summary>
    public required bool WouldChange { get; init; }

    /// <summary>
    /// Number of changes that would be made (alias for TotalChanges)
    /// </summary>
    public int ChangeCount => TotalChanges;

    /// <summary>
    /// Reason why the file would be skipped
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Diff preview of the changes
    /// </summary>
    public string? DiffPreview { get; init; }

    /// <summary>
    /// Planned changes for this file
    /// </summary>
    public IReadOnlyList<FileChange>? PlannedChanges { get; init; }
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
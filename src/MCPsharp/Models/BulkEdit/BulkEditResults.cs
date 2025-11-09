namespace MCPsharp.Models.BulkEdit;

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

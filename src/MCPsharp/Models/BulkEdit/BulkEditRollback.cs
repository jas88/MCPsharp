namespace MCPsharp.Models.BulkEdit;

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

using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for managing backup and rollback operations for bulk edits
/// </summary>
public interface IRollbackService
{
    /// <summary>
    /// Create a backup snapshot before bulk operations
    /// </summary>
    /// <param name="operationId">ID of the operation being backed up</param>
    /// <param name="files">List of files to create backups for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rollback information for the backup session</returns>
    Task<RollbackInfo> CreateBackupAsync(
        string operationId,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback to a previous snapshot using rollback ID
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the rollback operation</returns>
    Task<RollbackResult> RollbackAsync(
        string rollbackId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get information about a specific rollback session
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rollback information or null if not found</returns>
    Task<RollbackInfo?> GetRollbackInfoAsync(
        string rollbackId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all available rollback sessions
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available rollback sessions</returns>
    Task<IReadOnlyList<RollbackInfo>> GetAvailableRollbacksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up expired rollback sessions and their backup files
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of sessions cleaned up</returns>
    Task<int> CleanupExpiredRollbacksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific rollback session and its backup files
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully deleted</returns>
    Task<bool> DeleteRollbackAsync(
        string rollbackId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify the integrity of backup files in a rollback session
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session to verify</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Verification result with details about any issues</returns>
    Task<RollbackVerificationResult> VerifyRollbackIntegrityAsync(
        string rollbackId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get rollback history and statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rollback history and statistics</returns>
    Task<RollbackHistory> GetRollbackHistoryAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a rollback session is valid and can be used
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if rollback is possible</returns>
    Task<bool> CanRollbackAsync(
        string rollbackId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the space usage of rollback operations
    /// </summary>
    /// <param name="files">Files to estimate backup size for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Estimated space usage in bytes</returns>
    Task<long> EstimateBackupSpaceAsync(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Export rollback metadata for external storage/archiving
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session to export</param>
    /// <param name="exportPath">Path to export the metadata to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if export was successful</returns>
    Task<bool> ExportRollbackMetadataAsync(
        string rollbackId,
        string exportPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Import rollback metadata from external storage
    /// </summary>
    /// <param name="importPath">Path to import the metadata from</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Rollback information if import was successful</returns>
    Task<RollbackInfo?> ImportRollbackMetadataAsync(
        string importPath,
        CancellationToken cancellationToken = default);
}


/// <summary>
/// Result of rolling back a single file
/// </summary>
public class RollbackFileResult
{
    /// <summary>
    /// Original file path
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the rollback for this file succeeded
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the rollback failed for this file
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Whether this file was skipped
    /// </summary>
    public required bool Skipped { get; init; }

    /// <summary>
    /// Reason why this file was skipped
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Whether backup file integrity was verified
    /// </summary>
    public required bool IntegrityVerified { get; init; }

    /// <summary>
    /// Original file checksum for verification
    /// </summary>
    public string? OriginalChecksum { get; init; }

    /// <summary>
    /// Backup file checksum for verification
    /// </summary>
    public string? BackupChecksum { get; init; }

    /// <summary>
    /// Size of the file after rollback
    /// </summary>
    public required long FinalSize { get; init; }

    /// <summary>
    /// When processing started for this file
    /// </summary>
    public required DateTime ProcessStartTime { get; init; }

    /// <summary>
    /// When processing completed for this file
    /// </summary>
    public required DateTime ProcessEndTime { get; init; }

    /// <summary>
    /// Duration of processing for this file
    /// </summary>
    public TimeSpan ProcessDuration => ProcessEndTime - ProcessStartTime;
}

/// <summary>
/// Result of rollback integrity verification
/// </summary>
public class RollbackVerificationResult
{
    /// <summary>
    /// Whether the overall verification succeeded
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// List of verification results for each file
    /// </summary>
    public required IReadOnlyList<FileIntegrityResult> FileResults { get; init; }

    /// <summary>
    /// Number of files with verified integrity
    /// </summary>
    public required int VerifiedFiles { get; init; }

    /// <summary>
    /// Number of files with integrity issues
    /// </summary>
    public required int CorruptedFiles { get; init; }

    /// <summary>
    /// Number of files with missing backups
    /// </summary>
    public required int MissingFiles { get; init; }

    /// <summary>
    /// When verification started
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// When verification completed
    /// </summary>
    public required DateTime EndTime { get; init; }

    /// <summary>
    /// Total duration of verification
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Result of integrity verification for a single file
/// </summary>
public class FileIntegrityResult
{
    /// <summary>
    /// File path
    /// </summary>
    public required string FilePath { get; set; }

    /// <summary>
    /// Whether the file integrity was verified
    /// </summary>
    public required bool IntegrityVerified { get; set; }

    /// <summary>
    /// Whether the backup file exists
    /// </summary>
    public required bool BackupExists { get; set; }

    /// <summary>
    /// Expected checksum
    /// </summary>
    public required string ExpectedChecksum { get; set; }

    /// <summary>
    /// Actual checksum of backup file
    /// </summary>
    public string? ActualChecksum { get; set; }

    /// <summary>
    /// Error message if verification failed
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Size of the backup file
    /// </summary>
    public required long BackupSize { get; set; }
}

/// <summary>
/// Rollback history and statistics
/// </summary>
public class RollbackHistory
{
    /// <summary>
    /// List of all rollback sessions (active and expired)
    /// </summary>
    public required IReadOnlyList<RollbackInfo> AllRollbacks { get; init; }

    /// <summary>
    /// Number of active rollback sessions
    /// </summary>
    public required int ActiveRollbacks { get; init; }

    /// <summary>
    /// Number of expired rollback sessions
    /// </summary>
    public required int ExpiredRollbacks { get; init; }

    /// <summary>
    /// Total space used by all rollback backups
    /// </summary>
    public required long TotalSpaceUsed { get; init; }

    /// <summary>
    /// Number of files currently backed up
    /// </summary>
    public required int TotalBackedUpFiles { get; init; }

    /// <summary>
    /// Oldest rollback session date
    /// </summary>
    public DateTime? OldestRollback { get; init; }

    /// <summary>
    /// Newest rollback session date
    /// </summary>
    public DateTime? NewestRollback { get; init; }

    /// <summary>
    /// When the history was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; }
}
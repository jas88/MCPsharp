using System.IO;

namespace MCPsharp.Models;

/// <summary>
/// Result of a bulk edit operation
/// </summary>
public class BulkEditResult
{
    /// <summary>
    /// Whether the overall operation succeeded
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// List of results for each file processed
    /// </summary>
    public required IReadOnlyList<FileBulkEditResult> FileResults { get; init; }

    /// <summary>
    /// Summary statistics for the operation
    /// </summary>
    public required BulkEditSummary Summary { get; init; }

    /// <summary>
    /// Rollback information if the operation succeeded
    /// </summary>
    public RollbackInfo? RollbackInfo { get; init; }

    /// <summary>
    /// Operation ID for tracking
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// When the operation started
    /// </summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// When the operation completed
    /// </summary>
    public required DateTime EndTime { get; init; }

    /// <summary>
    /// Total duration of the operation
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Result of bulk edit operation for a single file
/// </summary>
public class FileBulkEditResult
{
    /// <summary>
    /// File path that was processed
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the operation on this file succeeded
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the operation failed for this file
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Number of changes applied to this file
    /// </summary>
    public int ChangesApplied { get; init; }

    /// <summary>
    /// List of specific changes made to this file
    /// </summary>
    public IReadOnlyList<FileChange>? Changes { get; init; }

    /// <summary>
    /// Whether this file was skipped
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>
    /// Reason why this file was skipped
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Backup file path if backup was created
    /// </summary>
    public string? BackupPath { get; init; }

    /// <summary>
    /// File size before the operation
    /// </summary>
    public long OriginalSize { get; init; }

    /// <summary>
    /// File size after the operation
    /// </summary>
    public long NewSize { get; init; }

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
/// Summary statistics for a bulk edit operation
/// </summary>
public class BulkEditSummary
{
    /// <summary>
    /// Total files that matched the criteria
    /// </summary>
    public required int TotalFilesMatched { get; init; }

    /// <summary>
    /// Total files processed
    /// </summary>
    public required int TotalFilesProcessed { get; init; }

    /// <summary>
    /// Files successfully processed
    /// </summary>
    public required int SuccessfulFiles { get; init; }

    /// <summary>
    /// Files that failed to process
    /// </summary>
    public required int FailedFiles { get; init; }

    /// <summary>
    /// Files that were skipped
    /// </summary>
    public required int SkippedFiles { get; init; }

    /// <summary>
    /// Total changes applied across all files
    /// </summary>
    public required int TotalChangesApplied { get; init; }

    /// <summary>
    /// Total bytes processed
    /// </summary>
    public required long TotalBytesProcessed { get; init; }

    /// <summary>
    /// Total bytes written (for write operations)
    /// </summary>
    public required long TotalBytesWritten { get; init; }

    /// <summary>
    /// Number of backups created
    /// </summary>
    public required int BackupsCreated { get; init; }

    /// <summary>
    /// Average processing time per file
    /// </summary>
    public TimeSpan AverageProcessingTime { get; init; }

    /// <summary>
    /// Files processed per second
    /// </summary>
    public double FilesPerSecond { get; init; }

    /// <summary>
    /// Validation issues found (if validation was enabled)
    /// </summary>
    public IReadOnlyList<ValidationIssue>? ValidationIssues { get; init; }
}

/// <summary>
/// Information needed to rollback a bulk edit operation
/// </summary>
public class RollbackInfo
{
    /// <summary>
    /// Unique identifier for this rollback session
    /// </summary>
    public required string RollbackId { get; init; }

    /// <summary>
    /// Operation ID that can be rolled back
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Directory containing rollback information
    /// </summary>
    public required string RollbackDirectory { get; init; }

    /// <summary>
    /// List of files that can be rolled back
    /// </summary>
    public required IReadOnlyList<RollbackFileInfo> Files { get; init; }

    /// <summary>
    /// When the rollback information expires
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// Whether rollback is still possible
    /// </summary>
    public bool CanRollback => DateTime.UtcNow < ExpiresAt && Directory.Exists(RollbackDirectory);

    /// <summary>
    /// Size of rollback data in bytes
    /// </summary>
    public long RollbackSize { get; init; }
}

/// <summary>
/// Information about a file that can be rolled back
/// </summary>
public class RollbackFileInfo
{
    /// <summary>
    /// Original file path
    /// </summary>
    public required string OriginalPath { get; init; }

    /// <summary>
    /// Path to the backup file
    /// </summary>
    public required string BackupPath { get; init; }

    /// <summary>
    /// Checksum of the original file for integrity verification
    /// </summary>
    public required string OriginalChecksum { get; init; }

    /// <summary>
    /// Checksum of the backup file for integrity verification
    /// </summary>
    public required string BackupChecksum { get; init; }

    /// <summary>
    /// Whether the backup file exists
    /// </summary>
    public bool BackupExists => File.Exists(BackupPath);

    /// <summary>
    /// Size of the backup file
    /// </summary>
    public long BackupSize { get; init; }
}

/// <summary>
/// Result of a preview operation
/// </summary>
public class PreviewResult
{
    /// <summary>
    /// Whether the preview was generated successfully
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if preview failed
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// List of previewed changes for each file
    /// </summary>
    public required IReadOnlyList<FilePreviewResult> FilePreviews { get; init; }

    /// <summary>
    /// Summary of what changes would be made
    /// </summary>
    public required PreviewSummary Summary { get; init; }

    /// <summary>
    /// Estimated impact of the changes
    /// </summary>
    public required ImpactEstimate Impact { get; init; }

    /// <summary>
    /// Preview ID for reference
    /// </summary>
    public required string PreviewId { get; init; }

    /// <summary>
    /// When the preview was generated
    /// </summary>
    public required DateTime GeneratedAt { get; init; }
}

/// <summary>
/// Preview of changes for a single file
/// </summary>
public class FilePreviewResult
{
    /// <summary>
    /// File path being previewed
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether changes would be applied to this file
    /// </summary>
    public required bool WouldChange { get; init; }

    /// <summary>
    /// Number of changes that would be applied
    /// </summary>
    public required int ChangeCount { get; init; }

    /// <summary>
    /// Preview of changes (diff format)
    /// </summary>
    public string? DiffPreview { get; init; }

    /// <summary>
    /// List of specific changes that would be made
    /// </summary>
    public IReadOnlyList<FileChange>? PlannedChanges { get; init; }

    /// <summary>
    /// Reason why this file would be skipped
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Risk level of changes to this file
    /// </summary>
    public ChangeRiskLevel RiskLevel { get; init; } = ChangeRiskLevel.Low;
}

/// <summary>
/// Summary of preview results
/// </summary>
public class PreviewSummary
{
    /// <summary>
    /// Total files that would be processed
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
    /// Total changes that would be applied
    /// </summary>
    public required int TotalChanges { get; init; }

    /// <summary>
    /// Estimated lines to be added
    /// </summary>
    public required int LinesAdded { get; init; }

    /// <summary>
    /// Estimated lines to be removed
    /// </summary>
    public required int LinesRemoved { get; init; }

    /// <summary>
    /// Estimated size change in bytes
    /// </summary>
    public required long SizeChange { get; init; }
}

/// <summary>
/// Estimate of the impact of the changes
/// </summary>
public class ImpactEstimate
{
    /// <summary>
    /// Overall risk level of the changes
    /// </summary>
    public required ChangeRiskLevel OverallRisk { get; init; }

    /// <summary>
    /// List of potential issues or risks
    /// </summary>
    public required IReadOnlyList<RiskItem> Risks { get; init; }

    /// <summary>
    /// Estimated complexity of applying the changes
    /// </summary>
    public required ComplexityEstimate Complexity { get; init; }

    /// <summary>
    /// Recommendations for safe application
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; }
}

/// <summary>
/// Risk levels for changes
/// </summary>
public enum ChangeRiskLevel
{
    /// <summary>
    /// Low risk - simple text changes
    /// </summary>
    Low,

    /// <summary>
    /// Medium risk - structural changes
    /// </summary>
    Medium,

    /// <summary>
    /// High risk - complex refactoring
    /// </summary>
    High,

    /// <summary>
    /// Critical risk - could break functionality
    /// </summary>
    Critical
}

/// <summary>
/// A specific risk item
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
    public required IReadOnlyList<string> AffectedFiles { get; init; }

    /// <summary>
    /// Severity of this risk
    /// </summary>
    public required RiskSeverity Severity { get; init; }

    /// <summary>
    /// How to mitigate this risk
    /// </summary>
    public string? Mitigation { get; init; }
}

/// <summary>
/// Types of risks
/// </summary>
public enum RiskType
{
    /// <summary>
    /// Could break compilation
    /// </summary>
    Compilation,

    /// <summary>
    /// Could change runtime behavior
    /// </summary>
    Runtime,

    /// <summary>
    /// Could affect performance
    /// </summary>
    Performance,

    /// <summary>
    /// Could break dependencies
    /// </summary>
    Dependencies,

    /// <summary>
    /// Could cause data loss
    /// </summary>
    DataLoss,

    /// <summary>
    /// Could affect security
    /// </summary>
    Security,

    /// <summary>
    /// Could affect API compatibility
    /// </summary>
    ApiCompatibility,

    /// <summary>
    /// Could cause file access issues
    /// </summary>
    FileAccessIssue,

    /// <summary>
    /// Other type of risk
    /// </summary>
    Other
}

/// <summary>
/// Risk severity levels
/// </summary>
public enum RiskSeverity
{
    /// <summary>
    /// Minor issue
    /// </summary>
    Low,

    /// <summary>
    /// Moderate concern
    /// </summary>
    Medium,

    /// <summary>
    /// Significant concern
    /// </summary>
    High,

    /// <summary>
    /// Critical issue
    /// </summary>
    Critical
}

/// <summary>
/// Estimate of complexity
/// </summary>
public class ComplexityEstimate
{
    /// <summary>
    /// Overall complexity score (0-100)
    /// </summary>
    public required int Score { get; init; }

    /// <summary>
    /// Description of complexity level
    /// </summary>
    public required string Level { get; init; }

    /// <summary>
    /// Factors contributing to complexity
    /// </summary>
    public required IReadOnlyList<string> Factors { get; init; }

    /// <summary>
    /// Estimated time to apply changes
    /// </summary>
    public required TimeSpan EstimatedTime { get; init; }
}

/// <summary>
/// Progress information for bulk edit operations
/// </summary>
public class BulkEditProgress
{
    /// <summary>
    /// Current progress percentage (0-100)
    /// </summary>
    public required int Percentage { get; init; }

    /// <summary>
    /// Number of files processed so far
    /// </summary>
    public required int FilesProcessed { get; init; }

    /// <summary>
    /// Total number of files to process
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Current file being processed
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// Current operation phase
    /// </summary>
    public required string Phase { get; init; }

    /// <summary>
    /// Whether the operation is complete
    /// </summary>
    public required bool IsComplete { get; init; }

    /// <summary>
    /// Any errors encountered so far
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Additional status information
    /// </summary>
    public string? Status { get; init; }
}
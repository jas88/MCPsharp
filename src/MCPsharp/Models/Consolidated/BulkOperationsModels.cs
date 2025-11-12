using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Models.Consolidated;

/// <summary>
/// Bulk operation types
/// </summary>
public enum BulkOperationType
{
    Replace,
    Conditional,
    Refactor,
    MultiEdit,
    FileOperation
}

/// <summary>
/// Bulk operation status
/// </summary>
public enum BulkOperationStatus
{
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    NotFound
}

/// <summary>
/// Bulk management actions
/// </summary>
public enum BulkManagementAction
{
    List,
    Cancel,
    Pause,
    Resume,
    Cleanup,
    Status
}

/// <summary>
/// Bulk operation request options
/// </summary>
public class BulkOperationOptions
{
    /// <summary>
    /// Create backup before operation
    /// </summary>
    public bool CreateBackup { get; set; } = false;

    /// <summary>
    /// Maximum parallelism
    /// </summary>
    public int MaxParallelism { get; set; } = 10;

    /// <summary>
    /// Dry run - don't actually make changes
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Fail fast on first error
    /// </summary>
    public bool FailFast { get; set; } = true;

    /// <summary>
    /// Maximum file age to process
    /// </summary>
    public TimeSpan? MaxFileAge { get; set; }

    /// <summary>
    /// Include hidden files
    /// </summary>
    public bool IncludeHidden { get; set; } = false;

    /// <summary>
    /// Custom filters to apply
    /// </summary>
    public List<Filter> Filters { get; set; } = new();

    /// <summary>
    /// Create backup with this ID (for internal use)
    /// </summary>
    internal BulkOperationOptions WithDryRun(bool dryRun)
    {
        return new BulkOperationOptions
        {
            CreateBackup = CreateBackup,
            MaxParallelism = MaxParallelism,
            DryRun = dryRun,
            FailFast = FailFast,
            MaxFileAge = MaxFileAge,
            IncludeHidden = IncludeHidden,
            Filters = Filters
        };
    }
}

/// <summary>
/// Edit condition for conditional edits
/// </summary>
public class EditCondition
{
    public required string Type { get; init; }
    public required string Value { get; init; }
    public string? Operator { get; init; }
}

/// <summary>
/// Bulk operation request
/// </summary>
public class BulkOperationRequest
{
    public required BulkOperationType OperationType { get; set; }
    public List<string>? Files { get; set; }
    public BulkOperationOptions? Options { get; set; }
    public string? OperationId { get; set; }
    public string? RequestId { get; set; }

    // Replace operation properties
    public string? Pattern { get; set; }
    public string? Replacement { get; set; }

    // Conditional operation properties
    public List<EditCondition>? Conditions { get; set; }
    public List<TextEdit>? Edits { get; set; }

    // Refactor operation properties
    public string? RefactorType { get; set; }
    public string? TargetPattern { get; set; }
    public string? ReplacementPattern { get; set; }

    // Multi-edit operation properties
    public List<FileOperationDefinition>? Operations { get; set; }

    // Legacy compatibility
    public string? Operation { get; set; } // Alias for OperationType
}

/// <summary>
/// Bulk operation result
/// </summary>
public class BulkOperationResult
{
    public required string FilePath { get; init; }
    public required bool Success { get; init; }
    public int? Changes { get; init; }
    public long? BytesProcessed { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Bulk operation summary
/// </summary>
public class BulkOperationSummary
{
    public required int TotalFiles { get; init; }
    public required int SuccessfulFiles { get; init; }
    public required int FailedFiles { get; init; }
    public required int TotalChanges { get; init; }
    public required long TotalBytesProcessed { get; init; }
    public required TimeSpan ExecutionTime { get; init; }
}

/// <summary>
/// Bulk operation response
/// </summary>
public class BulkOperationResponse
{
    public required string OperationId { get; set; }
    public required BulkOperationType OperationType { get; set; }
    public List<BulkOperationResult>? Results { get; set; }
    public BulkOperationSummary? Summary { get; set; }
    public TimeSpan ExecutionTime { get; set; }
    public bool Success { get; set; }
    public bool RolledBack { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Error message (alias for Error property)
    /// </summary>
    public string? ErrorMessage => Error;
}

/// <summary>
/// Bulk preview request
/// </summary>
public class BulkPreviewRequest
{
    public required BulkOperationType OperationType { get; set; }
    public List<string>? Files { get; set; }
    public BulkOperationOptions? Options { get; set; }
    public string? RequestId { get; set; }

    // For replace operations
    public string? Pattern { get; set; }
    public string? Replacement { get; set; }

    // For conditional operations
    public List<EditCondition>? Conditions { get; set; }
    public List<TextEdit>? Edits { get; set; }

    // For refactor operations
    public string? RefactorType { get; set; }
    public string? TargetPattern { get; set; }
    public string? ReplacementPattern { get; set; }

    // For multi-edit operations
    public List<FileOperationDefinition>? Operations { get; set; }

    // Legacy compatibility
    public string? Operation { get; set; } // Alias for OperationType
    public int? MaxPreviewFiles { get; set; }
}

/// <summary>
/// Bulk impact analysis
/// </summary>
public class BulkImpactAnalysis
{
    public required int AffectedFiles { get; init; }
    public required int TotalChanges { get; init; }
    public required int FilesWithErrors { get; init; }
    public required int HighRiskFiles { get; init; }
    public required double EstimatedComplexity { get; init; }
    public List<string> AffectedDirectories { get; init; } = new();
    public List<string> AffectedNamespaces { get; init; } = new();
}

/// <summary>
/// Bulk risk assessment
/// </summary>
public class BulkRiskAssessment
{
    public required RiskLevel RiskLevel { get; init; }
    public required List<string> Warnings { get; init; }
    public required List<string> Recommendations { get; init; }
    public double ConfidenceScore { get; init; }
}

/// <summary>
/// Bulk preview response
/// </summary>
public class BulkPreviewResponse
{
    public required BulkOperationType OperationType { get; init; }
    public List<BulkOperationResult>? PreviewResults { get; set; }
    public BulkImpactAnalysis? ImpactAnalysis { get; set; }
    public BulkRiskAssessment? RiskAssessment { get; set; }
    public TimeSpan? EstimatedTime { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; init; } = string.Empty;
}

/// <summary>
/// Bulk progress information
/// </summary>
public class BulkProgressInfo
{
    public required int CurrentStep { get; init; }
    public required int TotalSteps { get; init; }
    public required double PercentComplete { get; init; }
    public required string CurrentFile { get; init; }
    public required TimeSpan ElapsedTime { get; init; }
    public required TimeSpan? EstimatedTimeRemaining { get; init; }
    public required string Message { get; init; }
    public double? FilesPerSecond { get; init; }
    public long? BytesPerSecond { get; init; }
}

/// <summary>
/// Bulk progress response
/// </summary>
public class BulkProgressResponse
{
    public required string OperationId { get; init; }
    public BulkOperationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
    public BulkProgressInfo? Progress { get; set; }
    public List<string>? RecentLogs { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; init; } = string.Empty;
}

/// <summary>
/// Bulk management request
/// </summary>
public class BulkManagementRequest
{
    public required BulkManagementAction Action { get; set; }
    public string? Target { get; set; }
    public string? OperationId { get; set; } // Alias for Target for compatibility
    public TimeSpan? MaxAge { get; set; }
    public string? RequestId { get; set; }

    // Legacy compatibility - allow string to Action conversion
    public string? ActionString { get; set; } // For string enum conversion
}

/// <summary>
/// Bulk operation info
/// </summary>
public class BulkOperationInfo
{
    public required string Id { get; init; }
    public required BulkOperationType Type { get; init; }
    public required BulkOperationStatus Status { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public bool HasBackup { get; init; }
    public TimeSpan? RunningTime { get; init; }
}

/// <summary>
/// Bulk cancel result
/// </summary>
public class BulkCancelResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public DateTime? CancelledAt { get; init; }
    public int? CompletedFiles { get; init; }
}

/// <summary>
/// Bulk pause result
/// </summary>
public class BulkPauseResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public DateTime? PausedAt { get; init; }
    public int? CompletedFiles { get; init; }
}

/// <summary>
/// Bulk resume result
/// </summary>
public class BulkResumeResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public DateTime? ResumedAt { get; init; }
    public int? CompletedFiles { get; init; }
}

/// <summary>
/// Bulk cleanup result
/// </summary>
public class BulkCleanupResult
{
    public required bool Success { get; init; }
    public required int CleanedOperations { get; init; }
    public required string Message { get; init; }
    public long FreedSpaceBytes { get; init; }
    public List<string> CleanedOperationIds { get; init; } = new();
}

/// <summary>
/// Bulk operation status result
/// </summary>
public class BulkOperationStatusResult
{
    public required bool Found { get; init; }
    public BulkOperationStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? EstimatedCompletion { get; init; }
    public TimeSpan? RunningTime { get; init; }
}

/// <summary>
/// Bulk management response
/// </summary>
public class BulkManagementResponse
{
    public required BulkManagementAction Action { get; set; }
    public string? Target { get; set; }
    public List<BulkOperationInfo>? ActiveOperations { get; set; }
    public BulkCancelResult? CancelResult { get; set; }
    public BulkPauseResult? PauseResult { get; set; }
    public BulkResumeResult? ResumeResult { get; set; }
    public BulkCleanupResult? CleanupResult { get; set; }
    public BulkOperationStatusResult? StatusResult { get; set; }
    public ResponseMetadata Metadata { get; set; } = new();
    public string? Error { get; set; }
    public string RequestId { get; set; } = string.Empty;

    /// <summary>
    /// Whether the management operation was successful
    /// </summary>
    public bool Success => string.IsNullOrEmpty(Error);

    /// <summary>
    /// Error message (alias for Error property)
    /// </summary>
    public string? ErrorMessage => Error;
}

/// <summary>
/// Progress info for tracking
/// </summary>
public class ProgressInfo
{
    public required string Title { get; init; }
    public required int TotalSteps { get; init; }
    public int CurrentStep { get; init; }
    public string? CurrentItem { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Progress result for completed tracking
/// </summary>
public class ProgressResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public required Dictionary<string, object> Metrics { get; init; }
    public List<string>? Warnings { get; init; }
    public List<string>? Errors { get; init; }
}

/// <summary>
/// Progress tracking interface
/// </summary>
public interface IProgressTracker
{
    Task<string> StartTrackingAsync(string operationId, ProgressInfo info, CancellationToken ct);
    Task UpdateProgressAsync(string progressId, ProgressInfo info, CancellationToken ct);
    Task<ProgressResult?> CompleteTrackingAsync(string progressId, ProgressResult result, CancellationToken ct);
    Task<ProgressInfo?> GetProgressAsync(string progressId, CancellationToken ct);
    Task<List<ProgressInfo>> GetAllProgressAsync(CancellationToken ct);
    Task CancelTrackingAsync(string progressId, CancellationToken ct);
}

/// <summary>
/// Temporary file manager interface
/// </summary>
public interface ITempFileManager
{
    Task<string> CreateTempFileAsync(string? extension = null, CancellationToken ct = default);
    Task<string> CreateTempDirectoryAsync(CancellationToken ct = default);
    Task CleanupTempFilesAsync(TimeSpan olderThan, CancellationToken ct = default);
    Task<long> GetTempSpaceUsageAsync(CancellationToken ct = default);
}
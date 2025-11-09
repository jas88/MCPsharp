using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Service for performing bulk edit operations across multiple files
/// </summary>
public interface IBulkEditService
{
    /// <summary>
    /// Perform bulk replace operation using regex patterns
    /// </summary>
    /// <param name="files">Files to process (glob patterns or absolute paths)</param>
    /// <param name="regexPattern">Regex pattern to search for</param>
    /// <param name="replacement">Replacement text or pattern</param>
    /// <param name="options">Operation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the bulk replace operation</returns>
    Task<BulkEditResult> BulkReplaceAsync(
        IReadOnlyList<string> files,
        string regexPattern,
        string replacement,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform conditional edits based on file content conditions
    /// </summary>
    /// <param name="files">Files to process (glob patterns or absolute paths)</param>
    /// <param name="condition">Condition to check for each file</param>
    /// <param name="edits">Edits to apply when condition is met</param>
    /// <param name="options">Operation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the conditional edit operation</returns>
    Task<BulkEditResult> ConditionalEditAsync(
        IReadOnlyList<string> files,
        BulkEditCondition condition,
        IReadOnlyList<TextEdit> edits,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform batch refactoring using pattern-based transformations
    /// </summary>
    /// <param name="files">Files to process (glob patterns or absolute paths)</param>
    /// <param name="refactorPattern">Refactoring pattern to apply</param>
    /// <param name="options">Operation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the batch refactor operation</returns>
    Task<BulkEditResult> BatchRefactorAsync(
        IReadOnlyList<string> files,
        BulkRefactorPattern refactorPattern,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform coordinated multi-file edits
    /// </summary>
    /// <param name="editOperations">Multi-file edit operations to perform</param>
    /// <param name="options">Operation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the multi-file edit operation</returns>
    Task<BulkEditResult> MultiFileEditAsync(
        IReadOnlyList<MultiFileEditOperation> editOperations,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview bulk changes without applying them
    /// </summary>
    /// <param name="request">Bulk edit request to preview</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Preview of what changes would be made</returns>
    Task<PreviewResult> PreviewBulkChangesAsync(
        BulkEditRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback a previous bulk edit operation
    /// </summary>
    /// <param name="rollbackId">ID of the rollback session</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the rollback operation</returns>
    Task<BulkEditResult> RollbackBulkEditAsync(
        string rollbackId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate a bulk edit request before execution
    /// </summary>
    /// <param name="request">Bulk edit request to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with any issues found</returns>
    Task<ValidationResult> ValidateBulkEditAsync(
        BulkEditRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available rollback sessions
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available rollback sessions</returns>
    Task<IReadOnlyList<RollbackInfo>> GetAvailableRollbacksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up expired rollback sessions
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of sessions cleaned up</returns>
    Task<int> CleanupExpiredRollbacksAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimate the impact of a bulk edit operation
    /// </summary>
    /// <param name="request">Bulk edit request to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Impact estimation for the operation</returns>
    Task<ImpactEstimate> EstimateImpactAsync(
        BulkEditRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file statistics for planning bulk operations
    /// </summary>
    /// <param name="files">Files to analyze (glob patterns or absolute paths)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics about the files</returns>
    Task<FileStatistics> GetFileStatisticsAsync(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Statistics about files for bulk operation planning
/// </summary>
public class FileStatistics
{
    /// <summary>
    /// Total files found matching the criteria
    /// </summary>
    public required int TotalFiles { get; init; }

    /// <summary>
    /// Total size of all files in bytes
    /// </summary>
    public required long TotalSize { get; init; }

    /// <summary>
    /// Average file size in bytes
    /// </summary>
    public required double AverageFileSize { get; init; }

    /// <summary>
    /// Largest file size in bytes
    /// </summary>
    public required long LargestFileSize { get; init; }

    /// <summary>
    /// Smallest file size in bytes
    /// </summary>
    public required long SmallestFileSize { get; init; }

    /// <summary>
    /// Total lines across all files
    /// </summary>
    public required int TotalLines { get; init; }

    /// <summary>
    /// File types and their counts
    /// </summary>
    public required IReadOnlyDictionary<string, int> FileTypes { get; init; }

    /// <summary>
    /// Files that would be too large to process
    /// </summary>
    public required IReadOnlyList<string> OversizedFiles { get; init; }

    /// <summary>
    /// Files that cannot be accessed
    /// </summary>
    public required IReadOnlyList<string> InaccessibleFiles { get; init; }

    /// <summary>
    /// Estimated processing time
    /// </summary>
    public required TimeSpan EstimatedProcessingTime { get; init; }

    /// <summary>
    /// Recommended batch size for processing
    /// </summary>
    public required int RecommendedBatchSize { get; init; }
}
using System;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Interface for tracking progress of streaming operations
/// </summary>
public interface IProgressTracker
{
    /// <summary>
    /// Create a new progress tracker for an operation
    /// </summary>
    Task<string> CreateProgressAsync(string operationId, string name, long totalBytes);

    /// <summary>
    /// Update progress for an operation
    /// </summary>
    Task UpdateProgressAsync(string operationId, long bytesProcessed, long chunksProcessed = 0, long linesProcessed = 0, long itemsProcessed = 0);

    /// <summary>
    /// Get current progress for an operation
    /// </summary>
    Task<StreamProgress?> GetProgressAsync(string operationId);

    /// <summary>
    /// Set the current phase of processing
    /// </summary>
    Task SetPhaseAsync(string operationId, string phase);

    /// <summary>
    /// Add custom metadata to progress
    /// </summary>
    Task AddMetadataAsync(string operationId, string key, object value);

    /// <summary>
    /// Complete progress tracking for an operation
    /// </summary>
    Task CompleteProgressAsync(string operationId);

    /// <summary>
    /// Remove progress tracking for an operation
    /// </summary>
    Task RemoveProgressAsync(string operationId);

    /// <summary>
    /// Get all active progress trackers
    /// </summary>
    Task<List<StreamProgress>> GetActiveProgressAsync();

    /// <summary>
    /// Clean up old progress trackers
    /// </summary>
    Task CleanupAsync(TimeSpan olderThan);

    /// <summary>
    /// Report progress for file processing operations
    /// </summary>
    void ReportProgress(FileProcessingProgress progress);
}
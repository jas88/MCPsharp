using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Interface for streaming file processing operations
/// </summary>
public interface IStreamingFileProcessor
{
    /// <summary>
    /// Process a single file using streaming operations
    /// </summary>
    Task<StreamResult> ProcessFileAsync(StreamProcessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process multiple files in bulk
    /// </summary>
    Task<List<StreamResult>> BulkTransformAsync(BulkTransformRequest request, IProgress<StreamProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get progress of a streaming operation
    /// </summary>
    Task<StreamProgress?> GetProgressAsync(string operationId);

    /// <summary>
    /// Cancel a streaming operation
    /// </summary>
    Task<bool> CancelOperationAsync(string operationId);

    /// <summary>
    /// Resume a previously cancelled or failed operation from checkpoint
    /// </summary>
    Task<StreamResult?> ResumeOperationAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a checkpoint for the current operation
    /// </summary>
    Task<CheckpointData?> CreateCheckpointAsync(string operationId);

    /// <summary>
    /// List all active and recent operations
    /// </summary>
    Task<List<StreamOperation>> ListOperationsAsync(int maxCount = 50);

    /// <summary>
    /// Clean up temporary files and completed operations
    /// </summary>
    Task CleanupAsync(TimeSpan? olderThan = null);

    /// <summary>
    /// Get available stream processors
    /// </summary>
    Task<List<ChunkProcessor>> GetAvailableProcessorsAsync();

    /// <summary>
    /// Validate a stream process request
    /// </summary>
    Task<(bool IsValid, List<string> Errors)> ValidateRequestAsync(StreamProcessRequest request);

    /// <summary>
    /// Estimate processing time for a file
    /// </summary>
    Task<TimeSpan> EstimateProcessingTimeAsync(StreamProcessRequest request);
}
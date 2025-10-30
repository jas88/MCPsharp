using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Interface for managing long-running streaming operations with progress tracking
/// </summary>
public interface IStreamOperationManager
{
    /// <summary>
    /// Create a new streaming operation
    /// </summary>
    /// <param name="name">Human-readable name for the operation</param>
    /// <param name="request">Stream processing request</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Created operation with unique ID</returns>
    Task<StreamOperation> CreateOperationAsync(string name, StreamProcessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Start executing a streaming operation
    /// </summary>
    /// <param name="operationId">ID of the operation to start</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Task that completes when operation finishes</returns>
    Task<StreamResult> StartOperationAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current progress of an operation
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <returns>Current progress or null if operation not found</returns>
    Task<StreamProgress?> GetProgressAsync(string operationId);

    /// <summary>
    /// Get detailed information about an operation
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <returns>Operation details or null if not found</returns>
    Task<StreamOperation?> GetOperationAsync(string operationId);

    /// <summary>
    /// List all active and recent streaming operations
    /// </summary>
    /// <param name="maxCount">Maximum number of operations to return</param>
    /// <param name="includeCompleted">Whether to include completed operations</param>
    /// <returns>List of operations sorted by creation time (newest first)</returns>
    Task<List<StreamOperation>> ListOperationsAsync(int maxCount = 50, bool includeCompleted = true);

    /// <summary>
    /// Cancel a running operation
    /// </summary>
    /// <param name="operationId">ID of the operation to cancel</param>
    /// <returns>True if operation was cancelled, false if not found or already completed</returns>
    Task<bool> CancelOperationAsync(string operationId);

    /// <summary>
    /// Pause a running operation
    /// </summary>
    /// <param name="operationId">ID of the operation to pause</param>
    /// <returns>True if operation was paused, false if not found or not running</returns>
    Task<bool> PauseOperationAsync(string operationId);

    /// <summary>
    /// Resume a paused operation
    /// </summary>
    /// <param name="operationId">ID of the operation to resume</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>True if operation was resumed, false if not found or not paused</returns>
    Task<bool> ResumeOperationAsync(string operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a checkpoint for the current operation state
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <returns>Checkpoint data or null if operation not found</returns>
    Task<CheckpointData?> CreateCheckpointAsync(string operationId);

    /// <summary>
    /// Resume an operation from a checkpoint
    /// </summary>
    /// <param name="operationId">ID of the operation to resume</param>
    /// <param name="checkpointId">ID of the checkpoint to resume from</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>Stream result or null if operation not found</returns>
    Task<StreamResult?> ResumeFromCheckpointAsync(string operationId, string checkpointId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get statistics about all operations
    /// </summary>
    /// <returns>Processing statistics</returns>
    Task<ProcessingStatistics> GetStatisticsAsync();

    /// <summary>
    /// Clean up completed operations and temporary files
    /// </summary>
    /// <param name="olderThan">Only clean up operations older than this duration</param>
    /// <param name="includeFailed">Whether to also clean up failed operations</param>
    /// <returns>Number of operations cleaned up</returns>
    Task<int> CleanupAsync(TimeSpan? olderThan = null, bool includeFailed = false);

    /// <summary>
    /// Get all available checkpoints for an operation
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <returns>List of checkpoints</returns>
    Task<List<CheckpointData>> GetCheckpointsAsync(string operationId);

    /// <summary>
    /// Delete a checkpoint
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <param name="checkpointId">ID of the checkpoint to delete</param>
    /// <returns>True if checkpoint was deleted</returns>
    Task<bool> DeleteCheckpointAsync(string operationId, string checkpointId);

    /// <summary>
    /// Update operation status
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <param name="status">New status</param>
    /// <param name="errorMessage">Optional error message</param>
    /// <returns>True if status was updated</returns>
    Task<bool> UpdateOperationStatusAsync(string operationId, StreamOperationStatus status, string? errorMessage = null);

    /// <summary>
    /// Add temporary file to an operation for cleanup
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <param name="filePath">Path to temporary file</param>
    /// <returns>True if file was added</returns>
    Task<bool> AddTemporaryFileAsync(string operationId, string filePath);

    /// <summary>
    /// Remove temporary file from operation tracking
    /// </summary>
    /// <param name="operationId">ID of the operation</param>
    /// <param name="filePath">Path to temporary file</param>
    /// <returns>True if file was removed</returns>
    Task<bool> RemoveTemporaryFileAsync(string operationId, string filePath);
}
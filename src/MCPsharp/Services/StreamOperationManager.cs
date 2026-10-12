using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Service for managing long-running streaming operations with progress tracking,
/// checkpoint support, and concurrent operation handling.
/// </summary>
public partial class StreamOperationManager : IStreamOperationManager, IDisposable
{
    private readonly ILogger<StreamOperationManager> _logger;
    private readonly IStreamingFileProcessor _streamingProcessor;
    private readonly IProgressTracker _progressTracker;

    // Thread-safe collections for operation management
    private readonly ConcurrentDictionary<string, StreamOperation> _operations;
    private readonly ConcurrentDictionary<string, List<CheckpointData>> _checkpoints;
    private readonly SemaphoreSlim _executionSemaphore;
    private readonly Timer _cleanupTimer;

    // Configuration
    private readonly TimeSpan _defaultCleanupAge = TimeSpan.FromHours(24);
    private readonly int _maxConcurrentOperations;

    public StreamOperationManager(
        ILogger<StreamOperationManager> logger,
        IStreamingFileProcessor streamingProcessor,
        IProgressTracker progressTracker,
        ITempFileManager tempFileManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _streamingProcessor = streamingProcessor ?? throw new ArgumentNullException(nameof(streamingProcessor));
        _progressTracker = progressTracker ?? throw new ArgumentNullException(nameof(progressTracker));

        _operations = new ConcurrentDictionary<string, StreamOperation>();
        _checkpoints = new ConcurrentDictionary<string, List<CheckpointData>>();
        _maxConcurrentOperations = Environment.ProcessorCount;
        _executionSemaphore = new SemaphoreSlim(_maxConcurrentOperations, _maxConcurrentOperations);

        // Run cleanup every 10 minutes
        _cleanupTimer = new Timer(PerformAutomaticCleanup, null, 
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        LogStreamOperationManagerInitializedWithMaxConcurrent(_logger, _maxConcurrentOperations);
    }

    public async Task<StreamOperation> CreateOperationAsync(string name, StreamProcessRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Operation name cannot be empty", nameof(name));

        ArgumentNullException.ThrowIfNull(request);

        var operation = new StreamOperation
        {
            Name = name,
            Request = request,
            Status = StreamOperationStatus.Created,
            CreatedAt = DateTime.UtcNow
        };

        _operations.TryAdd(operation.OperationId, operation);

        // Initialize progress tracking
        var fileSize = request.FilePath != null && File.Exists(request.FilePath) 
            ? new FileInfo(request.FilePath).Length 
            : 0;
        await _progressTracker.CreateProgressAsync(operation.OperationId, name, fileSize);

        LogCreatedStreamingOperationForFile(_logger, operation.OperationId, name, request.FilePath);

        return operation;
    }

    public async Task<StreamResult> StartOperationAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            throw new ArgumentException($"Operation {operationId} not found", nameof(operationId));
        }

        if (operation.Status != StreamOperationStatus.Created && 
            operation.Status != StreamOperationStatus.Resumed)
        {
            throw new InvalidOperationException($"Operation {operationId} is not in a startable state: {operation.Status}");
        }

        await _executionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            operation.Status = StreamOperationStatus.Running;
            operation.StartedAt = DateTime.UtcNow;

            // Link cancellation tokens
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, operation.CancellationTokenSource.Token);

            LogStartingStreamingOperation(_logger, operationId, operation.Name);

            // Execute the streaming operation
            var result = await _streamingProcessor.ProcessFileAsync(operation.Request, linkedCts.Token);

            // Update operation status based on result
            if (result.Success)
            {
                operation.Status = StreamOperationStatus.Completed;
                operation.CompletedAt = DateTime.UtcNow;
                await _progressTracker.CompleteProgressAsync(operationId);
            }
            else
            {
                operation.Status = StreamOperationStatus.Failed;
                operation.ErrorMessage = result.ErrorMessage;
                operation.Exception = result.Exception;
            }

            LogStreamingOperationCompletedWithStatus(_logger, operationId, operation.Status, result.ErrorMessage ?? "Success");

            return result;
        }
        catch (OperationCanceledException)
        {
            operation.Status = StreamOperationStatus.Cancelled;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = "Operation was cancelled";
            
            LogStreamingOperationWasCancelled(_logger, operationId);
            throw;
        }
        catch (Exception ex)
        {
            operation.Status = StreamOperationStatus.Failed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = ex.Message;
            operation.Exception = ex;

            LogStreamingOperationFailed(_logger, ex, operationId);
            throw;
        }
        finally
        {
            _executionSemaphore.Release();
        }
    }

    public async Task<StreamProgress?> GetProgressAsync(string operationId)
    {
        return await _progressTracker.GetProgressAsync(operationId);
    }

    public Task<StreamOperation?> GetOperationAsync(string operationId)
    {
        _operations.TryGetValue(operationId, out var operation);
        return Task.FromResult(operation);
    }

    public Task<List<StreamOperation>> ListOperationsAsync(int maxCount = 50, bool includeCompleted = true)
    {
        var operations = _operations.Values.AsEnumerable();

        if (!includeCompleted)
        {
            operations = operations.Where(op =>
                op.Status != StreamOperationStatus.Completed &&
                op.Status != StreamOperationStatus.Failed &&
                op.Status != StreamOperationStatus.Cancelled);
        }

        var result = operations
            .OrderByDescending(op => op.CreatedAt)
            .Take(maxCount)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> CancelOperationAsync(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult(false);
        }

        if (operation.Status == StreamOperationStatus.Completed ||
            operation.Status == StreamOperationStatus.Failed ||
            operation.Status == StreamOperationStatus.Cancelled)
        {
            return Task.FromResult(false);
        }

        try
        {
            operation.CancellationTokenSource.Cancel();
            operation.Status = StreamOperationStatus.Cancelled;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = "Operation was cancelled by user request";

            LogCancelledStreamingOperation(_logger, operationId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogErrorCancellingOperation(_logger, ex, operationId);
            return Task.FromResult(false);
        }
    }

    public Task<bool> PauseOperationAsync(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult(false);
        }

        if (operation.Status != StreamOperationStatus.Running)
        {
            return Task.FromResult(false);
        }

        operation.Status = StreamOperationStatus.Paused;
        LogPausedStreamingOperation(_logger, operationId);
        return Task.FromResult(true);
    }

    public Task<bool> ResumeOperationAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult(false);
        }

        if (operation.Status != StreamOperationStatus.Paused)
        {
            return Task.FromResult(false);
        }

        operation.Status = StreamOperationStatus.Resumed;
        LogResumedStreamingOperation(_logger, operationId);
        return Task.FromResult(true);
    }

    public async Task<CheckpointData?> CreateCheckpointAsync(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return null;
        }

        try
        {
            var checkpoint = await _streamingProcessor.CreateCheckpointAsync(operationId);
            if (checkpoint != null)
            {
                // Add to operation's checkpoint list
                var checkpoints = _checkpoints.GetOrAdd(operationId, _ => new List<CheckpointData>());
                lock (checkpoints)
                {
                    checkpoints.Add(checkpoint);
                    // Keep only last 10 checkpoints per operation
                    if (checkpoints.Count > 10)
                    {
                        checkpoints.RemoveAt(0);
                    }
                }

                operation.LastCheckpoint = checkpoint;
                operation.LastCheckpointAt = DateTime.UtcNow;

                LogCreatedCheckpointForOperation(_logger, checkpoint.CheckpointId, operationId);
            }

            return checkpoint;
        }
        catch (Exception ex)
        {
            LogErrorCreatingCheckpointForOperation(_logger, ex, operationId);
            return null;
        }
    }

    public async Task<StreamResult?> ResumeFromCheckpointAsync(string operationId, string checkpointId, 
        CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return null;
        }

        if (!_checkpoints.TryGetValue(operationId, out var checkpoints))
        {
            return null;
        }

        CheckpointData? checkpoint = null;
        lock (checkpoints)
        {
            checkpoint = checkpoints.FirstOrDefault(c => c.CheckpointId == checkpointId);
        }

        if (checkpoint == null)
        {
            return null;
        }

        try
        {
            operation.Status = StreamOperationStatus.Resumed;
            
            LogResumingOperationFromCheckpoint(_logger, operationId, checkpointId);

            var result = await _streamingProcessor.ResumeOperationAsync(operationId, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            LogErrorResumingOperationFromCheckpoint(_logger, ex, operationId, checkpointId);
            operation.Status = StreamOperationStatus.Failed;
            operation.ErrorMessage = ex.Message;
            operation.Exception = ex;
            return null;
        }
    }

    public Task<ProcessingStatistics> GetStatisticsAsync()
    {
        var operations = _operations.Values.ToList();
        var now = DateTime.UtcNow;

        var stats = new ProcessingStatistics
        {
            TotalFilesProcessed = operations.Count(op => op.Status == StreamOperationStatus.Completed),
            TotalBytesProcessed = operations
                .Where(op => op.Status == StreamOperationStatus.Completed)
                .Sum(op => op.Progress.BytesProcessed),
            ActiveProcessingTasks = operations.Count(op => op.Status == StreamOperationStatus.Running),
            LastUpdated = now
        };

        // Calculate average processing time
        var completedOperations = operations
            .Where(op => op.Status == StreamOperationStatus.Completed &&
                        op.StartedAt.HasValue &&
                        op.CompletedAt.HasValue)
            .ToList();

        if (completedOperations.Count > 0)
        {
            stats.AverageProcessingTime = TimeSpan.FromTicks(
                (long)completedOperations.Average(op => (op.CompletedAt!.Value - op.StartedAt!.Value).Ticks));
        }

        return Task.FromResult(stats);
    }

    public async Task<int> CleanupAsync(TimeSpan? olderThan = null, bool includeFailed = false)
    {
        var cutoffTime = DateTime.UtcNow - (olderThan ?? _defaultCleanupAge);
        var operationsToCleanup = _operations
            .Where(kvp => ShouldCleanupOperation(kvp.Value, cutoffTime, includeFailed))
            .ToList();

        var cleanedCount = 0;

        foreach (var kvp in operationsToCleanup)
        {
            var operationId = kvp.Key;
            var operation = kvp.Value;

            try
            {
                // Clean up temporary files
                foreach (var tempFile in operation.TemporaryFiles.Where(File.Exists))
                {
                    File.Delete(tempFile);
                }

                // Clean up checkpoints
                if (_checkpoints.TryRemove(operationId, out var checkpoints))
                {
                    foreach (var checkpointFile in checkpoints.Select(c => c.CheckpointFilePath).Where(File.Exists))
                    {
                        File.Delete(checkpointFile);
                    }
                }

                // Cancel if still running
                if (operation.Status == StreamOperationStatus.Running)
                {
                    await operation.CancellationTokenSource.CancelAsync();
                }

                // Dispose cancellation token source
                operation.CancellationTokenSource.Dispose();

                // Remove from operations
                _operations.TryRemove(operationId, out _);

                // Remove from progress tracker
                await _progressTracker.RemoveProgressAsync(operationId);

                cleanedCount++;
            }
            catch (Exception ex)
            {
                LogErrorCleaningUpOperation(_logger, ex, operationId);
            }
        }

        if (cleanedCount > 0)
        {
            LogCleanedUpStreamingOperations(_logger, cleanedCount);
        }

        return cleanedCount;
    }

    public Task<List<CheckpointData>> GetCheckpointsAsync(string operationId)
    {
        if (!_checkpoints.TryGetValue(operationId, out var checkpoints))
        {
            return Task.FromResult(new List<CheckpointData>());
        }

        lock (checkpoints)
        {
            return Task.FromResult(checkpoints.OrderByDescending(c => c.CreatedAt).ToList());
        }
    }

    public Task<bool> DeleteCheckpointAsync(string operationId, string checkpointId)
    {
        if (!_checkpoints.TryGetValue(operationId, out var checkpoints))
        {
            return Task.FromResult(false);
        }

        lock (checkpoints)
        {
            var checkpoint = checkpoints.FirstOrDefault(c => c.CheckpointId == checkpointId);
            if (checkpoint == null)
            {
                return Task.FromResult(false);
            }

            // Delete checkpoint file
            try
            {
                if (File.Exists(checkpoint.CheckpointFilePath))
                {
                    File.Delete(checkpoint.CheckpointFilePath);
                }
            }
            catch (Exception ex)
            {
                LogErrorDeletingCheckpointFile(_logger, ex, checkpoint.CheckpointFilePath);
            }

            // Remove from list
            checkpoints.Remove(checkpoint);
            return Task.FromResult(true);
        }
    }

    public Task<bool> UpdateOperationStatusAsync(string operationId, StreamOperationStatus status,
        string? errorMessage = null)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult(false);
        }

        operation.Status = status;
        if (!string.IsNullOrEmpty(errorMessage))
        {
            operation.ErrorMessage = errorMessage;
        }

        if (status == StreamOperationStatus.Completed ||
            status == StreamOperationStatus.Failed ||
            status == StreamOperationStatus.Cancelled)
        {
            operation.CompletedAt = DateTime.UtcNow;
        }

        return Task.FromResult(true);
    }

    public Task<bool> AddTemporaryFileAsync(string operationId, string filePath)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult(false);
        }

        lock (operation.TemporaryFiles)
        {
            if (!operation.TemporaryFiles.Contains(filePath))
            {
                operation.TemporaryFiles.Add(filePath);
            }
        }

        return Task.FromResult(true);
    }

    public Task<bool> RemoveTemporaryFileAsync(string operationId, string filePath)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return Task.FromResult(false);
        }

        lock (operation.TemporaryFiles)
        {
            return Task.FromResult(operation.TemporaryFiles.Remove(filePath));
        }
    }

    private static bool ShouldCleanupOperation(StreamOperation operation, DateTime cutoffTime, bool includeFailed)
    {
        // Don't clean up running operations
        if (operation.Status == StreamOperationStatus.Running || 
            operation.Status == StreamOperationStatus.Paused)
        {
            return false;
        }

        // Always clean up very old operations
        if (operation.CreatedAt < cutoffTime)
        {
            return true;
        }

        // Clean up completed operations older than 1 hour
        if (operation.Status == StreamOperationStatus.Completed && 
            operation.CompletedAt.HasValue && 
            operation.CompletedAt.Value < DateTime.UtcNow - TimeSpan.FromHours(1))
        {
            return true;
        }

        // Clean up failed operations if requested
        if (includeFailed && 
            (operation.Status == StreamOperationStatus.Failed || operation.Status == StreamOperationStatus.Cancelled) &&
            operation.CompletedAt.HasValue && 
            operation.CompletedAt.Value < DateTime.UtcNow - TimeSpan.FromMinutes(30))
        {
            return true;
        }

        return false;
    }

    private void PerformAutomaticCleanup(object? state)
    {
        try
        {
            CleanupAsync(TimeSpan.FromHours(2), includeFailed: true).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogErrorDuringAutomaticCleanupOf(_logger, ex);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose managed resources
            _cleanupTimer?.Dispose();
            _executionSemaphore?.Dispose();

            // Cancel all running operations
            foreach (var operation in _operations.Values)
            {
                try
                {
                    if (operation.Status == StreamOperationStatus.Running)
                    {
                        operation.CancellationTokenSource.Cancel();
                    }
                    operation.CancellationTokenSource.Dispose();
                }
                catch (Exception ex)
                {
                    LogErrorDisposingOperation(_logger, ex, operation.OperationId);
                }
            }

            _operations.Clear();
            _checkpoints.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "StreamOperationManager initialized with max {MaxConcurrent} concurrent operations")]
    private static partial void LogStreamOperationManagerInitializedWithMaxConcurrent(ILogger logger, int maxConcurrent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created streaming operation {OperationId} ({Name}) for file {FilePath}")]
    private static partial void LogCreatedStreamingOperationForFile(ILogger logger, string operationId, string name, string? filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting streaming operation {OperationId} ({Name})")]
    private static partial void LogStartingStreamingOperation(ILogger logger, string operationId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Streaming operation {OperationId} completed with status {Status}: {Message}")]
    private static partial void LogStreamingOperationCompletedWithStatus(ILogger logger, string operationId, StreamOperationStatus status, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "Streaming operation {OperationId} was cancelled")]
    private static partial void LogStreamingOperationWasCancelled(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Streaming operation {OperationId} failed")]
    private static partial void LogStreamingOperationFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled streaming operation {OperationId}")]
    private static partial void LogCancelledStreamingOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error cancelling operation {OperationId}")]
    private static partial void LogErrorCancellingOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Paused streaming operation {OperationId}")]
    private static partial void LogPausedStreamingOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resumed streaming operation {OperationId}")]
    private static partial void LogResumedStreamingOperation(ILogger logger, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created checkpoint {CheckpointId} for operation {OperationId}")]
    private static partial void LogCreatedCheckpointForOperation(ILogger logger, string checkpointId, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating checkpoint for operation {OperationId}")]
    private static partial void LogErrorCreatingCheckpointForOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resuming operation {OperationId} from checkpoint {CheckpointId}")]
    private static partial void LogResumingOperationFromCheckpoint(ILogger logger, string operationId, string checkpointId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resuming operation {OperationId} from checkpoint {CheckpointId}")]
    private static partial void LogErrorResumingOperationFromCheckpoint(ILogger logger, Exception exception, string operationId, string checkpointId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error cleaning up operation {OperationId}")]
    private static partial void LogErrorCleaningUpOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} streaming operations")]
    private static partial void LogCleanedUpStreamingOperations(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error deleting checkpoint file {FilePath}")]
    private static partial void LogErrorDeletingCheckpointFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error during automatic cleanup of streaming operations")]
    private static partial void LogErrorDuringAutomaticCleanupOf(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing operation {OperationId}")]
    private static partial void LogErrorDisposingOperation(ILogger logger, Exception exception, string operationId);
}
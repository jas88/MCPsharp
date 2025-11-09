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
public class StreamOperationManager : IStreamOperationManager, IDisposable
{
    private readonly ILogger<StreamOperationManager> _logger;
    private readonly IStreamingFileProcessor _streamingProcessor;
    private readonly IProgressTracker _progressTracker;
    private readonly ITempFileManager _tempFileManager;

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
        _tempFileManager = tempFileManager ?? throw new ArgumentNullException(nameof(tempFileManager));

        _operations = new ConcurrentDictionary<string, StreamOperation>();
        _checkpoints = new ConcurrentDictionary<string, List<CheckpointData>>();
        _maxConcurrentOperations = Environment.ProcessorCount;
        _executionSemaphore = new SemaphoreSlim(_maxConcurrentOperations, _maxConcurrentOperations);

        // Run cleanup every 10 minutes
        _cleanupTimer = new Timer(PerformAutomaticCleanup, null, 
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        _logger.LogInformation("StreamOperationManager initialized with max {MaxConcurrent} concurrent operations",
            _maxConcurrentOperations);
    }

    public async Task<StreamOperation> CreateOperationAsync(string name, StreamProcessRequest request, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Operation name cannot be empty", nameof(name));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

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

        _logger.LogInformation("Created streaming operation {OperationId} ({Name}) for file {FilePath}",
            operation.OperationId, name, request.FilePath);

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

            _logger.LogInformation("Starting streaming operation {OperationId} ({Name})", 
                operationId, operation.Name);

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

            _logger.LogInformation("Streaming operation {OperationId} completed with status {Status}: {Message}",
                operationId, operation.Status, result.ErrorMessage ?? "Success");

            return result;
        }
        catch (OperationCanceledException)
        {
            operation.Status = StreamOperationStatus.Cancelled;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = "Operation was cancelled";
            
            _logger.LogInformation("Streaming operation {OperationId} was cancelled", operationId);
            throw;
        }
        catch (Exception ex)
        {
            operation.Status = StreamOperationStatus.Failed;
            operation.CompletedAt = DateTime.UtcNow;
            operation.ErrorMessage = ex.Message;
            operation.Exception = ex;

            _logger.LogError(ex, "Streaming operation {OperationId} failed", operationId);
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

            _logger.LogInformation("Cancelled streaming operation {OperationId}", operationId);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling operation {OperationId}", operationId);
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
        _logger.LogInformation("Paused streaming operation {OperationId}", operationId);
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
        _logger.LogInformation("Resumed streaming operation {OperationId}", operationId);
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

                _logger.LogInformation("Created checkpoint {CheckpointId} for operation {OperationId}",
                    checkpoint.CheckpointId, operationId);
            }

            return checkpoint;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating checkpoint for operation {OperationId}", operationId);
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
            
            _logger.LogInformation("Resuming operation {OperationId} from checkpoint {CheckpointId}",
                operationId, checkpointId);

            var result = await _streamingProcessor.ResumeOperationAsync(operationId, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming operation {OperationId} from checkpoint {CheckpointId}",
                operationId, checkpointId);
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

        if (completedOperations.Any())
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
                foreach (var tempFile in operation.TemporaryFiles)
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }

                // Clean up checkpoints
                if (_checkpoints.TryRemove(operationId, out var checkpoints))
                {
                    foreach (var checkpoint in checkpoints)
                    {
                        if (File.Exists(checkpoint.CheckpointFilePath))
                        {
                            File.Delete(checkpoint.CheckpointFilePath);
                        }
                    }
                }

                // Cancel if still running
                if (operation.Status == StreamOperationStatus.Running)
                {
                    operation.CancellationTokenSource.Cancel();
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
                _logger.LogError(ex, "Error cleaning up operation {OperationId}", operationId);
            }
        }

        if (cleanedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} streaming operations", cleanedCount);
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
                _logger.LogError(ex, "Error deleting checkpoint file {FilePath}",
                    checkpoint.CheckpointFilePath);
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

    private bool ShouldCleanupOperation(StreamOperation operation, DateTime cutoffTime, bool includeFailed)
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
            _logger.LogError(ex, "Error during automatic cleanup of streaming operations");
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
                    _logger.LogError(ex, "Error disposing operation {OperationId}", operation.OperationId);
                }
            }

            _operations.Clear();
            _checkpoints.Clear();
        }
    }
}
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
}

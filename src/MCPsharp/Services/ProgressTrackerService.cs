using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Service for tracking progress of streaming operations
/// </summary>
public class ProgressTrackerService : IProgressTracker, IDisposable
{
    private readonly ILogger<ProgressTrackerService> _logger;
    private readonly ConcurrentDictionary<string, StreamProgress> _progressTracker;
    private readonly Timer _cleanupTimer;
    private readonly object _lock = new object();

    public ProgressTrackerService(ILogger<ProgressTrackerService> logger)
    {
        _logger = logger;
        _progressTracker = new ConcurrentDictionary<string, StreamProgress>();

        // Run cleanup every 5 minutes
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public async Task<string> CreateProgressAsync(string operationId, string name, long totalBytes)
    {
        var progress = new StreamProgress
        {
            TotalBytes = totalBytes,
            CurrentPhase = "Initializing",
            LastUpdated = DateTime.UtcNow
        };

        _progressTracker.TryAdd(operationId, progress);

        _logger.LogInformation("Created progress tracker for operation {OperationId} ({Name}) with {TotalBytes} bytes",
            operationId, name, totalBytes);

        return operationId;
    }

    public async Task UpdateProgressAsync(string operationId, long bytesProcessed, long chunksProcessed = 0, long linesProcessed = 0, long itemsProcessed = 0)
    {
        if (_progressTracker.TryGetValue(operationId, out var progress))
        {
            lock (progress)
            {
                progress.BytesProcessed = bytesProcessed;
                if (chunksProcessed > 0) progress.ChunksProcessed = chunksProcessed;
                if (linesProcessed > 0) progress.LinesProcessed = linesProcessed;
                if (itemsProcessed > 0) progress.ItemsProcessed = itemsProcessed;

                progress.LastUpdated = DateTime.UtcNow;

                // Calculate processing speed
                if (progress.TotalBytes > 0)
                {
                    var elapsed = progress.LastUpdated - progress.LastUpdated.AddSeconds(-1);
                    if (elapsed.TotalSeconds > 0)
                    {
                        progress.ProcessingSpeedBytesPerSecond = (long)(bytesProcessed / elapsed.TotalSeconds);
                    }

                    // Estimate remaining time
                    var remainingBytes = progress.TotalBytes - bytesProcessed;
                    if (progress.ProcessingSpeedBytesPerSecond > 0)
                    {
                        progress.EstimatedTimeRemaining = TimeSpan.FromSeconds(
                            remainingBytes / (double)progress.ProcessingSpeedBytesPerSecond);
                    }
                }
            }
        }
    }

    public async Task<StreamProgress?> GetProgressAsync(string operationId)
    {
        _progressTracker.TryGetValue(operationId, out var progress);
        return progress;
    }

    public async Task SetPhaseAsync(string operationId, string phase)
    {
        if (_progressTracker.TryGetValue(operationId, out var progress))
        {
            lock (progress)
            {
                progress.CurrentPhase = phase;
                progress.LastUpdated = DateTime.UtcNow;
            }
        }
    }

    public async Task AddMetadataAsync(string operationId, string key, object value)
    {
        if (_progressTracker.TryGetValue(operationId, out var progress))
        {
            lock (progress)
            {
                progress.Metadata[key] = value;
                progress.LastUpdated = DateTime.UtcNow;
            }
        }
    }

    public async Task CompleteProgressAsync(string operationId)
    {
        if (_progressTracker.TryGetValue(operationId, out var progress))
        {
            lock (progress)
            {
                progress.CurrentPhase = "Completed";
                progress.LastUpdated = DateTime.UtcNow;
            }
        }
    }

    public async Task RemoveProgressAsync(string operationId)
    {
        _progressTracker.TryRemove(operationId, out _);
    }

    public async Task<List<StreamProgress>> GetActiveProgressAsync()
    {
        return _progressTracker.Values
            .Where(p => p.CurrentPhase != "Completed" && p.CurrentPhase != "Failed")
            .ToList();
    }

    public async Task CleanupAsync(TimeSpan olderThan)
    {
        var cutoffTime = DateTime.UtcNow - olderThan;
        var toRemove = _progressTracker
            .Where(kvp => kvp.Value.LastUpdated < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _progressTracker.TryRemove(key, out _);
        }

        if (toRemove.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} progress trackers older than {OlderThan}",
                toRemove.Count, olderThan);
        }
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            CleanupAsync(TimeSpan.FromHours(1)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic progress tracker cleanup");
        }
    }

    public void ReportProgress(FileProcessingProgress progress)
    {
        if (progress == null) return;

        // Update or create progress tracker with file processing progress
        var streamProgress = new StreamProgress
        {
            BytesProcessed = progress.BytesProcessed,
            TotalBytes = progress.TotalBytes,
            ChunksProcessed = progress.ChunksProcessed,
            TotalChunks = progress.TotalChunks,
            LinesProcessed = 0, // Not directly mapped from FileProcessingProgress
            ItemsProcessed = 0, // Not directly mapped from FileProcessingProgress
            CurrentPhase = progress.CurrentPhase ?? "Processing",
            LastUpdated = progress.LastUpdated,
            EstimatedTimeRemaining = progress.EstimatedTimeRemaining,
            ProcessingSpeedBytesPerSecond = progress.ProcessingSpeedBytesPerSecond
        };

        // Copy metadata
        foreach (var kvp in progress.Metadata)
        {
            streamProgress.Metadata[kvp.Key] = kvp.Value;
        }

        _progressTracker.AddOrUpdate(progress.OperationId, streamProgress, (key, existing) =>
        {
            lock (existing)
            {
                existing.BytesProcessed = streamProgress.BytesProcessed;
                existing.TotalBytes = streamProgress.TotalBytes;
                existing.ChunksProcessed = streamProgress.ChunksProcessed;
                existing.TotalChunks = streamProgress.TotalChunks;
                existing.CurrentPhase = streamProgress.CurrentPhase;
                existing.LastUpdated = streamProgress.LastUpdated;
                existing.EstimatedTimeRemaining = streamProgress.EstimatedTimeRemaining;
                existing.ProcessingSpeedBytesPerSecond = streamProgress.ProcessingSpeedBytesPerSecond;

                // Merge metadata
                foreach (var kvp in streamProgress.Metadata)
                {
                    existing.Metadata[kvp.Key] = kvp.Value;
                }
            }
            return existing;
        });

        _logger.LogDebug("Reported progress for operation {OperationId}: {ProgressPercentage:F1}%",
            progress.OperationId, progress.ProgressPercentage);
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _progressTracker.Clear();
    }
}
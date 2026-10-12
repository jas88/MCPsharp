using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Mock implementation of IProgressTracker for testing
/// </summary>
internal sealed class MockProgressTracker : IProgressTracker
{
    private readonly Dictionary<string, StreamProgress> _progress = new();

    public Task<string> CreateProgressAsync(string operationId, string name, long totalBytes)
    {
        _progress[operationId] = new StreamProgress
        {
            TotalBytes = totalBytes,
            CurrentPhase = "Processing"
        };
        return Task.FromResult(operationId);
    }

    public Task UpdateProgressAsync(string operationId, long bytesProcessed, long chunksProcessed = 0, long linesProcessed = 0, long itemsProcessed = 0)
    {
        if (_progress.TryGetValue(operationId, out var progress))
        {
            progress.BytesProcessed = bytesProcessed;
            progress.ChunksProcessed = chunksProcessed;
            progress.LinesProcessed = linesProcessed;
            progress.ItemsProcessed = itemsProcessed;
            progress.LastUpdated = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task UpdateProgressAsync(string operationId, MCPsharp.Models.Consolidated.ProgressInfo progressInfo, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<StreamProgress?> GetProgressAsync(string operationId)
    {
        _progress.TryGetValue(operationId, out var progress);
        return Task.FromResult(progress);
    }

    public Task SetPhaseAsync(string operationId, string phase)
    {
        if (_progress.TryGetValue(operationId, out var progress))
        {
            progress.CurrentPhase = phase;
            progress.LastUpdated = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task AddMetadataAsync(string operationId, string key, object value)
    {
        if (_progress.TryGetValue(operationId, out var progress))
        {
            progress.Metadata[key] = value;
        }
        return Task.CompletedTask;
    }

    public Task CompleteProgressAsync(string operationId)
    {
        if (_progress.TryGetValue(operationId, out var progress))
        {
            progress.BytesProcessed = progress.TotalBytes;
            progress.LastUpdated = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task RemoveProgressAsync(string operationId)
    {
        _progress.Remove(operationId);
        return Task.CompletedTask;
    }

    public Task<List<StreamProgress>> GetActiveProgressAsync()
    {
        return Task.FromResult(_progress.Values.ToList());
    }

    public Task CleanupAsync(TimeSpan olderThan)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        var toRemove = _progress.Where(kvp => kvp.Value.LastUpdated < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in toRemove)
        {
            _progress.Remove(key);
        }
        return Task.CompletedTask;
    }

    public void ReportProgress(FileProcessingProgress progress)
    {
        // Mock implementation
    }

    public Task<string> StartTrackingAsync(string operationId, MCPsharp.Models.Consolidated.ProgressInfo progressInfo, CancellationToken ct = default)
    {
        return Task.FromResult(operationId);
    }

    public Task CompleteTrackingAsync(string progressId, MCPsharp.Models.Consolidated.ProgressResult result, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}

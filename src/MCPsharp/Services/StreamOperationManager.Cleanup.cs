using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

public partial class StreamOperationManager
{
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
}

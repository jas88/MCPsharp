using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Streaming operation progress, cancellation, resumption and cleanup tools
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Get progress information for a streaming operation
    /// </summary>
    /// <param name="arguments">Tool arguments containing operationId</param>
    /// <returns>Tool execution result with progress information</returns>
    private async Task<ToolCallResult> ExecuteGetStreamProgress(JsonDocument arguments)
    {
        if (_progressTracker == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "ProgressTracker is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();

            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "operationId is required" };
            }

            var progress = await _progressTracker.GetProgressAsync(operationId);

            if (progress == null)
            {
                return new ToolCallResult { Success = false, Error = $"Operation not found: {operationId}" };
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    operationId = operationId,
                    progressPercentage = progress.ProgressPercentage,
                    bytesProcessed = progress.BytesProcessed,
                    totalBytes = progress.TotalBytes,
                    itemsProcessed = progress.ItemsProcessed,
                    processingSpeed = progress.ProcessingSpeedBytesPerSecond,
                    estimatedTimeRemaining = progress.EstimatedTimeRemaining.TotalSeconds,
                    lastUpdated = progress.LastUpdated,
                    currentPhase = progress.CurrentPhase,
                    metadata = progress.Metadata
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetStreamProgress));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Cancel a running streaming operation
    /// </summary>
    /// <param name="arguments">Tool arguments containing operationId</param>
    /// <returns>Tool execution result indicating success or failure</returns>
    private async Task<ToolCallResult> ExecuteCancelStreamOperation(JsonDocument arguments)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();

            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "operationId is required" };
            }

            var cancelled = await _streamingProcessor.CancelOperationAsync(operationId);

            return new ToolCallResult
            {
                Success = cancelled,
                Result = cancelled ? (object)new { operationId, cancelled = true } : null,
                Error = cancelled ? null : $"Failed to cancel operation or operation not found: {operationId}"
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteCancelStreamOperation));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Resume a previously cancelled or failed operation from checkpoint
    /// </summary>
    /// <param name="arguments">Tool arguments containing operationId</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with resumption details</returns>
    private async Task<ToolCallResult> ExecuteResumeStreamOperation(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_streamingProcessor == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "StreamingFileProcessor is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationId = arguments.RootElement.GetProperty("operationId").GetString();

            if (string.IsNullOrEmpty(operationId))
            {
                return new ToolCallResult { Success = false, Error = "operationId is required" };
            }

            var result = await _streamingProcessor.ResumeOperationAsync(operationId, cancellationToken);
            if (result == null)
                throw new InvalidOperationException("Resume operation returned null result");

            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? (object)new
                {
                    operationId = result.OperationId,
                    bytesProcessed = result.BytesProcessed,
                    chunksProcessed = result.ChunksProcessed,
                    processingTime = result.ProcessingTime.TotalSeconds
                } : null,
                Error = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteResumeStreamOperation));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// List all active and recent streaming operations
    /// </summary>
    /// <param name="arguments">Tool arguments containing optional maxCount parameter</param>
    /// <returns>Tool execution result with list of operations</returns>
    private async Task<ToolCallResult> ExecuteListStreamOperations(JsonDocument arguments)
    {
        if (_progressTracker == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "ProgressTracker is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var maxCount = arguments.RootElement.TryGetProperty("maxCount", out var maxCountEl) ? maxCountEl.GetInt32() : 50;

            var operations = await _progressTracker.GetActiveProgressAsync();
            if (maxCount > 0 && operations.Count > maxCount)
            {
                operations = operations.Take(maxCount).ToList();
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    operations = operations.Select((op, index) => new
                    {
                        operationId = $"op_{index}", // StreamProgress doesn't have OperationId
                        progressPercentage = op.ProgressPercentage,
                        bytesProcessed = op.BytesProcessed,
                        totalBytes = op.TotalBytes,
                        chunksProcessed = op.ChunksProcessed,
                        totalChunks = op.TotalChunks,
                        linesProcessed = op.LinesProcessed,
                        itemsProcessed = op.ItemsProcessed,
                        lastUpdated = op.LastUpdated,
                        estimatedTimeRemaining = op.EstimatedTimeRemaining.TotalSeconds,
                        processingSpeed = op.ProcessingSpeedBytesPerSecond,
                        currentPhase = op.CurrentPhase,
                        metadata = op.Metadata
                    }).ToArray(),
                    count = operations.Count
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteListStreamOperations));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Clean up temporary files and completed operations
    /// </summary>
    /// <param name="arguments">Tool arguments containing optional olderThanHours parameter</param>
    /// <returns>Tool execution result with cleanup details</returns>
    private async Task<ToolCallResult> ExecuteCleanupStreamOperations(JsonDocument arguments)
    {
        if (_tempFileManager == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "TempFileManager is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var olderThanHours = arguments.RootElement.TryGetProperty("olderThanHours", out var hoursEl) ? hoursEl.GetInt32() : 24;

            await _tempFileManager.CleanupAsync(TimeSpan.FromHours(olderThanHours));
            var stats = await _tempFileManager.GetStatsAsync();

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    filesDeleted = stats.FileCount,
                    totalSizeBytes = stats.TotalSizeBytes,
                    directoriesDeleted = stats.DirectoryCount,
                    oldestFile = stats.OldestFile,
                    newestFile = stats.NewestFile,
                    filesByOperation = stats.FilesByOperation,
                    sizeByExtension = stats.SizeByExtension,
                    olderThanHours = olderThanHours
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteCleanupStreamOperations));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}

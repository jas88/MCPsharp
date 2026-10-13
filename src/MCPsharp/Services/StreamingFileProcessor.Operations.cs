using System.Text.Json;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

public partial class StreamingFileProcessor
{
    public async Task<StreamProgress?> GetProgressAsync(string operationId)
    {
        if (_operations.TryGetValue(operationId, out _))
        {
            return await _progressTracker.GetProgressAsync(operationId);
        }
        return null;
    }

    public async Task<bool> CancelOperationAsync(string operationId)
    {
        if (_operations.TryGetValue(operationId, out var operation))
        {
            operation.Status = StreamOperationStatus.Cancelled;
            await operation.CancellationTokenSource.CancelAsync();

            LogCancelledOperation(_logger, operationId);
            return true;
        }
        return false;
    }

    public async Task<StreamResult?> ResumeOperationAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            LogOperationNotFoundForResume(_logger, operationId);
            return null;
        }

        if (operation.LastCheckpoint == null)
        {
            LogOperationHasNoCheckpointTo(_logger, operationId);
            return null;
        }

        try
        {
            operation.Status = StreamOperationStatus.Resumed;
            operation.StartedAt = DateTime.UtcNow;

            var result = await ResumeFromCheckpointAsync(operation, cancellationToken);

            operation.Status = StreamOperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            LogSuccessfullyResumedOperation(_logger, operationId);
            return result;
        }
        catch (Exception ex)
        {
            operation.Status = StreamOperationStatus.Failed;
            operation.Exception = ex;
            operation.ErrorMessage = ex.Message;

            LogFailedToResumeOperation(_logger, ex, operationId);
            return null;
        }
    }

    public async Task<CheckpointData?> CreateCheckpointAsync(string operationId)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            return null;
        }

        var progress = await _progressTracker.GetProgressAsync(operationId);
        if (progress == null)
        {
            return null;
        }

        var checkpoint = new CheckpointData
        {
            CreatedAt = DateTime.UtcNow,
            Position = progress.BytesProcessed,
            BytesProcessed = progress.BytesProcessed,
            ChunksProcessed = progress.ChunksProcessed,
            LinesProcessed = progress.LinesProcessed,
            CustomState = progress.Metadata,
            CheckpointDirectory = operation.Request.CheckpointDirectory ?? string.Empty
        };

        // Save checkpoint to file
        if (!string.IsNullOrEmpty(operation.Request.CheckpointDirectory))
        {
            checkpoint.CheckpointFilePath = Path.Combine(operation.Request.CheckpointDirectory, $"checkpoint_{checkpoint.CheckpointId}.json");
            await SaveCheckpointAsync(checkpoint);
        }

        operation.LastCheckpoint = checkpoint;
        operation.LastCheckpointAt = DateTime.UtcNow;

        LogCreatedCheckpointForOperationAt(_logger, checkpoint.CheckpointId, operationId, checkpoint.Position);

        return checkpoint;
    }

    public Task<List<StreamOperation>> ListOperationsAsync(int maxCount = 50)
    {
        return Task.FromResult(_operations.Values
            .OrderByDescending(o => o.CreatedAt)
            .Take(maxCount)
            .ToList());
    }

    public async Task CleanupAsync(TimeSpan? olderThan = null)
    {
        var cutoffTime = DateTime.UtcNow - (olderThan ?? TimeSpan.FromHours(24));
        var toRemove = _operations
            .Where(kvp => kvp.Value.CompletedAt.HasValue && kvp.Value.CompletedAt.Value < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var operationId in toRemove)
        {
            if (_operations.TryRemove(operationId, out _))
            {
                await _tempFileManager.CleanupAsync(operationId);
                await _progressTracker.RemoveProgressAsync(operationId);
            }
        }

        // Also clean up temp files
        await _tempFileManager.CleanupAsync(olderThan ?? TimeSpan.FromHours(2));
        await _progressTracker.CleanupAsync(olderThan ?? TimeSpan.FromHours(2));

        if (toRemove.Count > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            var retention = olderThan ?? TimeSpan.FromHours(24);
            LogCleanedUpCompletedOperationsOlder(_logger, toRemove.Count, retention);
        }
    }

    private async Task<StreamResult> ResumeFromCheckpointAsync(StreamOperation operation, CancellationToken cancellationToken)
    {
        var checkpoint = operation.LastCheckpoint!;
        var request = operation.Request;
        var processor = _processors[request.ProcessorType];

        using var inputStream = new FileStream(request.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var outputStream = new FileStream(request.OutputPath, FileMode.Append, FileAccess.Write, FileShare.None);

        // Seek to checkpoint position
        inputStream.Seek(checkpoint.Position, SeekOrigin.Begin);

        var reader = CreateChunkReader(inputStream, request);
        var nextLineNumber = 1 + await CountLineBreaksBeforeAsync(request.FilePath, checkpoint.Position, cancellationToken);
        var chunkIndex = checkpoint.ChunksProcessed;
        var processedBytes = checkpoint.BytesProcessed;
        var processedChunks = checkpoint.ChunksProcessed;
        var processedLines = checkpoint.LinesProcessed;
        var processedItems = 0L;
        var peakBufferBytes = 0L;

        await _progressTracker.SetPhaseAsync(operation.OperationId, "Resuming from checkpoint");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunkData = await reader.ReadChunkAsync(cancellationToken);
            if (chunkData == null)
                break;

            var bytesRead = chunkData.Length;
            var chunk = new StreamChunk
            {
                Data = chunkData,
                Position = processedBytes,
                Length = bytesRead,
                ChunkIndex = chunkIndex,
                IsLastChunk = reader.IsAtEnd,
                Metadata = new Dictionary<string, object>(checkpoint.CustomState)
                {
                    [LineStreamProcessor.StartLineNumberKey] = nextLineNumber
                }
            };

            var processedChunk = await processor.ProcessChunkAsync(chunk, request.ProcessorOptions);
            peakBufferBytes = Math.Max(peakBufferBytes, (long)reader.BufferSize + chunkData.Length + processedChunk.Data.Length);
            nextLineNumber += TextChunkLines.CountLineBreaks(chunkData);

            processedBytes += bytesRead;
            processedChunks++;
            processedLines += processedChunk.Lines.Count;
            processedItems += processedChunk.Lines.Count > 0 ? processedChunk.Lines.Count : 1;

            if (request.EnableCompression)
            {
                await WriteCompressedAsync(outputStream, processedChunk.Data, cancellationToken);
            }
            else
            {
                await outputStream.WriteAsync(processedChunk.Data.AsMemory(0, processedChunk.Data.Length), cancellationToken);
            }

            await _progressTracker.UpdateProgressAsync(operation.OperationId, processedBytes, processedChunks, processedLines, processedItems);

            if (request.CreateCheckpoint && processedChunks % 100 == 0)
            {
                await CreateCheckpointAsync(operation.OperationId);
            }

            chunkIndex++;
        }

        await outputStream.FlushAsync(cancellationToken);

        var processingTime = DateTime.UtcNow - operation.StartedAt!.Value;
        var originalSize = new FileInfo(request.FilePath).Length;

        try
        {
            return new StreamResult
            {
                Success = true,
                OperationId = operation.OperationId,
                OutputPath = request.OutputPath,
                OriginalSize = originalSize,
                BytesProcessed = processedBytes,
                ChunksProcessed = processedChunks,
                LinesProcessed = processedLines,
                ItemsProcessed = processedItems,
                // Peak size of the read buffer plus the input and processed copies of one chunk
                MemoryUsage = peakBufferBytes,
                ProcessingTime = processingTime,
                ProcessingSpeedBytesPerSecond = BytesPerSecond(processedBytes, processingTime),
                OutputFiles = new List<string> { request.OutputPath },
                TemporaryFiles = operation.TemporaryFiles,
                FinalCheckpoint = operation.LastCheckpoint
            };
        }
        catch (Exception ex)
        {
            LogErrorCreatingStreamResultProcessedBytesProcessingTime(_logger, ex, processedBytes, processingTime, processingTime.TotalSeconds);
            throw;
        }
    }

    /// <summary>
    /// Counts the line breaks in the first <paramref name="length"/> bytes of a file, so line numbering
    /// continues correctly when processing resumes from a checkpoint. Checkpoints of text processors fall
    /// on line boundaries, so a "\r\n" pair is never split between the counted prefix and the rest.
    /// </summary>
    private static async Task<long> CountLineBreaksBeforeAsync(string filePath, long length, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[81920];
        var remaining = length;
        var count = 0L;
        var previousEndedWithCarriageReturn = false;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
                break;

            count += TextChunkLines.CountLineBreaks(buffer.AsSpan(0, read));

            // A "\r\n" split across two reads was counted twice: once for the '\r' and once for the '\n'
            if (previousEndedWithCarriageReturn && buffer[0] == '\n')
                count--;

            previousEndedWithCarriageReturn = buffer[read - 1] == '\r';
            remaining -= read;
        }

        return count;
    }

    private async Task SaveCheckpointAsync(CheckpointData checkpoint)
    {
        try
        {
            var json = JsonSerializer.Serialize(checkpoint, IndentedJsonOptions);
            await File.WriteAllTextAsync(checkpoint.CheckpointFilePath, json);
        }
        catch (Exception ex)
        {
            LogFailedToSaveCheckpoint(_logger, ex, checkpoint.CheckpointId);
        }
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            CleanupAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            LogErrorDuringAutomaticCleanup(_logger, ex);
        }
    }
}

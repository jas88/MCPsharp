using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;

namespace MCPsharp.Services;

public partial class StreamingFileProcessor
{
    private async Task<StreamResult> ProcessFileStreamAsync(StreamOperation operation, CancellationToken cancellationToken)
    {
        var request = operation.Request;
        var processor = _processors[request.ProcessorType];

        using var inputStream = new FileStream(request.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var outputStream = new FileStream(request.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var reader = CreateChunkReader(inputStream, request);
        var nextLineNumber = 1L;
        var chunkIndex = 0;
        var processedBytes = 0L;
        var processedChunks = 0L;
        var processedLines = 0L;
        var processedItems = 0L;
        var peakBufferBytes = 0L;

        await _progressTracker.SetPhaseAsync(operation.OperationId, "Processing");

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
                Metadata = new Dictionary<string, object>
                {
                    ["ChunkIndex"] = chunkIndex,
                    ["OriginalSize"] = bytesRead,
                    [LineStreamProcessor.StartLineNumberKey] = nextLineNumber
                }
            };

            var processedChunk = await processor.ProcessChunkAsync(chunk, request.ProcessorOptions);
            peakBufferBytes = Math.Max(peakBufferBytes, (long)reader.BufferSize + chunkData.Length + processedChunk.Data.Length);
            nextLineNumber += TextChunkLines.CountLineBreaks(chunkData);

            // Update counters
            processedBytes += bytesRead;
            processedChunks++;
            processedLines += processedChunk.Lines.Count;
            processedItems += processedChunk.Lines.Count > 0 ? processedChunk.Lines.Count : 1;

            // Write processed data
            if (request.EnableCompression)
            {
                // Simple compression - in practice, you'd use a proper compression library
                await WriteCompressedAsync(outputStream, processedChunk.Data, cancellationToken);
            }
            else
            {
                await outputStream.WriteAsync(processedChunk.Data.AsMemory(0, processedChunk.Data.Length), cancellationToken);
            }

            // Update progress
            await _progressTracker.UpdateProgressAsync(operation.OperationId, processedBytes, processedChunks, processedLines, processedItems);

            // Create checkpoint if configured and at interval
            if (request.CreateCheckpoint && processedChunks % 100 == 0)
            {
                await CreateCheckpointAsync(operation.OperationId);
            }

            chunkIndex++;
        }

        await outputStream.FlushAsync(cancellationToken);

        // Create final checkpoint if enabled
        if (request.CreateCheckpoint)
        {
            await CreateCheckpointAsync(operation.OperationId);
        }

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
    /// Creates the chunk reader for a request. Text processors get line-aligned chunks so no line is
    /// split between two chunks; the binary processor gets fixed-size chunks.
    /// </summary>
    private static StreamChunkReader CreateChunkReader(Stream inputStream, StreamProcessRequest request)
        => new(inputStream, (int)request.ChunkSize, alignToLines: request.ProcessorType != StreamProcessorType.BinaryProcessor);

    /// <summary>
    /// Processing speed in bytes per second. Computed in floating point: truncating the elapsed seconds
    /// to an integer divides by zero for any run shorter than a second.
    /// </summary>
    private static long BytesPerSecond(long bytes, TimeSpan elapsed)
        => elapsed.TotalSeconds > 0 && bytes > 0 ? (long)(bytes / elapsed.TotalSeconds) : 0;

    private static async Task WriteCompressedAsync(Stream outputStream, byte[] data, CancellationToken cancellationToken)
    {
        // Simple placeholder for compression - in practice, you'd use System.IO.Compression
        await outputStream.WriteAsync(data.AsMemory(0, data.Length), cancellationToken);
    }

    private static StreamResult CreateFailureResult(StreamOperation operation, string errorMessage)
    {
        return new StreamResult
        {
            Success = false,
            OperationId = operation.OperationId,
            ErrorMessage = errorMessage,
            Exception = operation.Exception,
            ProcessingTime = operation.CompletedAt - operation.StartedAt ?? TimeSpan.Zero,
            TemporaryFiles = operation.TemporaryFiles,
            ProcessingSpeedBytesPerSecond = 0
        };
    }
}

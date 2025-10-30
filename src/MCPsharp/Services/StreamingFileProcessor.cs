using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Service for streaming file processing operations
/// </summary>
public class StreamingFileProcessor : IStreamingFileProcessor, IDisposable
{
    private readonly ILogger<StreamingFileProcessor> _logger;
    private readonly IProgressTracker _progressTracker;
    private readonly ITempFileManager _tempFileManager;
    private readonly ConcurrentDictionary<string, StreamOperation> _operations;
    private readonly Dictionary<StreamProcessorType, IStreamProcessor> _processors;
    private readonly Timer _cleanupTimer;

    public StreamingFileProcessor(
        ILogger<StreamingFileProcessor> logger,
        IProgressTracker progressTracker,
        ITempFileManager tempFileManager)
    {
        _logger = logger;
        _progressTracker = progressTracker;
        _tempFileManager = tempFileManager;
        _operations = new ConcurrentDictionary<string, StreamOperation>();
        _processors = InitializeProcessors();

        // Run cleanup every 15 minutes
        _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
    }

    public async Task<StreamResult> ProcessFileAsync(StreamProcessRequest request, CancellationToken cancellationToken = default)
    {
        var operation = new StreamOperation
        {
            Name = $"Process {Path.GetFileName(request.FilePath)}",
            Request = request,
            Status = StreamOperationStatus.Created
        };

        _operations.TryAdd(operation.OperationId, operation);

        try
        {
            // Validate request
            var (isValid, errors) = await ValidateRequestAsync(request);
            if (!isValid)
            {
                operation.Status = StreamOperationStatus.Failed;
                operation.ErrorMessage = $"Validation failed: {string.Join(", ", errors)}";
                return CreateFailureResult(operation, string.Join(", ", errors));
            }

            // Initialize progress tracking
            var fileInfo = new FileInfo(request.FilePath);
            await _progressTracker.CreateProgressAsync(operation.OperationId, operation.Name, fileInfo.Length);

            // Create output directory
            var outputDir = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Create temporary checkpoint directory if needed
            if (request.CreateCheckpoint)
            {
                var checkpointDir = await _tempFileManager.CreateTempDirectoryAsync("checkpoint", operation.OperationId);
                operation.Request.CheckpointDirectory = checkpointDir;
            }

            operation.Status = StreamOperationStatus.Running;
            operation.StartedAt = DateTime.UtcNow;

            var result = await ProcessFileStreamAsync(operation, cancellationToken);

            operation.Status = StreamOperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
            await _progressTracker.CompleteProgressAsync(operation.OperationId);

            _logger.LogInformation("Successfully processed file {FilePath} to {OutputPath} in {ProcessingTime}",
                request.FilePath, request.OutputPath, result.ProcessingTime);

            return result;
        }
        catch (OperationCanceledException)
        {
            operation.Status = StreamOperationStatus.Cancelled;
            operation.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("Operation {OperationId} was cancelled", operation.OperationId);
            throw;
        }
        catch (Exception ex)
        {
            operation.Status = StreamOperationStatus.Failed;
            operation.Exception = ex;
            operation.ErrorMessage = ex.Message;
            operation.CompletedAt = DateTime.UtcNow;

            _logger.LogError(ex, "Failed to process file {FilePath}", request.FilePath);
            return CreateFailureResult(operation, ex.Message);
        }
    }

    public async Task<List<StreamResult>> BulkTransformAsync(BulkTransformRequest request, IProgress<StreamProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<StreamResult>();
        var inputFiles = await GetInputFilesAsync(request);

        _logger.LogInformation("Starting bulk transformation of {Count} files with {MaxDegree} parallel workers",
            inputFiles.Count, request.MaxDegreeOfParallelism);

        var semaphore = new SemaphoreSlim(request.MaxDegreeOfParallelism, request.MaxDegreeOfParallelism);
        var tasks = inputFiles.Select(async inputFile =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var outputPath = GetOutputPath(inputFile, request);
                var processRequest = new StreamProcessRequest
                {
                    FilePath = inputFile,
                    OutputPath = outputPath,
                    ProcessorType = request.ProcessorType,
                    ProcessorOptions = request.ProcessorOptions,
                    ChunkSize = request.ChunkSize,
                    EnableCompression = request.EnableCompression
                };

                return await ProcessFileAsync(processRequest, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var allResults = await Task.WhenAll(tasks);
        results.AddRange(allResults);

        _logger.LogInformation("Bulk transformation completed. Processed {Count} files with {SuccessCount} successes",
            results.Count, results.Count(r => r.Success));

        return results;
    }

    public async Task<StreamProgress?> GetProgressAsync(string operationId)
    {
        if (_operations.TryGetValue(operationId, out var operation))
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
            operation.CancellationTokenSource.Cancel();

            _logger.LogInformation("Cancelled operation {OperationId}", operationId);
            return true;
        }
        return false;
    }

    public async Task<StreamResult?> ResumeOperationAsync(string operationId, CancellationToken cancellationToken = default)
    {
        if (!_operations.TryGetValue(operationId, out var operation))
        {
            _logger.LogWarning("Operation {OperationId} not found for resume", operationId);
            return null;
        }

        if (operation.LastCheckpoint == null)
        {
            _logger.LogWarning("Operation {OperationId} has no checkpoint to resume from", operationId);
            return null;
        }

        try
        {
            operation.Status = StreamOperationStatus.Resumed;
            operation.StartedAt = DateTime.UtcNow;

            var result = await ResumeFromCheckpointAsync(operation, cancellationToken);

            operation.Status = StreamOperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Successfully resumed operation {OperationId}", operationId);
            return result;
        }
        catch (Exception ex)
        {
            operation.Status = StreamOperationStatus.Failed;
            operation.Exception = ex;
            operation.ErrorMessage = ex.Message;

            _logger.LogError(ex, "Failed to resume operation {OperationId}", operationId);
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

        _logger.LogDebug("Created checkpoint {CheckpointId} for operation {OperationId} at position {Position}",
            checkpoint.CheckpointId, operationId, checkpoint.Position);

        return checkpoint;
    }

    public async Task<List<StreamOperation>> ListOperationsAsync(int maxCount = 50)
    {
        return _operations.Values
            .OrderByDescending(o => o.CreatedAt)
            .Take(maxCount)
            .ToList();
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
            if (_operations.TryRemove(operationId, out var operation))
            {
                await _tempFileManager.CleanupAsync(operationId);
                await _progressTracker.RemoveProgressAsync(operationId);
            }
        }

        // Also clean up temp files
        await _tempFileManager.CleanupAsync(olderThan ?? TimeSpan.FromHours(2));
        await _progressTracker.CleanupAsync(olderThan ?? TimeSpan.FromHours(2));

        if (toRemove.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} completed operations older than {OlderThan}",
                toRemove.Count, olderThan ?? TimeSpan.FromHours(24));
        }
    }

    public async Task<List<ChunkProcessor>> GetAvailableProcessorsAsync()
    {
        return _processors.Select(kvp => new ChunkProcessor
        {
            Name = kvp.Key.ToString(),
            ProcessorType = kvp.Key,
            Options = new Dictionary<string, object>()
        }).ToList();
    }

    public async Task<(bool IsValid, List<string> Errors)> ValidateRequestAsync(StreamProcessRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.FilePath))
        {
            errors.Add("FilePath is required");
        }
        else if (!File.Exists(request.FilePath))
        {
            errors.Add($"File not found: {request.FilePath}");
        }

        if (string.IsNullOrEmpty(request.OutputPath))
        {
            errors.Add("OutputPath is required");
        }

        if (request.ChunkSize <= 0)
        {
            errors.Add("ChunkSize must be greater than 0");
        }

        // Validate processor options
        if (_processors.TryGetValue(request.ProcessorType, out var processor))
        {
            if (!await processor.ValidateOptionsAsync(request.ProcessorOptions))
            {
                errors.Add($"Invalid processor options for {request.ProcessorType}");
            }
        }
        else
        {
            errors.Add($"Unsupported processor type: {request.ProcessorType}");
        }

        return (errors.Count == 0, errors);
    }

    public async Task<TimeSpan> EstimateProcessingTimeAsync(StreamProcessRequest request)
    {
        try
        {
            var fileInfo = new FileInfo(request.FilePath);
            var fileSize = fileInfo.Length;

            // Estimate based on file size and processor complexity
            var bytesPerSecond = request.ProcessorType switch
            {
                StreamProcessorType.LineProcessor => 50 * 1024 * 1024, // 50 MB/s
                StreamProcessorType.RegexProcessor => 20 * 1024 * 1024, // 20 MB/s
                StreamProcessorType.CsvProcessor => 30 * 1024 * 1024, // 30 MB/s
                StreamProcessorType.BinaryProcessor => 100 * 1024 * 1024, // 100 MB/s
                _ => 25 * 1024 * 1024 // 25 MB/s default
            };

            var seconds = fileSize / bytesPerSecond;
            return TimeSpan.FromSeconds(seconds);
        }
        catch
        {
            return TimeSpan.FromMinutes(1); // Default estimate
        }
    }

    private Dictionary<StreamProcessorType, IStreamProcessor> InitializeProcessors()
    {
        return new Dictionary<StreamProcessorType, IStreamProcessor>
        {
            { StreamProcessorType.LineProcessor, new LineStreamProcessor() },
            { StreamProcessorType.RegexProcessor, new RegexStreamProcessor() },
            { StreamProcessorType.CsvProcessor, new CsvStreamProcessor() },
            { StreamProcessorType.BinaryProcessor, new BinaryStreamProcessor() }
        };
    }

    private async Task<StreamResult> ProcessFileStreamAsync(StreamOperation operation, CancellationToken cancellationToken)
    {
        var request = operation.Request;
        var processor = _processors[request.ProcessorType];

        using var inputStream = new FileStream(request.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var outputStream = new FileStream(request.OutputPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[request.ChunkSize];
        var chunkIndex = 0;
        var totalBytes = inputStream.Length;
        var processedBytes = 0L;
        var processedChunks = 0L;
        var processedLines = 0L;
        var processedItems = 0L;

        await _progressTracker.SetPhaseAsync(operation.OperationId, "Processing");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0) break;

            var chunkData = new byte[bytesRead];
            Array.Copy(buffer, chunkData, bytesRead);

            var chunk = new StreamChunk
            {
                Data = chunkData,
                Position = processedBytes,
                Length = bytesRead,
                ChunkIndex = chunkIndex,
                IsLastChunk = inputStream.Position == inputStream.Length,
                Metadata = new Dictionary<string, object>
                {
                    ["ChunkIndex"] = chunkIndex,
                    ["OriginalSize"] = bytesRead
                }
            };

            var processedChunk = await processor.ProcessChunkAsync(chunk, request.ProcessorOptions);

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
                await outputStream.WriteAsync(processedChunk.Data, 0, processedChunk.Data.Length, cancellationToken);
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

        return new StreamResult
        {
            Success = true,
            OperationId = operation.OperationId,
            OutputPath = request.OutputPath,
            BytesProcessed = processedBytes,
            ChunksProcessed = processedChunks,
            LinesProcessed = processedLines,
            ItemsProcessed = processedItems,
            ProcessingTime = processingTime,
            ProcessingSpeedBytesPerSecond = processedBytes / (long)processingTime.TotalSeconds,
            OutputFiles = new List<string> { request.OutputPath },
            TemporaryFiles = operation.TemporaryFiles,
            FinalCheckpoint = operation.LastCheckpoint
        };
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

        var buffer = new byte[request.ChunkSize];
        var chunkIndex = checkpoint.ChunksProcessed;
        var processedBytes = checkpoint.BytesProcessed;
        var processedChunks = checkpoint.ChunksProcessed;
        var processedLines = checkpoint.LinesProcessed;
        var processedItems = 0L;

        await _progressTracker.SetPhaseAsync(operation.OperationId, "Resuming from checkpoint");

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var bytesRead = await inputStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead == 0) break;

            var chunkData = new byte[bytesRead];
            Array.Copy(buffer, chunkData, bytesRead);

            var chunk = new StreamChunk
            {
                Data = chunkData,
                Position = processedBytes,
                Length = bytesRead,
                ChunkIndex = chunkIndex,
                IsLastChunk = inputStream.Position == inputStream.Length,
                Metadata = checkpoint.CustomState
            };

            var processedChunk = await processor.ProcessChunkAsync(chunk, request.ProcessorOptions);

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
                await outputStream.WriteAsync(processedChunk.Data, 0, processedChunk.Data.Length, cancellationToken);
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

        return new StreamResult
        {
            Success = true,
            OperationId = operation.OperationId,
            OutputPath = request.OutputPath,
            BytesProcessed = processedBytes,
            ChunksProcessed = processedChunks,
            LinesProcessed = processedLines,
            ItemsProcessed = processedItems,
            ProcessingTime = processingTime,
            ProcessingSpeedBytesPerSecond = processedBytes / (long)processingTime.TotalSeconds,
            OutputFiles = new List<string> { request.OutputPath },
            TemporaryFiles = operation.TemporaryFiles,
            FinalCheckpoint = operation.LastCheckpoint
        };
    }

    private async Task<List<string>> GetInputFilesAsync(BulkTransformRequest request)
    {
        var inputFiles = new List<string>();

        foreach (var inputFile in request.InputFiles)
        {
            if (File.Exists(inputFile))
            {
                inputFiles.Add(inputFile);
            }
            else if (Directory.Exists(inputFile))
            {
                var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(inputFile, request.FilePattern ?? "*", searchOption);
                inputFiles.AddRange(files);
            }
        }

        return inputFiles.Distinct().ToList();
    }

    private string GetOutputPath(string inputFile, BulkTransformRequest request)
    {
        var fileName = Path.GetFileName(inputFile);
        if (request.PreserveDirectoryStructure)
        {
            var relativePath = Path.GetRelativePath(Directory.GetCurrentDirectory(), inputFile);
            var relativeDir = Path.GetDirectoryName(relativePath);
            if (!string.IsNullOrEmpty(relativeDir))
            {
                return Path.Combine(request.OutputDirectory, relativeDir, fileName);
            }
        }

        return Path.Combine(request.OutputDirectory, fileName);
    }

    private async Task WriteCompressedAsync(Stream outputStream, byte[] data, CancellationToken cancellationToken)
    {
        // Simple placeholder for compression - in practice, you'd use System.IO.Compression
        await outputStream.WriteAsync(data, 0, data.Length, cancellationToken);
    }

    private async Task SaveCheckpointAsync(CheckpointData checkpoint)
    {
        try
        {
            var json = JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(checkpoint.CheckpointFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save checkpoint {CheckpointId}", checkpoint.CheckpointId);
        }
    }

    private StreamResult CreateFailureResult(StreamOperation operation, string errorMessage)
    {
        return new StreamResult
        {
            Success = false,
            OperationId = operation.OperationId,
            ErrorMessage = errorMessage,
            Exception = operation.Exception,
            ProcessingTime = operation.CompletedAt - operation.StartedAt ?? TimeSpan.Zero,
            TemporaryFiles = operation.TemporaryFiles
        };
    }

    private void PerformCleanup(object? state)
    {
        try
        {
            CleanupAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();

        // Cancel all running operations
        foreach (var operation in _operations.Values.Where(o => o.Status == StreamOperationStatus.Running))
        {
            operation.CancellationTokenSource.Cancel();
        }

        // Cleanup resources
        CleanupAsync(TimeSpan.Zero).GetAwaiter().GetResult();
    }
}
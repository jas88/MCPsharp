using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models.Streaming;
using MCPsharp.Models;
using MCPsharp.Services;
using ConsolidatedStream = MCPsharp.Models.Consolidated;
using StreamingStream = MCPsharp.Models.Streaming;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Stream Processing Controller consolidating all stream processing operations
/// Phase 4: Consolidate 40 stream processing tools into 6 unified tools
/// </summary>
public partial class StreamProcessingController
{
    private readonly IProgressTracker? _progressTracker;
    private readonly ILogger<StreamProcessingController> _logger;

    // Active stream operations
    private readonly Dictionary<string, StreamOperationContext> _activeStreams = new();
    private readonly object _streamsLock = new object();

    // Processor registry
    private readonly Dictionary<string, IStreamProcessor> _processors = new();

    public StreamProcessingController(
        IStreamingFileProcessor? streamingProcessor = null,
        IProgressTracker? progressTracker = null,
        ITempFileManager? tempFileManager = null,
        ILogger<StreamProcessingController>? logger = null)
    {
        _progressTracker = progressTracker;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StreamProcessingController>.Instance;

        RegisterBuiltInProcessors();
    }

    /// <summary>
    /// stream_process - Process streams
    /// Consolidates: stream_process_file, bulk_transform
    /// </summary>
    public async Task<StreamProcessResponse> ProcessStreamAsync(ConsolidatedStream.StreamProcessRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var operationId = request.OperationId ?? Guid.NewGuid().ToString();

        try
        {
            var response = new StreamProcessResponse
            {
                OperationId = operationId,
                ProcessorType = request.ProcessorType,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString()
            };

            // Validate request
            var validation = ValidateStreamRequest(request);
            if (!validation.IsValid)
            {
                response.Error = validation.ErrorMessage;
                response.Success = false;
                return response;
            }

            // Create operation context
            var context = new StreamOperationContext
            {
                Id = operationId,
                Type = StreamOperationType.Process,
                CreatedAt = DateTime.UtcNow,
                Status = StreamStatus.Running
            };

            lock (_streamsLock)
            {
                _activeStreams[operationId] = context;
            }

            context.AddLog($"Stream processing started with processor '{request.ProcessorType}' ({request.InputType})");

            try
            {
                // Initialize progress tracking
                if (_progressTracker != null)
                {
                    var estimatedSteps = EstimateProcessingSteps(request);
                    context.ProgressId = await _progressTracker.StartTrackingAsync(operationId, new ProgressInfo
                    {
                        Title = $"Stream processing with {request.ProcessorType}",
                        TotalSteps = estimatedSteps,
                        CurrentStep = 0
                    }, ct);
                }

                // Get or create processor
                var processor = await GetProcessorAsync(request.ProcessorType, request.ProcessorOptions, ct);
                if (processor == null)
                {
                    response.Error = $"Processor type '{request.ProcessorType}' not available";
                    response.Success = false;
                    context.Status = StreamStatus.Failed;
                    context.AddLog(response.Error);
                    return response;
                }

                // Process based on input type
                var results = request.InputType switch
                {
                    StreamInputType.File => await ProcessFileStreamAsync(request, processor, context, ct),
                    StreamInputType.Directory => await ProcessDirectoryStreamAsync(request, processor, context, ct),
                    StreamInputType.MultipleFiles => await ProcessMultipleFilesStreamAsync(request, processor, context, ct),
                    StreamInputType.Stream => ProcessDataStream(),
                    _ => throw new ArgumentException($"Unsupported input type: {request.InputType}")
                };

                response.Results = results;
                response.Summary = new StreamProcessSummary
                {
                    TotalFiles = results.Count,
                    SuccessfulFiles = results.Count(r => r.Success),
                    FailedFiles = results.Count(r => !r.Success),
                    TotalBytesProcessed = results.Sum(r => r.BytesProcessed ?? 0),
                    TotalRecordsProcessed = results.Sum(r => r.RecordsProcessed ?? 0),
                    ExecutionTime = stopwatch.Elapsed
                };

                response.Success = response.Summary.FailedFiles == 0;
                context.Status = response.Success ? StreamStatus.Completed : StreamStatus.Failed;
                context.AddLog($"Stream processing finished: {response.Summary.SuccessfulFiles}/{response.Summary.TotalFiles} files succeeded");

                // Complete progress tracking
                if (_progressTracker != null && context.ProgressId != null)
                {
                    await _progressTracker.CompleteTrackingAsync(context.ProgressId, new ProgressResult
                    {
                        Success = response.Success,
                        Message = response.Success ? "Stream processing completed" : "Stream processing failed",
                        Metrics = new Dictionary<string, object>
                        {
                            ["TotalFiles"] = response.Summary.TotalFiles,
                            ["TotalBytes"] = response.Summary.TotalBytesProcessed,
                            ["TotalRecords"] = response.Summary.TotalRecordsProcessed
                        }
                    }, ct);
                }
            }
            catch (Exception ex)
            {
                response.Error = ex.Message;
                response.Success = false;
                context.Status = StreamStatus.Failed;
                context.AddLog($"Stream processing failed: {ex.Message}");
                LogErrorProcessingStream(_logger, ex, operationId);
            }
            finally
            {
                // Cleanup resources
                CleanupStreamContext(context);
            }

            stopwatch.Stop();
            response.ExecutionTime = stopwatch.Elapsed;
            response.Metadata = new ResponseMetadata
            {
                ProcessingTime = stopwatch.Elapsed,
                Success = response.Success,
                ResultCount = response.Results?.Count ?? 0
            };

            return response;
        }
        catch (Exception ex)
        {
            LogFatalErrorInStreamProcessing(_logger, ex, operationId);

            return new StreamProcessResponse
            {
                OperationId = operationId,
                ProcessorType = request.ProcessorType,
                Error = ex.Message,
                Success = false,
                ExecutionTime = stopwatch.Elapsed,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
        finally
        {
            // Schedule cleanup. Deliberately not tied to the request token:
            // cancelling the request must not skip removal of the tracked stream.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(10), CancellationToken.None);
                lock (_streamsLock)
                {
                    _activeStreams.Remove(operationId);
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// stream_monitor - Monitor stream operations
    /// Consolidates: Monitoring tools
    /// </summary>
    public async Task<StreamMonitorResponse> MonitorStreamAsync(string operationId, StreamMonitorRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {

            // Get operation context
            StreamOperationContext? context;
            lock (_streamsLock)
            {
                _activeStreams.TryGetValue(operationId, out context);
            }

            if (context == null)
            {
                return new StreamMonitorResponse
                {
                    OperationId = operationId,
                    Status = StreamStatus.NotFound,
                    CreatedAt = DateTime.UtcNow,
                    RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                    Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false },
                    Error = $"Stream operation {operationId} not found"
                };
            }

            // Build response data
            StreamProgressInfo? progressInfo = null;
            StreamMetrics? metrics = null;
            List<string>? recentLogs = null;
            StreamPerformanceData? performance = null;

            // Get progress from tracker
            if (_progressTracker != null && context.ProgressId != null)
            {
                var progress = await _progressTracker.GetProgressAsync(context.ProgressId);
                if (progress != null)
                {
                    // Extract progress info from metadata
                    progress.Metadata.TryGetValue("TotalSteps", out var totalStepsObj);
                    progress.Metadata.TryGetValue("CurrentStep", out var currentStepObj);
                    progress.Metadata.TryGetValue("CurrentItem", out var currentItemObj);
                    progress.Metadata.TryGetValue("Message", out var messageObj);
                    progress.Metadata.TryGetValue("PercentComplete", out var percentCompleteObj);

                    var totalSteps = totalStepsObj as int? ?? 0;
                    var currentStep = currentStepObj as int? ?? 0;
                    var currentItem = currentItemObj as string ?? string.Empty;
                    var message = messageObj as string ?? string.Empty;
                    var percentComplete = percentCompleteObj as double? ?? 0.0;

                    progressInfo = new StreamProgressInfo
                    {
                        CurrentStep = currentStep,
                        TotalSteps = totalSteps,
                        PercentComplete = percentComplete,
                        CurrentFile = currentItem,
                        ElapsedTime = DateTime.UtcNow - progress.LastUpdated,
                        EstimatedTimeRemaining = progress.EstimatedTimeRemaining,
                        Message = message,
                        ProcessingRate = CalculateProcessingRate(context),
                        MemoryUsage = GetMemoryUsage(),
                        CpuUsage = GetCpuUsage()
                    };
                }
            }

            // Get detailed metrics
            metrics = GetStreamMetrics(context);

            // Get recent logs
            if (request.IncludeLogs)
            {
                recentLogs = GetRecentStreamLogs(context, request.MaxLogEntries ?? 50);
            }

            // Get performance data
            if (request.IncludePerformance)
            {
                performance = GetStreamPerformance(context);
            }

            stopwatch.Stop();

            return new StreamMonitorResponse
            {
                OperationId = operationId,
                Status = context.Status,
                CreatedAt = context.CreatedAt,
                StartedAt = context.StartedAt,
                EstimatedCompletion = context.EstimatedCompletion,
                Progress = progressInfo,
                Metrics = metrics,
                Performance = performance,
                RecentLogs = recentLogs,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata
                {
                    ProcessingTime = stopwatch.Elapsed,
                    Success = true
                }
            };
        }
        catch (Exception ex)
        {
            LogErrorMonitoringStream(_logger, ex, operationId);

            return new StreamMonitorResponse
            {
                OperationId = operationId,
                Status = StreamStatus.Failed,
                CreatedAt = DateTime.UtcNow,
                Error = ex.Message,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// stream_management - Manage stream operations
    /// Consolidates: Management tools
    /// </summary>
    public Task<StreamManagementResponse> ManageStreamAsync(StreamManagementRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(ManageStream(request));
    }

    private StreamManagementResponse ManageStream(StreamManagementRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Build response data
            List<ActiveStreamInfo>? activeStreams = null;
            StreamCancelResult? cancelResult = null;
            StreamPauseResult? pauseResult = null;
            StreamResumeResult? resumeResult = null;
            StreamCleanupResult? cleanupResult = null;
            StreamRegisterResult? registerResult = null;
            StreamUnregisterResult? unregisterResult = null;
            string? error = null;

            switch (request.Action)
            {
                case StreamManagementAction.List:
                    activeStreams = ListActiveStreams();
                    break;

                case StreamManagementAction.Cancel:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        error = "Target operation ID is required for cancel operation";
                    }
                    else
                    {
                        cancelResult = CancelStream();
                    }
                    break;

                case StreamManagementAction.Pause:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        error = "Target operation ID is required for pause operation";
                    }
                    else
                    {
                        pauseResult = PauseStream();
                    }
                    break;

                case StreamManagementAction.Resume:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        error = "Target operation ID is required for resume operation";
                    }
                    else
                    {
                        resumeResult = ResumeStream();
                    }
                    break;

                case StreamManagementAction.Cleanup:
                    cleanupResult = CleanupStreams(request.MaxAge);
                    break;

                case StreamManagementAction.RegisterProcessor:
                    registerResult = RegisterProcessor(request.ProcessorDefinition!);
                    break;

                case StreamManagementAction.UnregisterProcessor:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        error = "Target processor name is required for unregister operation";
                    }
                    else
                    {
                        unregisterResult = UnregisterProcessor(request.Target);
                    }
                    break;

                default:
                    error = $"Unknown management action: {request.Action}";
                    break;
            }

            stopwatch.Stop();

            return new StreamManagementResponse
            {
                Action = request.Action,
                Target = request.Target,
                ActiveStreams = activeStreams,
                CancelResult = cancelResult,
                PauseResult = pauseResult,
                ResumeResult = resumeResult,
                CleanupResult = cleanupResult,
                RegisterResult = registerResult,
                UnregisterResult = unregisterResult,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata
                {
                    ProcessingTime = stopwatch.Elapsed,
                    Success = error == null
                },
                Error = error
            };
        }
        catch (Exception ex)
        {
            LogErrorManagingStreamOperations(_logger, ex);

            return new StreamManagementResponse
            {
                Action = request.Action,
                Target = request.Target,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    #region Private Methods

    private void RegisterBuiltInProcessors()
    {
        // Register built-in processors
        _processors["line"] = new LineStreamProcessor();
        _processors["regex"] = new RegexStreamProcessor();
        _processors["csv"] = new CsvStreamProcessor();
        _processors["json"] = new JsonStreamProcessor();
        _processors["binary"] = new BinaryStreamProcessor();
        _processors["xml"] = new XmlStreamProcessor();
        _processors["log"] = new LogStreamProcessor();
        _processors["transform"] = new TransformStreamProcessor();
    }

    private StreamValidationResult ValidateStreamRequest(ConsolidatedStream.StreamProcessRequest request)
    {
        // Validate input
        if (request.InputPath == null && request.InputStream == null)
        {
            return new StreamValidationResult
            {
                IsValid = false,
                ErrorMessage = "Input path or stream is required"
            };
        }

        // Validate processor
        if (!_processors.ContainsKey(request.ProcessorType))
        {
            return new StreamValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Unknown processor type: {request.ProcessorType}"
            };
        }

        // Validate paths
        if (request.InputPath != null)
        {
            switch (request.InputType)
            {
                case StreamInputType.File:
                    if (!File.Exists(request.InputPath))
                    {
                        return new StreamValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Input file not found: {request.InputPath}"
                        };
                    }
                    break;

                case StreamInputType.Directory:
                    if (!Directory.Exists(request.InputPath))
                    {
                        return new StreamValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Input directory not found: {request.InputPath}"
                        };
                    }
                    break;

                case StreamInputType.MultipleFiles:
                    if (request.Files is not { Count: > 0 })
                    {
                        return new StreamValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "File list is required for multiple files input"
                        };
                    }
                    break;
            }
        }

        return new StreamValidationResult { IsValid = true };
    }

    private async Task<IStreamProcessor?> GetProcessorAsync(string processorType, ProcessorOptions? options, CancellationToken ct)
    {
        if (_processors.TryGetValue(processorType, out var processor))
        {
            await processor.InitializeAsync(options ?? new ProcessorOptions(), ct);
            return processor;
        }

        // Try to create processor from factory if available
        return null;
    }

    private static int EstimateProcessingSteps(ConsolidatedStream.StreamProcessRequest request)
    {
        return request.InputType switch
        {
            StreamInputType.File => 10,
            StreamInputType.Directory => request.Files?.Count ?? 100,
            StreamInputType.MultipleFiles => request.Files?.Count ?? 10,
            _ => 10
        };
    }

    private async Task<List<StreamProcessResult>> ProcessFileStreamAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        CancellationToken ct)
    {
        if (request.InputPath == null)
            return new List<StreamProcessResult>();

        var result = new StreamProcessResult
        {
            InputPath = request.InputPath,
            OutputPath = request.OutputPath,
            ProcessorType = request.ProcessorType,
            Success = true
        };

        try
        {
            var fileInfo = new System.IO.FileInfo(request.InputPath);
            result.BytesProcessed = fileInfo.Length;

            using var inputStream = File.OpenRead(request.InputPath);
            using var outputStream = request.OutputPath != null
                ? File.Create(request.OutputPath)
                : Stream.Null;

            var processResult = await processor.ProcessAsync(inputStream, outputStream, new ProcessingContext
            {
                FilePath = request.InputPath,
                OperationId = context.Id,
                ChunkSize = request.ChunkSize ?? 65536,
                EnableCompression = request.EnableCompression,
                EnableCheckpoints = request.EnableCheckpoints
            }, ct);

            result.Success = processResult.Success;
            result.RecordsProcessed = processResult.RecordsProcessed;
            result.Error = processResult.Error;
            result.Message = processResult.Message;
            result.Metrics = processResult.Metrics;
        }
        catch (Exception ex)
        {
            LogStreamProcessingFailed(_logger, ex, context.Id);
            context.AddLog($"Failed to process '{request.InputPath}': {ex.Message}");
            result.Success = false;
            result.Error = ex.Message;
        }

        return new List<StreamProcessResult> { result };
    }

    private async Task<List<StreamProcessResult>> ProcessDirectoryStreamAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        CancellationToken ct)
    {
        if (request.InputPath == null)
            return new List<StreamProcessResult>();

        var results = new List<StreamProcessResult>();
        var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var file in Directory.EnumerateFiles(request.InputPath, request.SearchPattern ?? "*", searchOption))
        {
            var fileRequest = new ConsolidatedStream.StreamProcessRequest
            {
                InputType = StreamInputType.File,
                InputPath = file,
                OutputPath = GetOutputPath(request.OutputPath, file),
                ProcessorType = request.ProcessorType,
                ProcessorOptions = request.ProcessorOptions,
                ChunkSize = request.ChunkSize,
                EnableCompression = request.EnableCompression,
                EnableCheckpoints = request.EnableCheckpoints
            };

            var fileResults = await ProcessFileStreamAsync(fileRequest, processor, context, ct);
            results.AddRange(fileResults);

            // Update progress
            if (_progressTracker != null && context.ProgressId != null)
            {
                await _progressTracker.UpdateProgressAsync(context.ProgressId, new ProgressInfo
                {
                    Title = "Bulk Processing",
                    TotalSteps = request.Files?.Count ?? 0,
                    CurrentStep = results.Count,
                    CurrentItem = file,
                    Message = $"Processing {Path.GetFileName(file)}"
                }, ct);
            }
        }

        return results;
    }

    private async Task<List<StreamProcessResult>> ProcessMultipleFilesStreamAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        CancellationToken ct)
    {
        var results = new List<StreamProcessResult>();

        foreach (var file in request.Files ?? new List<string>())
        {
            if (!File.Exists(file)) continue;

            var fileRequest = new ConsolidatedStream.StreamProcessRequest
            {
                InputType = StreamInputType.File,
                InputPath = file,
                OutputPath = GetOutputPath(request.OutputPath, file),
                ProcessorType = request.ProcessorType,
                ProcessorOptions = request.ProcessorOptions,
                ChunkSize = request.ChunkSize,
                EnableCompression = request.EnableCompression,
                EnableCheckpoints = request.EnableCheckpoints
            };

            var fileResults = await ProcessFileStreamAsync(fileRequest, processor, context, ct);
            results.AddRange(fileResults);

            // Update progress
            if (_progressTracker != null && context.ProgressId != null)
            {
                await _progressTracker.UpdateProgressAsync(context.ProgressId, new ProgressInfo
                {
                    Title = "Bulk Processing",
                    TotalSteps = request.Files?.Count ?? 0,
                    CurrentStep = results.Count,
                    CurrentItem = file,
                    Message = $"Processing {Path.GetFileName(file)}"
                }, ct);
            }
        }

        return results;
    }

    private static List<StreamProcessResult> ProcessDataStream()
    {
        // Implementation for processing data streams
        return new List<StreamProcessResult>();
    }

    private static string GetOutputPath(string? outputPath, string inputFile)
    {
        if (string.IsNullOrEmpty(outputPath))
            return string.Empty;

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
            return outputPath;

        // Generate output path based on input
        var fileName = Path.GetFileNameWithoutExtension(inputFile);
        var extension = Path.GetExtension(inputFile);
        return Path.Combine(outputPath, $"{fileName}_processed{extension}");
    }

  
    private static StreamMetrics GetStreamMetrics(StreamOperationContext context)
    {
        // Implementation for gathering stream metrics
        return new StreamMetrics
        {
            BytesRead = context.BytesRead,
            BytesWritten = context.BytesWritten,
            RecordsProcessed = context.RecordsProcessed,
            ChunksProcessed = context.ChunksProcessed,
            AverageProcessingRate = context.AverageProcessingRate,
            PeakMemoryUsage = context.PeakMemoryUsage,
            ProcessorUtilization = context.ProcessorUtilization,
            IOWaitTime = context.IOWaitTime
        };
    }

    private static double CalculateProcessingRate(StreamOperationContext context)
    {
        if (context.ElapsedTime.TotalSeconds == 0)
            return 0;

        return context.BytesProcessed / context.ElapsedTime.TotalSeconds;
    }

    private static long GetMemoryUsage()
    {
        // Implementation for getting memory usage
        return GC.GetTotalMemory(false);
    }

    private static double GetCpuUsage()
    {
        // Average CPU utilisation of this process since it started, as a percentage of all cores
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var wallClock = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        if (wallClock <= TimeSpan.Zero)
        {
            return 0.0;
        }

        return process.TotalProcessorTime.TotalMilliseconds / (wallClock.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
    }

    private static StreamPerformanceData GetStreamPerformance(StreamOperationContext context)
    {
        // Implementation for performance data
        return new StreamPerformanceData
        {
            AverageThroughput = context.AverageProcessingRate,
            PeakThroughput = context.AverageProcessingRate * 1.5, // Estimate peak as 1.5x average
            AverageLatency = TimeSpan.FromMilliseconds(100), // Placeholder
            PeakLatency = TimeSpan.FromMilliseconds(200), // Placeholder
            CpuUtilization = GetCpuUsage(),
            MemoryUsage = GetMemoryUsage(),
            ThreadUtilization = 1, // Placeholder
            DiskReadOps = 0, // Placeholder
            DiskWriteOps = 0, // Placeholder
            DiskReadBytes = context.BytesRead,
            DiskWriteBytes = context.BytesWritten
        };
    }

    private static List<string> GetRecentStreamLogs(StreamOperationContext context, int maxEntries)
    {
        return context.GetRecentLogs(maxEntries);
    }

    private List<ActiveStreamInfo> ListActiveStreams()
    {
        List<ActiveStreamInfo> streams;

        lock (_streamsLock)
        {
            streams = _activeStreams.Values.Select(ctx => new ActiveStreamInfo
            {
                Id = ctx.Id,
                Type = ctx.Type,
                Status = ctx.Status,
                CreatedAt = ctx.CreatedAt,
                StartedAt = ctx.StartedAt,
                ProcessorType = ctx.ProcessorType ?? string.Empty,
                BytesProcessed = ctx.BytesProcessed,
                CurrentFile = ctx.CurrentFile ?? string.Empty
            }).ToList();
        }

        return streams;
    }

    private static StreamCancelResult CancelStream()
    {
        // Implementation for canceling streams
        return new StreamCancelResult
        {
            Success = true,
            Message = "Stream operation cancelled"
        };
    }

    private static StreamPauseResult PauseStream()
    {
        // Implementation for pausing streams
        return new StreamPauseResult
        {
            Success = true,
            Message = "Stream operation paused"
        };
    }

    private static StreamResumeResult ResumeStream()
    {
        // Implementation for resuming streams
        return new StreamResumeResult
        {
            Success = true,
            Message = "Stream operation resumed"
        };
    }

    private StreamCleanupResult CleanupStreams(TimeSpan? maxAge)
    {
        var cleanedCount = 0;
        var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(1));

        lock (_streamsLock)
        {
            var toRemove = _activeStreams
                .Where(kvp => kvp.Value.CreatedAt < cutoff || kvp.Value.Status == StreamStatus.Completed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _activeStreams.Remove(key);
                cleanedCount++;
            }
        }

        return new StreamCleanupResult
        {
            Success = true,
            CleanedOperations = cleanedCount,
            Message = $"Cleaned up {cleanedCount} stream operations"
        };
    }

    private static StreamRegisterResult RegisterProcessor(StreamProcessorDefinition definition)
    {
        // Implementation for registering custom processors
        return new StreamRegisterResult
        {
            Success = true,
            Message = $"Processor {definition.Name} registered successfully"
        };
    }

    private StreamUnregisterResult UnregisterProcessor(string processorName)
    {
        var success = _processors.Remove(processorName);

        return new StreamUnregisterResult
        {
            Success = success,
            Message = success ? $"Processor {processorName} unregistered" : $"Processor {processorName} not found"
        };
    }

    private static void CleanupStreamContext(StreamOperationContext context)
    {
        // Cleanup temporary files, close streams, etc.
        context.Status = context.Status == StreamStatus.Running ? StreamStatus.Completed : context.Status;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stream processing failed for operation {OperationId}")]
    private static partial void LogStreamProcessingFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing stream {OperationId}")]
    private static partial void LogErrorProcessingStream(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal error in stream processing {OperationId}")]
    private static partial void LogFatalErrorInStreamProcessing(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error monitoring stream {OperationId}")]
    private static partial void LogErrorMonitoringStream(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error managing stream operations")]
    private static partial void LogErrorManagingStreamOperations(ILogger logger, Exception exception);

    #endregion
}

#region Supporting Classes

/// <summary>
/// Stream operation context
/// </summary>
internal sealed class StreamOperationContext
{
    public required string Id { get; init; }
    public required StreamOperationType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
    public StreamStatus Status { get; set; }
    public string? ProgressId { get; set; }
    public string? ProcessorType { get; set; }
    public string? CurrentFile { get; set; }
    public long BytesProcessed { get; set; }
    public long BytesRead { get; set; }
    public long BytesWritten { get; set; }
    public long RecordsProcessed { get; set; }
    public int ChunksProcessed { get; set; }
    public TimeSpan ElapsedTime => DateTime.UtcNow - CreatedAt;
    public double AverageProcessingRate { get; set; }
    public long PeakMemoryUsage { get; set; }
    public double ProcessorUtilization { get; set; }
    public TimeSpan IOWaitTime { get; set; }

    private const int MaxRetainedLogEntries = 200;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logEntries = new();

    /// <summary>
    /// Records a timestamped log entry for this operation, retaining only the most recent entries
    /// </summary>
    public void AddLog(string message)
    {
        _logEntries.Enqueue($"{DateTime.UtcNow:O} {message}");
        while (_logEntries.Count > MaxRetainedLogEntries && _logEntries.TryDequeue(out _))
        {
            // Trim oldest entries
        }
    }

    /// <summary>
    /// Returns up to <paramref name="maxEntries"/> of the most recent log entries, oldest first
    /// </summary>
    public List<string> GetRecentLogs(int maxEntries)
    {
        var snapshot = _logEntries.ToArray();
        return snapshot.Skip(Math.Max(0, snapshot.Length - maxEntries)).ToList();
    }
}

/// <summary>
/// Stream validation result
/// </summary>
internal sealed class StreamValidationResult
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Stream processor interface
/// </summary>
public interface IStreamProcessor
{
    Task InitializeAsync(ProcessorOptions options, CancellationToken ct);
    Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct);
    Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct);
}

/// <summary>
/// Built-in stream processors
/// </summary>
public class LineStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "log", "text", "csv" },
            SupportedOutputFormats = new List<string> { "txt", "log", "text" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue
        });
}

public class RegexStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "log", "text", "csv", "json", "xml", "html" },
            SupportedOutputFormats = new List<string> { "txt", "log", "text", "json", "xml", "html" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue
        });
}

public class CsvStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "csv", "tsv", "txt" },
            SupportedOutputFormats = new List<string> { "csv", "tsv", "txt", "json" },
            SupportsStreaming = true,
            SupportsParallelProcessing = true,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}

public class JsonStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "json", "jsonl", "ndjson" },
            SupportedOutputFormats = new List<string> { "json", "jsonl", "ndjson", "csv", "txt" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}

public class BinaryStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "bin", "dat", "exe", "dll", "so", "dylib" },
            SupportedOutputFormats = new List<string> { "bin", "dat" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}

public class XmlStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "xml", "xhtml", "html", "svg", "config" },
            SupportedOutputFormats = new List<string> { "xml", "xhtml", "html", "json", "txt" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}

public class LogStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "log", "txt", "out", "err" },
            SupportedOutputFormats = new List<string> { "log", "txt", "json", "csv" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}

public class TransformStreamProcessor : IStreamProcessor
{
    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;
    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => Task.FromResult(new ProcessingResult { Success = true });
    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "csv", "json", "xml", "log", "bin" },
            SupportedOutputFormats = new List<string> { "txt", "csv", "json", "xml", "log", "bin" },
            SupportsStreaming = true,
            SupportsParallelProcessing = true,
            SupportsCheckpoints = true,
            SupportsCompression = true,
            MaxFileSize = long.MaxValue
        });
}

#endregion
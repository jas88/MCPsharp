using MCPsharp.Models.Consolidated;
using Microsoft.Extensions.Logging;
using ConsolidatedStream = MCPsharp.Models.Consolidated;

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
    }

    private StreamValidationResult ValidateStreamRequest(ConsolidatedStream.StreamProcessRequest request)
    {
        // Validate input (a multiple-files request names its inputs in Files instead)
        if (request.InputPath == null && request.InputStream == null && request.InputType != StreamInputType.MultipleFiles)
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

            }
        }

        if (request.InputType == StreamInputType.MultipleFiles && request.Files is not { Count: > 0 })
        {
            return new StreamValidationResult
            {
                IsValid = false,
                ErrorMessage = "File list is required for multiple files input"
            };
        }

        // Several inputs can't share one output file: each result would overwrite the previous one
        if (request.InputType is StreamInputType.Directory or StreamInputType.MultipleFiles
            && !string.IsNullOrEmpty(request.OutputPath)
            && File.Exists(request.OutputPath))
        {
            return new StreamValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Output path must be a directory when processing several files: {request.OutputPath}"
            };
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

    private static void CleanupStreamContext(StreamOperationContext context)
    {
        // Cleanup temporary files, close streams, etc.
        context.Status = context.Status == StreamStatus.Running ? StreamStatus.Completed : context.Status;
    }
}

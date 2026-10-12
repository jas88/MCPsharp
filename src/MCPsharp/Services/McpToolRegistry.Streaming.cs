using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Streaming;

namespace MCPsharp.Services;

/// <summary>
/// Partial class for streaming-related MCP tools
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Process a file using streaming operations with configurable chunk size and progress tracking
    /// </summary>
    /// <param name="arguments">Tool arguments containing filePath, outputPath, processorType, and optional parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with operation details</returns>
    private async Task<ToolCallResult> ExecuteStreamProcessFile(JsonDocument arguments, CancellationToken cancellationToken)
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
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            var outputPath = arguments.RootElement.GetProperty("outputPath").GetString();
            var processorTypeStr = arguments.RootElement.GetProperty("processorType").GetString();

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(outputPath) || string.IsNullOrEmpty(processorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "filePath, outputPath, and processorType are required" };
            }

            if (!Enum.TryParse<StreamProcessorType>(processorTypeStr, true, out var processorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid processor type: {processorTypeStr}" };
            }

            var request = new StreamProcessRequest
            {
                FilePath = filePath,
                OutputPath = outputPath,
                ProcessorType = processorType,
                ChunkSize = arguments.RootElement.TryGetProperty("chunkSize", out var chunkSizeEl) ? chunkSizeEl.GetInt64() : 65536,
                CreateCheckpoint = !arguments.RootElement.TryGetProperty("createCheckpoint", out var checkpointEl) || checkpointEl.GetBoolean(),
                EnableCompression = arguments.RootElement.TryGetProperty("enableCompression", out var compressionEl) && compressionEl.GetBoolean()
            };

            // Parse processor options
            if (arguments.RootElement.TryGetProperty("processorOptions", out var optionsEl))
            {
                request.ProcessorOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsEl.GetRawText()) ?? new Dictionary<string, object>();
            }

            var result = await _streamingProcessor.ProcessFileAsync(request, cancellationToken);

            return new ToolCallResult
            {
                Success = result.Success,
                Result = result.Success ? (object)new
                {
                    operationId = result.OperationId,
                    outputPath = result.OutputPath,
                    bytesProcessed = result.BytesProcessed,
                    chunksProcessed = result.ChunksProcessed,
                    linesProcessed = result.LinesProcessed,
                    processingTime = result.ProcessingTime.TotalSeconds,
                    processingSpeed = result.ProcessingSpeedBytesPerSecond,
                    outputFiles = result.OutputFiles,
                    temporaryFiles = result.TemporaryFiles
                } : null,
                Error = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteStreamProcessFile));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Transform multiple files in bulk with parallel processing
    /// </summary>
    /// <param name="arguments">Tool arguments containing inputFiles, outputDirectory, processorType, and configuration</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with bulk transformation details</returns>
    private async Task<ToolCallResult> ExecuteBulkTransform(JsonDocument arguments, CancellationToken cancellationToken)
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
            var inputFilesElement = arguments.RootElement.GetProperty("inputFiles");
            var outputDirectory = arguments.RootElement.GetProperty("outputDirectory").GetString();
            var processorTypeStr = arguments.RootElement.GetProperty("processorType").GetString();

            if (string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(processorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "outputDirectory and processorType are required" };
            }

            var inputFiles = new List<string>();
            if (inputFilesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var file in inputFilesElement.EnumerateArray())
                {
                    inputFiles.Add(file.GetString() ?? string.Empty);
                }
            }
            else if (inputFilesElement.ValueKind == JsonValueKind.String)
            {
                inputFiles.Add(inputFilesElement.GetString() ?? string.Empty);
            }

            if (!Enum.TryParse<StreamProcessorType>(processorTypeStr, true, out var processorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid processor type: {processorTypeStr}" };
            }

            var request = new BulkTransformRequest
            {
                InputFiles = inputFiles,
                OutputDirectory = outputDirectory,
                ProcessorType = processorType,
                ChunkSize = arguments.RootElement.TryGetProperty("chunkSize", out var chunkSizeEl) ? chunkSizeEl.GetInt64() : 65536,
                MaxDegreeOfParallelism = arguments.RootElement.TryGetProperty("maxDegreeOfParallelism", out var parallelEl) ? parallelEl.GetInt32() : 4,
                PreserveDirectoryStructure = !arguments.RootElement.TryGetProperty("preserveDirectoryStructure", out var preserveEl) || preserveEl.GetBoolean(),
                FilePattern = arguments.RootElement.TryGetProperty("filePattern", out var patternEl) ? patternEl.GetString() : "*",
                Recursive = arguments.RootElement.TryGetProperty("recursive", out var recursiveEl) && recursiveEl.GetBoolean()
            };

            // Parse processor options
            if (arguments.RootElement.TryGetProperty("processorOptions", out var optionsEl))
            {
                request.ProcessorOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsEl.GetRawText()) ?? new Dictionary<string, object>();
            }

            var results = await _streamingProcessor.BulkTransformAsync(request, null, cancellationToken);

            return new ToolCallResult
            {
                Success = results.Count > 0 && results.Any(r => r.Success),
                Result = results.Count > 0 ? (object)new
                {
                    totalFiles = results.Count,
                    processedFiles = results.Count(r => r.Success),
                    failedFiles = results.Count(r => !r.Success),
                    totalBytesProcessed = results.Sum(r => r.BytesProcessed),
                    totalProcessingTime = results.Sum(r => r.ProcessingTime.TotalSeconds),
                    averageProcessingSpeed = results.Average(r => r.ProcessingSpeedBytesPerSecond),
                    outputFiles = results.SelectMany(r => r.OutputFiles).ToList(),
                    errors = results.Where(r => !r.Success).Select(r => r.ErrorMessage).Where(msg => !string.IsNullOrEmpty(msg)).ToList()
                } : null,
                Error = results.Any(r => !r.Success) ? string.Join("; ", results.Where(r => !r.Success).Select(r => r.ErrorMessage).Where(msg => !string.IsNullOrEmpty(msg))) : null
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteBulkTransform));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get list of available stream processors and their capabilities
    /// </summary>
    /// <returns>Tool execution result with processor information</returns>
    private async Task<ToolCallResult> ExecuteGetAvailableProcessors()
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
            var processors = await _streamingProcessor.GetAvailableProcessorsAsync();

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    processors = processors.Select(p => new
                    {
                        name = p.Name,
                        processorType = p.ProcessorType.ToString(),
                        enableParallelProcessing = p.EnableParallelProcessing,
                        maxDegreeOfParallelism = p.MaxDegreeOfParallelism,
                        options = p.Options
                    }).ToArray()
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetAvailableProcessors));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Estimate processing time for a file with given configuration
    /// </summary>
    /// <param name="arguments">Tool arguments containing filePath, processorType, and optional configuration</param>
    /// <returns>Tool execution result with time estimation</returns>
    private async Task<ToolCallResult> ExecuteEstimateStreamProcessing(JsonDocument arguments)
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
            var filePath = arguments.RootElement.GetProperty("filePath").GetString();
            var processorTypeStr = arguments.RootElement.GetProperty("processorType").GetString();

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(processorTypeStr))
            {
                return new ToolCallResult { Success = false, Error = "filePath and processorType are required" };
            }

            if (!Enum.TryParse<StreamProcessorType>(processorTypeStr, true, out var processorType))
            {
                return new ToolCallResult { Success = false, Error = $"Invalid processor type: {processorTypeStr}" };
            }

            var chunkSize = arguments.RootElement.TryGetProperty("chunkSize", out var chunkSizeEl) ? chunkSizeEl.GetInt64() : 65536;

            // Parse processor options
            Dictionary<string, object>? processorOptions = null;
            if (arguments.RootElement.TryGetProperty("processorOptions", out var optionsEl))
            {
                processorOptions = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsEl.GetRawText());
            }

            var request = new StreamProcessRequest
            {
                FilePath = filePath,
                ProcessorType = processorType,
                ChunkSize = chunkSize,
                ProcessorOptions = processorOptions ?? new Dictionary<string, object>()
            };

            var estimation = await _streamingProcessor.EstimateProcessingTimeAsync(request);

            // Get file info for additional context
            var fileInfo = new System.IO.FileInfo(filePath);

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    filePath = filePath,
                    fileSize = fileInfo.Exists ? fileInfo.Length : 0,
                    estimatedProcessingTime = estimation.TotalSeconds,
                    chunkSize = chunkSize,
                    processorType = processorType.ToString(),
                    processorOptions = processorOptions ?? new Dictionary<string, object>()
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteEstimateStreamProcessing));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}

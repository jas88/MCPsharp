using MCPsharp.Models.Consolidated;
using ConsolidatedStream = MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class StreamProcessingController
{
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
            using var outputStream = !string.IsNullOrEmpty(request.OutputPath)
                ? File.Create(request.OutputPath)
                : Stream.Null;

            var processResult = await processor.ProcessAsync(inputStream, outputStream, new ProcessingContext
            {
                FilePath = request.InputPath,
                OperationId = context.Id,
                ChunkSize = request.ChunkSize ?? 65536,
                EnableCompression = request.EnableCompression,
                EnableCheckpoints = request.EnableCheckpoints,
                CancellationToken = ct,
                Options = request.ProcessorOptions
            }, ct);

            result.Success = processResult.Success;
            result.RecordsProcessed = processResult.RecordsProcessed;
            result.BytesProcessed = processResult.BytesProcessed ?? result.BytesProcessed;
            result.Warnings = processResult.Warnings;
            result.Error = processResult.Error;
            result.Message = processResult.Message;
            result.Metrics = processResult.Metrics;
            if (!processResult.Success)
                context.AddLog($"Processing '{request.InputPath}' failed: {processResult.Error}");
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
            if (!File.Exists(file))
                continue;

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

        if (File.Exists(outputPath))
            return outputPath;

        // Treat the output path as a directory and generate a per-input file name in it
        Directory.CreateDirectory(outputPath);
        var fileName = Path.GetFileNameWithoutExtension(inputFile);
        var extension = Path.GetExtension(inputFile);
        return Path.Combine(outputPath, $"{fileName}_processed{extension}");
    }
}

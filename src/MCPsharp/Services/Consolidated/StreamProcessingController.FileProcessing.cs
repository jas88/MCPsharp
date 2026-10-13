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

        var inputRoot = Path.GetFullPath(request.InputPath);
        var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Snapshot the inputs before writing anything so outputs created by this run are never picked up
        var files = Directory.EnumerateFiles(inputRoot, request.SearchPattern ?? "*", searchOption).ToList();
        var outputs = ExcludeGeneratedOutputs(files, request.OutputPath, inputRoot);

        return await ProcessFilesAsync(request, processor, context, outputs, ct);
    }

    private async Task<List<StreamProcessResult>> ProcessMultipleFilesStreamAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        CancellationToken ct)
    {
        var files = (request.Files ?? new List<string>())
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToList();
        var inputRoot = CommonDirectory(files);
        var outputs = files
            .Select(file => (Input: file, Output: GetOutputPath(request.OutputPath, inputRoot, file)))
            .ToList();

        return await ProcessFilesAsync(request, processor, context, outputs, ct);
    }

    private async Task<List<StreamProcessResult>> ProcessFilesAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        List<(string Input, string Output)> files,
        CancellationToken ct)
    {
        var results = new List<StreamProcessResult>();

        foreach (var (file, outputPath) in files)
        {
            if (Path.GetDirectoryName(outputPath) is { Length: > 0 } outputDirectory)
                Directory.CreateDirectory(outputDirectory);

            var fileRequest = new ConsolidatedStream.StreamProcessRequest
            {
                InputType = StreamInputType.File,
                InputPath = file,
                OutputPath = outputPath,
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
                    TotalSteps = files.Count,
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

    /// <summary>
    /// Pairs each enumerated input with its output path, dropping inputs that are outputs of an earlier
    /// run: files inside an output directory nested in the input tree, and files that another input
    /// would be written to (which covers an output directory equal to the input directory).
    /// </summary>
    private static List<(string Input, string Output)> ExcludeGeneratedOutputs(
        List<string> files, string? outputPath, string inputRoot)
    {
        if (string.IsNullOrEmpty(outputPath))
            return files.Select(file => (file, string.Empty)).ToList();

        var outputRoot = Path.GetFullPath(outputPath);
        var outputNestedInInput = IsUnder(outputRoot, inputRoot);
        var candidates = files
            .Where(file => !outputNestedInInput || !IsUnder(file, outputRoot))
            .Select(file => (Input: file, Output: GetOutputPath(outputRoot, inputRoot, file)))
            .ToList();
        var outputs = new HashSet<string>(candidates.Select(c => c.Output), PathComparer);

        return candidates.Where(c => !outputs.Contains(c.Input)).ToList();
    }

    /// <summary>
    /// Maps an input file to its output path. An existing output file is used as-is (request validation
    /// only allows that for a single input); otherwise the output path is a directory and the input's path
    /// relative to <paramref name="inputRoot"/> is preserved beneath it, so inputs with the same name in
    /// different directories do not collide.
    /// </summary>
    private static string GetOutputPath(string? outputPath, string? inputRoot, string inputFile)
    {
        if (string.IsNullOrEmpty(outputPath))
            return string.Empty;

        if (File.Exists(outputPath))
            return outputPath;

        var relativeDirectory = inputRoot != null
            ? Path.GetDirectoryName(Path.GetRelativePath(inputRoot, inputFile)) ?? string.Empty
            : string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(inputFile);
        var extension = Path.GetExtension(inputFile);
        return Path.Combine(Path.GetFullPath(outputPath), relativeDirectory, $"{fileName}_processed{extension}");
    }

    /// <summary>
    /// The deepest directory containing every file, or null when there is none (e.g. different drives)
    /// </summary>
    private static string? CommonDirectory(List<string> files)
    {
        var common = files.Count > 0 ? Path.GetDirectoryName(files[0]) : null;
        foreach (var file in files)
        {
            while (common != null && !IsUnder(file, common))
                common = Path.GetDirectoryName(common);
        }

        return common;
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// True when <paramref name="path"/> is strictly inside <paramref name="directory"/>
    /// </summary>
    private static bool IsUnder(string path, string directory)
    {
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return path.StartsWith(prefix, comparison);
    }
}

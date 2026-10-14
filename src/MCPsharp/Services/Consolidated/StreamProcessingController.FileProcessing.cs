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

    /// <summary>
    /// One input of a directory or multiple-files request: an existing file paired with its output path
    /// (empty when there is no output), or a listed path that does not exist
    /// </summary>
    private sealed record InputEntry(string Input, string Output, bool Exists)
    {
        public static InputEntry Present(string input, string output) => new(input, output, true);

        public static InputEntry Missing(string input) => new(input, string.Empty, false);
    }

    /// <summary>
    /// Inputs resolved for a directory or multiple-files request, in the order they are processed and
    /// reported (for a multiple-files request, the order the caller listed them in)
    /// </summary>
    private sealed record InputSelection(List<InputEntry> Entries)
    {
        public int Count => Entries.Count;

        public IEnumerable<InputEntry> Present => Entries.Where(entry => entry.Exists);
    }

    /// <summary>
    /// Checks the output path before anything is written: it must not be one of the input files (creating
    /// it would truncate that input before it is read), and it must not be an existing file when several
    /// inputs would be written to it. Returns the error, or null when the output path is acceptable.
    /// </summary>
    private static string? ValidateOutputPath(ConsolidatedStream.StreamProcessRequest request, InputSelection? selection)
    {
        if (string.IsNullOrEmpty(request.OutputPath))
            return null;

        var outputPath = Path.GetFullPath(request.OutputPath);
        if (InputFiles(request, selection).Any(input => PathComparer.Equals(input, outputPath)))
            return $"Output path must not be one of the input files: {request.OutputPath}";

        if (selection != null && selection.Present.Skip(1).Any() && File.Exists(request.OutputPath))
            return $"Output path must be a directory when processing several files: {request.OutputPath}";

        return null;
    }

    /// <summary>
    /// The full paths of the existing input files of a request
    /// </summary>
    private static IEnumerable<string> InputFiles(ConsolidatedStream.StreamProcessRequest request, InputSelection? selection)
    {
        if (selection != null)
            return selection.Present.Select(entry => entry.Input);

        return request.InputType == StreamInputType.File && request.InputPath != null
            ? new[] { Path.GetFullPath(request.InputPath) }
            : Enumerable.Empty<string>();
    }

    /// <summary>
    /// Resolves the inputs of a directory or multiple-files request, or null for a single-input request
    /// </summary>
    private static InputSelection? SelectInputs(ConsolidatedStream.StreamProcessRequest request) => request.InputType switch
    {
        StreamInputType.Directory => SelectDirectoryInputs(request),
        StreamInputType.MultipleFiles => SelectListedInputs(request),
        _ => null
    };

    private static InputSelection SelectDirectoryInputs(ConsolidatedStream.StreamProcessRequest request)
    {
        if (request.InputPath == null)
            return new InputSelection(new List<InputEntry>());

        var inputRoot = Path.GetFullPath(request.InputPath);
        var searchOption = request.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        // Snapshot the inputs before writing anything so outputs created by this run are never picked up
        var files = Directory.EnumerateFiles(inputRoot, request.SearchPattern ?? "*", searchOption).ToList();
        var entries = ExcludeGeneratedOutputs(files, request.OutputPath, inputRoot)
            .Select(file => InputEntry.Present(file.Input, file.Output))
            .ToList();
        return new InputSelection(entries);
    }

    private static InputSelection SelectListedInputs(ConsolidatedStream.StreamProcessRequest request)
    {
        var listed = (request.Files ?? new List<string>())
            .Select(path => (Listed: path, FullPath: File.Exists(path) ? Path.GetFullPath(path) : null))
            .ToList();
        var inputRoot = CommonDirectory(listed.Select(item => item.FullPath).OfType<string>().ToList());
        var entries = listed
            .Select(item => item.FullPath != null
                ? InputEntry.Present(item.FullPath, GetOutputPath(request.OutputPath, inputRoot, item.FullPath))
                : InputEntry.Missing(item.Listed))
            .ToList();

        return new InputSelection(entries);
    }

    private async Task<List<StreamProcessResult>> ProcessFilesAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        InputSelection selection,
        CancellationToken ct)
    {
        var results = new List<StreamProcessResult>();

        foreach (var (file, outputPath, exists) in selection.Entries)
        {
            if (exists)
                results.AddRange(await ProcessSelectedFileAsync(request, processor, context, file, outputPath, ct));
            else
                results.Add(MissingInputResult(request, context, file));

            // Update progress
            if (_progressTracker != null && context.ProgressId != null)
            {
                await _progressTracker.UpdateProgressAsync(context.ProgressId, new ProgressInfo
                {
                    Title = "Bulk Processing",
                    TotalSteps = selection.Count,
                    CurrentStep = results.Count,
                    CurrentItem = file,
                    Message = exists ? $"Processing {Path.GetFileName(file)}" : $"Input file not found: {file}"
                }, ct);
            }
        }

        return results;
    }

    private Task<List<StreamProcessResult>> ProcessSelectedFileAsync(
        ConsolidatedStream.StreamProcessRequest request,
        IStreamProcessor processor,
        StreamOperationContext context,
        string file,
        string outputPath,
        CancellationToken ct)
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

        return ProcessFileStreamAsync(fileRequest, processor, context, ct);
    }

    private static StreamProcessResult MissingInputResult(
        ConsolidatedStream.StreamProcessRequest request,
        StreamOperationContext context,
        string path)
    {
        context.AddLog($"Input file not found: '{path}'");
        return new StreamProcessResult
        {
            InputPath = path,
            ProcessorType = request.ProcessorType,
            Success = false,
            Error = $"File not found: {path}"
        };
    }

    private static List<StreamProcessResult> ProcessDataStream()
    {
        // Implementation for processing data streams
        return new List<StreamProcessResult>();
    }

    /// <summary>
    /// Pairs each enumerated input with its output path, dropping inputs that are outputs of an earlier
    /// run: files inside an output directory nested in the input tree, and files that another input
    /// would be written to (which covers an output directory equal to the input directory). An existing
    /// output file is not dropped: the request is rejected if it is one of the inputs.
    /// </summary>
    private static List<(string Input, string Output)> ExcludeGeneratedOutputs(
        List<string> files, string? outputPath, string inputRoot)
    {
        if (string.IsNullOrEmpty(outputPath))
            return files.Select(file => (file, string.Empty)).ToList();

        if (File.Exists(outputPath))
            return files.Select(file => (file, outputPath)).ToList();

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
    /// Maps an input file to its output path. An existing output file is used as-is (the request is
    /// rejected when it is one of the inputs or more than one input would be written to it); otherwise the output path is a directory and the input's path
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

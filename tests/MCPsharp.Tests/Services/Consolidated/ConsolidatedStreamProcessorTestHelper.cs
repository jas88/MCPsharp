using System.Text;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Consolidated;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// Runs consolidated stream processors over in-memory input and captures the output
/// </summary>
internal static class ConsolidatedStreamProcessorTestHelper
{
    public static Task<(ProcessingResult Result, string Output)> RunTextAsync(
        IStreamProcessor processor,
        string input,
        Dictionary<string, object>? parameters = null,
        int chunkSize = 65536,
        ProcessorOptions? options = null)
        => RunTextAsync(processor, Encoding.UTF8.GetBytes(input), parameters, chunkSize, options);

    public static async Task<(ProcessingResult Result, string Output)> RunTextAsync(
        IStreamProcessor processor,
        byte[] input,
        Dictionary<string, object>? parameters = null,
        int chunkSize = 65536,
        ProcessorOptions? options = null)
    {
        var (result, output) = await RunBytesAsync(processor, input, parameters, chunkSize, options);
        return (result, Encoding.UTF8.GetString(output));
    }

    public static async Task<(ProcessingResult Result, byte[] Output)> RunBytesAsync(
        IStreamProcessor processor,
        byte[] input,
        Dictionary<string, object>? parameters = null,
        int chunkSize = 65536,
        ProcessorOptions? options = null)
    {
        options ??= new ProcessorOptions();
        if (parameters != null)
            options.Parameters = parameters;

        await processor.InitializeAsync(options, CancellationToken.None);

        using var inputStream = new MemoryStream(input);
        using var outputStream = new MemoryStream();
        var result = await processor.ProcessAsync(inputStream, outputStream, new ProcessingContext
        {
            FilePath = "memory.txt",
            OperationId = "test",
            ChunkSize = chunkSize,
            EnableCompression = false,
            EnableCheckpoints = false,
            Options = options
        }, CancellationToken.None);

        return (result, outputStream.ToArray());
    }

    /// <summary>
    /// Joins lines the way line-oriented processors write them: each line followed by a newline
    /// </summary>
    public static string Lines(params string[] lines)
        => string.Concat(lines.Select(line => line + Environment.NewLine));
}

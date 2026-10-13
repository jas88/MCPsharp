using System.Text;
using MCPsharp.Models.Consolidated;
using StreamingLineProcessor = MCPsharp.Services.Streaming.LineStreamProcessor;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Line processor: filters and rewrites text lines using <see cref="StreamingLineProcessor"/>.
/// Parameters: LineFilter, LineReplacement, LinePrefix, LineSuffix, RemoveEmptyLines, TrimWhitespace,
/// FieldSeparator, SelectedFields, IncludeLineNumbers (numbers restart in each chunk), Encoding.
/// </summary>
public class LineStreamProcessor : IStreamProcessor
{
    private const string EncodingKey = "Encoding";

    private readonly StreamingLineProcessor _processor = new();

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("line", () =>
        {
            var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);
            var encodingName = StreamProcessorOptionMapper.GetString(options, EncodingKey);
            var encoding = encodingName != null
                ? Encoding.GetEncoding(encodingName)
                : StreamProcessorOptionMapper.ResolveEncoding(context.Options);
            options[EncodingKey] = encoding.WebName;

            return StreamingProcessorAdapter.ProcessLinesAsync(_processor, input, output, context, options, encoding, encoding, ct);
        });

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "txt", "log", "text", "csv" },
            SupportedOutputFormats = new List<string> { "txt", "log", "text" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string>
            {
                "LineFilter", "LineReplacement", "LinePrefix", "LineSuffix", "RemoveEmptyLines",
                "TrimWhitespace", "FieldSeparator", "SelectedFields", "IncludeLineNumbers", EncodingKey
            }
        });
}

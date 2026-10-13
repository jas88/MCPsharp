using System.Text;
using MCPsharp.Models.Consolidated;
using StreamingCsvProcessor = MCPsharp.Services.Streaming.CsvStreamProcessor;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// CSV processor: parses rows and selects/trims columns using <see cref="StreamingCsvProcessor"/>.
/// Parameters: Delimiter, QuoteCharacter, SelectedColumnIndexes, SkipEmptyRows, TrimFields.
/// Rows are read line by line, so quoted fields containing line breaks are not supported.
/// </summary>
public class CsvStreamProcessor : IStreamProcessor
{
    private readonly StreamingCsvProcessor _processor = new();

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("csv", () =>
        {
            var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);
            var inputEncoding = StreamProcessorOptionMapper.ResolveEncoding(context.Options);
            return StreamingProcessorAdapter.ProcessLinesAsync(_processor, input, output, context, options, inputEncoding, Encoding.UTF8, ct);
        });

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "csv", "tsv", "txt" },
            SupportedOutputFormats = new List<string> { "csv", "tsv", "txt" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string> { "Delimiter", "QuoteCharacter", "SelectedColumnIndexes", "SkipEmptyRows", "TrimFields" }
        });
}

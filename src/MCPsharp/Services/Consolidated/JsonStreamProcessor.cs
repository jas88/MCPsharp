using System.Buffers;
using System.Text.Json;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// JSON processor: validates and reformats JSON.
/// By default the whole input is parsed as one JSON document and rewritten (indented unless
/// <c>Indented</c> is false). With <c>JsonLines</c> = true the input is treated as JSON Lines /
/// NDJSON: each non-blank line is validated and rewritten compactly on its own line; invalid lines
/// fail the run unless <see cref="ProcessorOptions.SkipErrors"/> is set (up to
/// <see cref="ProcessorOptions.MaxErrors"/>), in which case they are dropped and reported as warnings.
/// </summary>
public class JsonStreamProcessor : IStreamProcessor
{
    private const string JsonLinesKey = "JsonLines";
    private const string IndentedKey = "Indented";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("json", () =>
        {
            var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);
            return StreamProcessorOptionMapper.GetBool(options, JsonLinesKey, false)
                ? ProcessJsonLinesAsync(input, output, context, ct)
                : ProcessDocumentAsync(input, output, context, StreamProcessorOptionMapper.GetBool(options, IndentedKey, true), ct);
        });

    private static async Task<ProcessingResult> ProcessDocumentAsync(Stream input, Stream output, ProcessingContext context, bool indented, CancellationToken ct)
    {
        var text = await TextStreamEncoding.DetectAsync(input, StreamProcessorOptionMapper.ResolveEncoding(context.Options), ct).ConfigureAwait(false);
        JsonDocument document;
        try
        {
            document = await ParseDocumentAsync(text, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return StreamProcessorExecution.Failure($"Invalid JSON: {ex.Message}");
        }

        using (document)
        {
            await text.WriteByteOrderMarkAsync(output, ct).ConfigureAwait(false);

            // Utf8JsonWriter only writes UTF-8: buffer the output to transcode it unless the input is UTF-8
            using var buffer = text.IsUtf8 ? null : new MemoryStream();
            await using var writer = new Utf8JsonWriter(buffer ?? output, new JsonWriterOptions { Indented = indented });
            document.WriteTo(writer);
            await writer.FlushAsync(ct).ConfigureAwait(false);
            if (buffer != null)
                await text.WriteUtf8Async(output, buffer.GetBuffer().AsMemory(0, (int)buffer.Length), ct).ConfigureAwait(false);

            var root = document.RootElement;
            var records = root.ValueKind == JsonValueKind.Array ? root.GetArrayLength() : 1;
            return new ProcessingResult
            {
                Success = true,
                RecordsProcessed = records,
                BytesProcessed = input.CanSeek ? input.Length : writer.BytesCommitted,
                Message = $"Validated JSON document ({root.ValueKind}, {records} record(s))",
                Metrics = new Dictionary<string, object>
                {
                    ["RootKind"] = root.ValueKind.ToString(),
                    ["BytesWritten"] = writer.BytesCommitted
                }
            };
        }
    }

    private static async Task<JsonDocument> ParseDocumentAsync(TextStreamEncoding text, CancellationToken ct)
    {
        if (text.IsUtf8)
            return await JsonDocument.ParseAsync(text.Content, DocumentOptions, ct).ConfigureAwait(false);

        using var reader = text.CreateReader();
        return JsonDocument.Parse(await reader.ReadToEndAsync(ct).ConfigureAwait(false), DocumentOptions);
    }

    private static async Task<ProcessingResult> ProcessJsonLinesAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
    {
        var skipErrors = context.Options?.SkipErrors ?? false;
        var maxErrors = context.Options?.MaxErrors ?? 0;
        var text = await TextStreamEncoding.DetectAsync(input, StreamProcessorOptionMapper.ResolveEncoding(context.Options), ct).ConfigureAwait(false);
        var newLine = "\n"u8.ToArray();
        var buffer = new ArrayBufferWriter<byte>();
        var warnings = new List<ProcessingWarning>();
        long lineNumber = 0;
        long records = 0;

        await text.WriteByteOrderMarkAsync(output, ct).ConfigureAwait(false);
        using var reader = text.CreateReader();
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line, DocumentOptions);
            }
            catch (JsonException ex)
            {
                if (!skipErrors)
                    return StreamProcessorExecution.Failure($"Invalid JSON on line {lineNumber}: {ex.Message}");

                warnings.Add(new ProcessingWarning
                {
                    Code = "InvalidJson",
                    Message = ex.Message,
                    FilePath = context.FilePath,
                    LineNumber = (int)Math.Min(lineNumber, int.MaxValue)
                });

                if (warnings.Count > maxErrors)
                    return StreamProcessorExecution.Failure($"Too many invalid JSON lines ({warnings.Count}); last error on line {lineNumber}: {ex.Message}");
                continue;
            }

            using (document)
            {
                buffer.ResetWrittenCount();
                await using (var writer = new Utf8JsonWriter(buffer))
                {
                    document.WriteTo(writer);
                    await writer.FlushAsync(ct).ConfigureAwait(false);
                }

                buffer.Write(newLine);
                await text.WriteUtf8Async(output, buffer.WrittenMemory, ct).ConfigureAwait(false);
                records++;
            }
        }

        await output.FlushAsync(ct).ConfigureAwait(false);

        return new ProcessingResult
        {
            Success = true,
            RecordsProcessed = records,
            BytesProcessed = input.CanSeek ? input.Length : null,
            Message = $"Validated {records} JSON line(s), skipped {warnings.Count} invalid line(s)",
            Warnings = warnings.Count > 0 ? warnings : null,
            Metrics = new Dictionary<string, object>
            {
                ["LinesRead"] = lineNumber,
                ["InvalidLines"] = warnings.Count
            }
        };
    }

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "json", "jsonl", "ndjson" },
            SupportedOutputFormats = new List<string> { "json", "jsonl", "ndjson" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string> { JsonLinesKey, IndentedKey }
        });
}

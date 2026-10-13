using System.Text;
using System.Xml;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// XML processor: validates well-formedness and rewrites the document node by node (streaming),
/// indented unless <c>Indent</c> is false. DTDs are rejected and external resources are never resolved.
/// </summary>
public class XmlStreamProcessor : IStreamProcessor
{
    private const string IndentKey = "Indent";

    public Task InitializeAsync(ProcessorOptions options, CancellationToken ct) => Task.CompletedTask;

    public Task<ProcessingResult> ProcessAsync(Stream input, Stream output, ProcessingContext context, CancellationToken ct)
        => StreamProcessorExecution.RunAsync("xml", async () =>
        {
            var options = StreamProcessorOptionMapper.ToOptionDictionary(context.Options);
            var indent = StreamProcessorOptionMapper.GetBool(options, IndentKey, true);
            try
            {
                return await CopyAsync(input, output, indent, ct).ConfigureAwait(false);
            }
            catch (XmlException ex)
            {
                return StreamProcessorExecution.Failure($"Invalid XML: {ex.Message}");
            }
        });

    private static async Task<ProcessingResult> CopyAsync(Stream input, Stream output, bool indent, CancellationToken ct)
    {
        var readerSettings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = indent,
            CloseInput = false
        };
        var writerSettings = new XmlWriterSettings
        {
            Async = true,
            Indent = indent,
            // Keep the document's own (reader-normalized) line breaks in text as-is, but write any character the
            // next parse would normalize away as a reference: a lone CR in text and line breaks in attributes.
            // Replace would turn a CR from &#xD; into a line break and None would write it raw, losing it either way.
            NewLineHandling = NewLineHandling.Entitize,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            CloseOutput = false
        };

        long elements = 0;
        using var reader = XmlReader.Create(input, readerSettings);
        await using (var writer = XmlWriter.Create(output, writerSettings))
        {
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element)
                    elements++;
                await CopyNodeAsync(reader, writer).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }

        return new ProcessingResult
        {
            Success = true,
            RecordsProcessed = elements,
            BytesProcessed = input.CanSeek ? input.Length : null,
            Message = $"Validated XML document with {elements} element(s)",
            Metrics = new Dictionary<string, object> { ["ElementCount"] = elements }
        };
    }

    private static async Task CopyNodeAsync(XmlReader reader, XmlWriter writer)
    {
        switch (reader.NodeType)
        {
            case XmlNodeType.Element:
                var isEmpty = reader.IsEmptyElement;
                await writer.WriteStartElementAsync(reader.Prefix, reader.LocalName, reader.NamespaceURI).ConfigureAwait(false);
                await writer.WriteAttributesAsync(reader, true).ConfigureAwait(false);
                if (isEmpty)
                    await writer.WriteEndElementAsync().ConfigureAwait(false);
                break;
            case XmlNodeType.EndElement:
                await writer.WriteFullEndElementAsync().ConfigureAwait(false);
                break;
            case XmlNodeType.Text:
                await writer.WriteStringAsync(reader.Value).ConfigureAwait(false);
                break;
            case XmlNodeType.CDATA:
                await writer.WriteCDataAsync(reader.Value).ConfigureAwait(false);
                break;
            case XmlNodeType.Comment:
                await writer.WriteCommentAsync(reader.Value).ConfigureAwait(false);
                break;
            case XmlNodeType.ProcessingInstruction:
                await writer.WriteProcessingInstructionAsync(reader.Name, reader.Value).ConfigureAwait(false);
                break;
            case XmlNodeType.Whitespace:
            case XmlNodeType.SignificantWhitespace:
                await writer.WriteWhitespaceAsync(reader.Value).ConfigureAwait(false);
                break;
            case XmlNodeType.EntityReference:
                await writer.WriteEntityRefAsync(reader.Name).ConfigureAwait(false);
                break;
            default:
                // XmlDeclaration is emitted by the writer itself; DTDs are prohibited
                break;
        }
    }

    public Task<ProcessorCapabilities> GetCapabilitiesAsync(CancellationToken ct)
        => Task.FromResult(new ProcessorCapabilities
        {
            SupportedInputFormats = new List<string> { "xml", "xhtml", "svg", "config" },
            SupportedOutputFormats = new List<string> { "xml", "xhtml", "svg", "config" },
            SupportsStreaming = true,
            SupportsParallelProcessing = false,
            SupportsCheckpoints = false,
            SupportsCompression = false,
            MaxFileSize = long.MaxValue,
            OptionalParameters = new List<string> { IndentKey }
        });
}

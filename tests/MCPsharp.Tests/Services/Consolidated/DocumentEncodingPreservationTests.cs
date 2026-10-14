using System.Text;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;
using static MCPsharp.Tests.Services.Consolidated.ConsolidatedStreamProcessorTestHelper;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// The json and log processors write their output in the input's encoding, with a byte order mark only
/// when the input had one
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class DocumentEncodingPreservationTests
{
    private static readonly Encoding Utf16LeWithBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static byte[] Encode(Encoding encoding, string text)
        => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    [Test]
    public async Task Json_Document_Utf16LeWithBom_IsWrittenAsUtf16LeWithBom()
    {
        var input = Encode(Utf16LeWithBom, "{ \"name\": \"café\" }");

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input,
            new Dictionary<string, object> { ["Indented"] = false });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf16LeWithBom, "{\"name\":\"caf\\u00E9\"}")));
    }

    [Test]
    public async Task Json_Document_Latin1EncodingOption_IsDecodedAndWrittenAsLatin1()
    {
        var input = Encoding.Latin1.GetBytes("[\"café\"]");

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input,
            new Dictionary<string, object> { ["Indented"] = false },
            options: new ProcessorOptions { Encoding = "iso-8859-1" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encoding.Latin1.GetBytes("[\"caf\\u00E9\"]")));
    }

    [Test]
    public async Task Json_Document_Utf8WithBom_KeepsBom()
    {
        var input = Encode(Utf8WithBom, "{ \"a\": 1 }");

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input,
            new Dictionary<string, object> { ["Indented"] = false });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf8WithBom, "{\"a\":1}")));
    }

    [Test]
    public async Task Json_Document_Utf8WithoutBom_OutputHasNoBom()
    {
        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), Encoding.ASCII.GetBytes("{ \"a\": 1 }"),
            new Dictionary<string, object> { ["Indented"] = false });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encoding.ASCII.GetBytes("{\"a\":1}")));
    }

    [Test]
    public async Task JsonLines_Utf16LeWithBom_IsWrittenAsUtf16LeWithBom()
    {
        var input = Encode(Utf16LeWithBom, "{ \"a\": 1 }\n[1, 2]\n");

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input,
            new Dictionary<string, object> { ["JsonLines"] = true });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf16LeWithBom, "{\"a\":1}\n[1,2]\n")));
    }

    [Test]
    public async Task Log_Utf16LeWithBom_IsWrittenAsUtf16LeWithBom()
    {
        var input = Encode(Utf16LeWithBom, "INFO café\nERROR thé\n");

        var (result, output) = await RunBytesAsync(new LogStreamProcessor(), input,
            new Dictionary<string, object> { ["MinLevel"] = "error" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf16LeWithBom, "ERROR thé\n")));
    }

    [Test]
    public async Task Log_Utf16EncodingOptionWithoutBom_OutputHasNoBom()
    {
        var utf16NoBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        var input = utf16NoBom.GetBytes("WARN café\n");

        var (result, output) = await RunBytesAsync(new LogStreamProcessor(), input,
            options: new ProcessorOptions { Encoding = "utf-16" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
    }

    [Test]
    public async Task Log_Latin1EncodingOption_PreservesSingleByteEncoding()
    {
        var input = Encoding.Latin1.GetBytes("ERROR café\n");

        var (result, output) = await RunBytesAsync(new LogStreamProcessor(), input,
            options: new ProcessorOptions { Encoding = "iso-8859-1" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
    }

    [Test]
    public async Task Json_Document_Utf16LeWithBom_BytesWrittenIsOutputLength()
    {
        var input = Encode(Utf16LeWithBom, "{ \"name\": \"café\" }");

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Metrics?["BytesWritten"], Is.EqualTo((long)output.Length));
    }

    [Test]
    public async Task Json_Document_NonSeekableUtf16Input_BytesProcessedFallbackIsOutputLength()
    {
        var input = Encode(Utf16LeWithBom, "[1, 2, 3]");
        using var inputStream = new NonSeekableStream(input);
        using var outputStream = new MemoryStream();
        var options = new ProcessorOptions();

        var result = await new JsonStreamProcessor().ProcessAsync(inputStream, outputStream, new ProcessingContext
        {
            FilePath = "memory.json",
            OperationId = "test",
            ChunkSize = 65536,
            EnableCompression = false,
            EnableCheckpoints = false,
            Options = options
        }, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.BytesProcessed, Is.EqualTo(outputStream.Length));
        Assert.That(result.Metrics?["BytesWritten"], Is.EqualTo(outputStream.Length));
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}

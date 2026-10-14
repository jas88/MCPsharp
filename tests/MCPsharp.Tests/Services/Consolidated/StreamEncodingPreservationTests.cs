using System.Text;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;
using static MCPsharp.Tests.Services.Consolidated.ConsolidatedStreamProcessorTestHelper;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// Text processors write their output in the input's encoding, with a byte order mark only when the input had one
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class StreamEncodingPreservationTests
{
    private static readonly Encoding Utf16LeWithBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static byte[] Encode(Encoding encoding, string text)
        => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();

    [Test]
    public async Task Regex_Utf16LeWithBom_NoMatch_LeavesBytesUnchanged()
    {
        var input = Encode(Utf16LeWithBom, Lines("café one", "two €"));

        var (result, output) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "no-such-text", ["Replacement"] = "x" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
    }

    [Test]
    public async Task Regex_Utf16LeWithBom_Replacement_IsWrittenAsUtf16LeWithBom()
    {
        var input = Encode(Utf16LeWithBom, Lines("café one", "two €"));

        var (result, output) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "one", ["Replacement"] = "über" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf16LeWithBom, Lines("café über", "two €"))));
    }

    [Test]
    public async Task Regex_Latin1EncodingOption_PreservesSingleByteEncoding()
    {
        var latin1 = Encoding.Latin1;
        var input = latin1.GetBytes(Lines("café one", "naïve"));

        var (result, output) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "one", ["Replacement"] = "deux" },
            options: new ProcessorOptions { Encoding = "iso-8859-1" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(latin1.GetBytes(Lines("café deux", "naïve"))));
    }

    [Test]
    public async Task Regex_Utf8WithoutBom_OutputHasNoBom()
    {
        var input = Utf8NoBom.GetBytes(Lines("café one"));

        var (result, output) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "one", ["Replacement"] = "two" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Utf8NoBom.GetBytes(Lines("café two"))));
    }

    [Test]
    public async Task Regex_Utf8WithBom_KeepsBom()
    {
        var input = Encode(Utf8WithBom, Lines("café one"));

        var (result, output) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "one", ["Replacement"] = "two" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf8WithBom, Lines("café two"))));
    }

    [Test]
    public async Task Regex_Utf16LeWithBom_SmallChunks_WritesBomOnce()
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"réponse {i}").ToArray();
        var input = Encode(Utf16LeWithBom, Lines(lines));

        var (result, output) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "nomatch" }, chunkSize: 16);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That((long)result.Metrics!["ChunksProcessed"], Is.GreaterThan(1));
        Assert.That(output, Is.EqualTo(input));
    }

    [Test]
    public async Task Line_Utf16LeWithBom_IsWrittenAsUtf16LeWithBom()
    {
        var input = Encode(Utf16LeWithBom, Lines("café", "thé"));

        var (result, output) = await RunBytesAsync(new LineStreamProcessor(), input,
            new Dictionary<string, object> { ["LinePrefix"] = "» " });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf16LeWithBom, Lines("» café", "» thé"))));
    }

    [Test]
    public async Task Line_Latin1EncodingParameter_PreservesSingleByteEncoding()
    {
        var input = Encoding.Latin1.GetBytes(Lines("café"));

        var (result, output) = await RunBytesAsync(new LineStreamProcessor(), input,
            new Dictionary<string, object> { ["Encoding"] = "iso-8859-1", ["LineSuffix"] = " à" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encoding.Latin1.GetBytes(Lines("café à"))));
    }

    [Test]
    public async Task Line_Utf16EncodingWithoutBom_OutputHasNoBom()
    {
        var utf16NoBom = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        var input = utf16NoBom.GetBytes(Lines("café"));

        var (result, output) = await RunBytesAsync(new LineStreamProcessor(), input,
            new Dictionary<string, object> { ["Encoding"] = "utf-16" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
    }

    [Test]
    public async Task Csv_Utf16LeWithBom_IsWrittenAsUtf16LeWithBom()
    {
        var input = Encode(Utf16LeWithBom, Lines("naïve,1,x", "café,2,y"));

        var (result, output) = await RunBytesAsync(new CsvStreamProcessor(), input,
            new Dictionary<string, object> { ["SelectedColumnIndexes"] = new List<object> { 0, 2 } });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Encode(Utf16LeWithBom, Lines("naïve,x", "café,y"))));
    }

    [Test]
    public async Task Csv_Latin1EncodingOption_PreservesSingleByteEncoding()
    {
        var input = Encoding.Latin1.GetBytes(Lines("café,1"));

        var (result, output) = await RunBytesAsync(new CsvStreamProcessor(), input,
            options: new ProcessorOptions { Encoding = "iso-8859-1" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
    }

    [Test]
    public async Task Regex_ReplacementNotRepresentableInEncoding_ReportsFailure()
    {
        var input = Encoding.Latin1.GetBytes(Lines("price"));

        var (result, _) = await RunBytesAsync(new RegexStreamProcessor(), input,
            new Dictionary<string, object> { ["Pattern"] = "price", ["Replacement"] = "€" },
            options: new ProcessorOptions { Encoding = "iso-8859-1" });

        Assert.That(result.Success, Is.False);
    }
}

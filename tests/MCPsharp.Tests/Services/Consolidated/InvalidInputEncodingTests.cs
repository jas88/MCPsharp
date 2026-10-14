using System.Text;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;
using static MCPsharp.Tests.Services.Consolidated.ConsolidatedStreamProcessorTestHelper;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// Text processors fail on input that is not valid in its encoding instead of replacing the malformed
/// bytes with U+FFFD and writing the "repaired" text
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class InvalidInputEncodingTests
{
    // 0xC3 starts a two-byte UTF-8 sequence but 0x28 ('(') is not a continuation byte
    private static readonly byte[] InvalidUtf8 = { 0xC3, 0x28 };
    private static readonly byte[] Utf8ReplacementCharacter = { 0xEF, 0xBF, 0xBD };
    private static readonly byte[] Utf16LeReplacementCharacter = { 0xFD, 0xFF };

    private static byte[] Utf8WithInvalidBytes(string before, string after)
        => Encoding.UTF8.GetBytes(before).Concat(InvalidUtf8).Concat(Encoding.UTF8.GetBytes(after)).ToArray();

    private static IStreamProcessor CreateProcessor(string name) => name switch
    {
        "line" => new LineStreamProcessor(),
        "regex" => new RegexStreamProcessor(),
        "csv" => new CsvStreamProcessor(),
        "json" => new JsonStreamProcessor(),
        "log" => new LogStreamProcessor(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
    };

    private static Dictionary<string, object> ParametersFor(string name) => name switch
    {
        "regex" => new Dictionary<string, object> { ["Pattern"] = "x", ["Replacement"] = "y" },
        "json" => new Dictionary<string, object> { ["JsonLines"] = true },
        _ => new Dictionary<string, object>()
    };

    [TestCase("line", "first\n", "\nlast\n")]
    [TestCase("regex", "first\n", "\nlast\n")]
    [TestCase("csv", "a,b\n1,", "\n")]
    [TestCase("json", "\"first\"\n\"", "\"\n")]
    [TestCase("log", "INFO first\nINFO ", "\n")]
    public async Task InvalidUtf8_FailsWithoutWritingReplacementCharacters(string processor, string before, string after)
    {
        var input = Utf8WithInvalidBytes(before, after);

        var (result, output) = await RunBytesAsync(CreateProcessor(processor), input, ParametersFor(processor));

        Assert.That(result.Success, Is.False, "Malformed input must fail the run");
        Assert.That(result.Error, Does.Contain("Input is not valid utf-8"));
        Assert.That(Contains(output, Utf8ReplacementCharacter), Is.False, "Malformed bytes must not be replaced");
    }

    [Test]
    public async Task JsonDocument_InvalidUtf8_Fails()
    {
        var input = Utf8WithInvalidBytes("[\"", "\"]");

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input);

        Assert.That(result.Success, Is.False, "Malformed input must fail the run");
        Assert.That(result.Error, Does.Contain("Input is not valid utf-8"));
        Assert.That(output, Is.Empty);
    }

    [Test]
    public async Task JsonDocument_LoneSurrogateInUtf16_Fails()
    {
        // UTF-16LE with BOM: [" <high surrogate D800 with no low surrogate> "]
        var encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        var input = new byte[] { 0xFF, 0xFE }
            .Concat(encoding.GetBytes("[\""))
            .Concat(new byte[] { 0x00, 0xD8 })
            .Concat(encoding.GetBytes("\"]"))
            .ToArray();

        var (result, output) = await RunBytesAsync(new JsonStreamProcessor(), input);

        Assert.That(result.Success, Is.False, "Malformed input must fail the run");
        Assert.That(result.Error, Does.Contain("Input is not valid utf-16"));
        Assert.That(Contains(output, Utf16LeReplacementCharacter), Is.False, "Malformed bytes must not be replaced");
    }

    private static bool Contains(byte[] data, byte[] sequence) => data.AsSpan().IndexOf(sequence) >= 0;
}

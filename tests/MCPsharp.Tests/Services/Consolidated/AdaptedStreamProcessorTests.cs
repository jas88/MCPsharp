using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;
using static MCPsharp.Tests.Services.Consolidated.ConsolidatedStreamProcessorTestHelper;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// Consolidated line/regex/csv/binary processors, which delegate to the MCPsharp.Services.Streaming processors
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class AdaptedStreamProcessorTests
{
    [Test]
    public async Task Line_FilterAndPrefix_WritesOnlyMatchingLines()
    {
        var (result, output) = await RunTextAsync(new LineStreamProcessor(),
            "alpha\nERROR one\nbeta\r\nERROR two\n",
            new Dictionary<string, object> { ["LineFilter"] = "^ERROR", ["LinePrefix"] = "> " });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("> ERROR one", "> ERROR two")));
        Assert.That(result.RecordsProcessed, Is.EqualTo(4));
    }

    [Test]
    public async Task Line_SmallChunks_NeverSplitLines()
    {
        var lines = Enumerable.Range(1, 50).Select(i => $"line number {i}").ToArray();

        var (result, output) = await RunTextAsync(new LineStreamProcessor(), string.Join("\n", lines),
            new Dictionary<string, object> { ["LineSuffix"] = ";" }, chunkSize: 40);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines(lines.Select(l => l + ";").ToArray())));
        Assert.That(result.RecordsProcessed, Is.EqualTo(50));
        Assert.That((long)result.Metrics!["ChunksProcessed"], Is.GreaterThan(1));
    }

    [Test]
    public async Task Line_IncludeLineNumbers_ContinueAcrossChunks()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"row {i}").ToArray();

        var (result, output) = await RunTextAsync(new LineStreamProcessor(), string.Join("\n", lines),
            new Dictionary<string, object> { ["IncludeLineNumbers"] = true }, chunkSize: 16);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That((long)result.Metrics!["ChunksProcessed"], Is.GreaterThan(1));
        Assert.That(output, Is.EqualTo(Lines(lines.Select((l, i) => $"{i + 1}: {l}").ToArray())));
    }

    [TestCase(65536)]
    [TestCase(1)]
    public async Task Line_EmptyLines_ArePreservedByDefault(int chunkSize)
    {
        var (result, output) = await RunTextAsync(new LineStreamProcessor(), "a\n\nb\n\n\nc\n",
            new Dictionary<string, object>(), chunkSize: chunkSize);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("a", "", "b", "", "", "c")));
    }

    [Test]
    public async Task Line_RemoveEmptyLines_DropsThem()
    {
        var (result, output) = await RunTextAsync(new LineStreamProcessor(), "a\n\nb\n  \nc\n",
            new Dictionary<string, object> { ["RemoveEmptyLines"] = true });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("a", "b", "c")));
    }

    [Test]
    public async Task Line_InvalidFilterRegex_ReportsFailure()
    {
        var (result, _) = await RunTextAsync(new LineStreamProcessor(), "text\n",
            new Dictionary<string, object> { ["LineFilter"] = "(" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("line"));
    }

    [Test]
    public async Task Regex_ReplacementAcrossChunks_AppliesToEveryLine()
    {
        var (result, output) = await RunTextAsync(new RegexStreamProcessor(),
            "id 1\nid 22\nid 333\nnone\n",
            new Dictionary<string, object> { ["Pattern"] = @"(\d+)", ["Replacement"] = "<$1>" }, chunkSize: 8);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("id <1>", "id <22>", "id <333>", "none")));
    }

    [Test]
    public async Task Regex_OptionsFromJsonArguments_AreMappedCaseInsensitively()
    {
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{ "pattern": "cat", "replacement": "dog", "ignoreCase": true }""")!;

        var (result, output) = await RunTextAsync(new RegexStreamProcessor(), "Cat and cat\n", parameters);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("dog and dog")));
    }

    [Test]
    public async Task Regex_MaxMatches_LimitsReplacements()
    {
        var (result, output) = await RunTextAsync(new RegexStreamProcessor(), "a a a\n",
            new Dictionary<string, object> { ["Pattern"] = "a", ["Replacement"] = "b", ["MaxMatches"] = 2 });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("b b a")));
    }

    [Test]
    public async Task Regex_InvalidPattern_ReportsFailure()
    {
        var (result, output) = await RunTextAsync(new RegexStreamProcessor(), "text\n",
            new Dictionary<string, object> { ["Pattern"] = "([a-z" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("regex"));
        Assert.That(output, Is.Empty);
    }

    [Test]
    public async Task Csv_SelectAndTrimColumns_RewritesRows()
    {
        var (result, output) = await RunTextAsync(new CsvStreamProcessor(),
            "name, \"city, region\" ,age\nann, \"Leeds, UK\" ,31\n\nbob,Paris,42\n",
            new Dictionary<string, object>
            {
                ["SelectedColumnIndexes"] = new List<object> { 2, 0 },
                ["TrimFields"] = true,
                ["SkipEmptyRows"] = true
            });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("age,name", "31,ann", "42,bob")));
    }

    [Test]
    public async Task Csv_ColumnIndexesFromJson_AreHonoured()
    {
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(
            """{ "selectedColumnIndexes": [1], "delimiter": ";" }""")!;

        var (result, output) = await RunTextAsync(new CsvStreamProcessor(), "a;b;c\n1;2;3\n", parameters);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(Lines("b", "2")));
    }

    [Test]
    public async Task Binary_NoOptions_CopiesInputExactly()
    {
        var input = new byte[10_000];
        new Random(42).NextBytes(input);

        var (result, output) = await RunBytesAsync(new BinaryStreamProcessor(), input, chunkSize: 1024);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
        Assert.That(result.BytesProcessed, Is.EqualTo(input.Length));
        Assert.That(result.RecordsProcessed, Is.EqualTo(10));
    }

    [Test]
    public async Task Binary_SearchReplace_HandlesMatchesStraddlingChunks()
    {
        var (result, output) = await RunBytesAsync(new BinaryStreamProcessor(),
            Encoding.ASCII.GetBytes("abcabcxxabcab"),
            new Dictionary<string, object> { ["SearchPattern"] = "abc", ["ReplacementPattern"] = "WXYZ" },
            chunkSize: 4);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(Encoding.ASCII.GetString(output), Is.EqualTo("WXYZWXYZxxWXYZab"));
    }

    [Test]
    public async Task Binary_Checksums_AreReportedPerChunk()
    {
        var input = Encoding.ASCII.GetBytes("0123456789");

        var (result, output) = await RunBytesAsync(new BinaryStreamProcessor(), input,
            new Dictionary<string, object> { ["CalculateChecksums"] = true, ["ChecksumAlgorithm"] = "SHA256" },
            chunkSize: 5);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(input));
        var checksums = (List<string>)result.Metrics!["ChunkChecksums"];
        Assert.That(checksums, Is.EqualTo(new[]
        {
            Convert.ToHexString(SHA256.HashData(input.AsSpan(0, 5))).ToLowerInvariant(),
            Convert.ToHexString(SHA256.HashData(input.AsSpan(5, 5))).ToLowerInvariant()
        }));
    }

    [Test]
    public async Task Binary_UnsupportedChecksumAlgorithm_ReportsFailure()
    {
        var (result, _) = await RunBytesAsync(new BinaryStreamProcessor(), new byte[] { 1, 2, 3 },
            new Dictionary<string, object> { ["CalculateChecksums"] = true, ["ChecksumAlgorithm"] = "MD5" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Invalid processor options"));
    }

    [TestCase("xxabc", 5)]
    [TestCase("xxab", 2)]
    [TestCase("abcab", 3)]
    [TestCase("abcabc", 6)]
    [TestCase("xyzzy", 3)]
    public void FindSafeCut_StopsBeforePossiblePartialMatch(string data, int expected)
    {
        var cut = StreamingProcessorAdapter.FindSafeCut(Encoding.ASCII.GetBytes(data), Encoding.ASCII.GetBytes("abc"));

        Assert.That(cut, Is.EqualTo(expected));
    }
}

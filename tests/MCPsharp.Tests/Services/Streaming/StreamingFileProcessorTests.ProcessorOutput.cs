using MCPsharp.Models.Streaming;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

/// <summary>
/// End-to-end checks of the output written by <see cref="MCPsharp.Services.StreamingFileProcessor"/>
/// for the built-in chunk processors.
/// </summary>
public partial class StreamingFileProcessorTests
{
    private async Task<string> ProcessToTextAsync(string content, StreamProcessorType processorType,
        Dictionary<string, object> options, long chunkSize = 1024)
    {
        var inputFile = CreateTestFile(string.Empty, ".txt");
        await File.WriteAllTextAsync(inputFile, content); // UTF-8 without a byte order mark
        var request = new StreamProcessRequest
        {
            FilePath = inputFile,
            OutputPath = inputFile + ".out",
            ProcessorType = processorType,
            ChunkSize = chunkSize,
            CreateCheckpoint = false,
            ProcessorOptions = options
        };

        var result = await _processor.ProcessFileAsync(request);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        return await File.ReadAllTextAsync(request.OutputPath);
    }

    [Test]
    public async Task ProcessFileAsync_RegexReplacementWithDefaultMaxMatches_ReplacesEveryMatch()
    {
        var output = await ProcessToTextAsync("foo bar foo\nfoo\n", StreamProcessorType.RegexProcessor,
            new Dictionary<string, object> { ["Pattern"] = "foo", ["Replacement"] = "baz" });

        Assert.That(output, Is.EqualTo("baz bar baz\nbaz\n"));
    }

    [TestCase(0, "x x x")]
    [TestCase(-5, "x x x")]
    [TestCase(2, "x x a")]
    public async Task ProcessFileAsync_RegexMaxMatches_ZeroOrNegativeMeansUnlimited(int maxMatches, string expected)
    {
        var output = await ProcessToTextAsync("a a a", StreamProcessorType.RegexProcessor,
            new Dictionary<string, object> { ["Pattern"] = "a", ["Replacement"] = "x", ["MaxMatches"] = maxMatches });

        Assert.That(output, Is.EqualTo(expected));
    }

    [TestCase(8)]
    [TestCase(1024)]
    public async Task ProcessFileAsync_LineNumbers_ContinueAcrossChunksAndKeepEmptyLines(int chunkSize)
    {
        var options = new Dictionary<string, object> { ["IncludeLineNumbers"] = true };

        var output = await ProcessToTextAsync("alpha\nbeta\n\ngamma delta epsilon\r\nzeta\n",
            StreamProcessorType.LineProcessor, options, chunkSize);

        var nl = Environment.NewLine;
        Assert.That(output, Is.EqualTo($"1: alpha{nl}2: beta{nl}3: {nl}4: gamma delta epsilon{nl}5: zeta{nl}"));
    }

    [Test]
    public async Task ProcessFileAsync_LineNumbers_RestartForEachRunOfTheSharedProcessor()
    {
        var options = new Dictionary<string, object> { ["IncludeLineNumbers"] = true };

        var first = await ProcessToTextAsync("a\nb\nc\n", StreamProcessorType.LineProcessor, options, 4);
        var second = await ProcessToTextAsync("x\ny\n", StreamProcessorType.LineProcessor, options, 4);

        var nl = Environment.NewLine;
        Assert.That(first, Is.EqualTo($"1: a{nl}2: b{nl}3: c{nl}"));
        Assert.That(second, Is.EqualTo($"1: x{nl}2: y{nl}"));
    }

    [Test]
    public async Task ProcessFileAsync_LineFilterWithSmallChunks_NeverSplitsLines()
    {
        var lines = Enumerable.Range(1, 40).Select(i => i % 3 == 0 ? $"keep {i} ünïcödé" : $"drop {i}").ToArray();
        var options = new Dictionary<string, object> { ["LineFilter"] = "^keep" };

        var output = await ProcessToTextAsync(string.Join("\n", lines) + "\n", StreamProcessorType.LineProcessor, options, 7);

        var expected = string.Concat(lines.Where(l => l.StartsWith("keep", StringComparison.Ordinal)).Select(l => l + Environment.NewLine));
        Assert.That(output, Is.EqualTo(expected));
    }
}

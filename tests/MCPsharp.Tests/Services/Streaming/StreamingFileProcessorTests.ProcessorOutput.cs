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
        var inputFile = CreateTestFile(content, ".txt");
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
}

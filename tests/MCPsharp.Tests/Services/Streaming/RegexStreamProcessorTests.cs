using System.Text;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class RegexStreamProcessorTests
{
    private static async Task<string> ProcessAsync(string text, Dictionary<string, object> options)
    {
        var processor = new RegexStreamProcessor(NullLogger<RegexStreamProcessor>.Instance);
        var chunk = new StreamChunk { Data = Encoding.UTF8.GetBytes(text), IsLastChunk = true };

        var result = await processor.ProcessChunkAsync(chunk, options);

        return Encoding.UTF8.GetString(result.Data);
    }

    [Test]
    public async Task ProcessChunkAsync_EmptyReplacement_DeletesMatches()
    {
        var options = new Dictionary<string, object> { ["Pattern"] = @"\s*#.*", ["Replacement"] = string.Empty };

        var output = await ProcessAsync("a = 1 # note\nb = 2\n", options);

        Assert.That(output, Is.EqualTo("a = 1\nb = 2\n"));
    }

    [Test]
    public async Task ProcessChunkAsync_NoReplacement_LeavesTextUnchanged()
    {
        var options = new Dictionary<string, object> { ["Pattern"] = "a" };

        var output = await ProcessAsync("banana\n", options);

        Assert.That(output, Is.EqualTo("banana\n"));
    }

    [Test]
    public async Task ProcessChunkAsync_MatchWholeLine_KeepsEachMatchingLineOnce()
    {
        var options = new Dictionary<string, object> { ["Pattern"] = "a", ["MatchWholeLine"] = true };

        var output = await ProcessAsync("banana\ncherry\navocado\n", options);

        Assert.That(output, Is.EqualTo("banana" + Environment.NewLine + "avocado" + Environment.NewLine));
    }

    [Test]
    public async Task ProcessChunkAsync_MatchWholeLine_HandlesCrLfInput()
    {
        var options = new Dictionary<string, object> { ["Pattern"] = "^b", ["MatchWholeLine"] = true };

        var output = await ProcessAsync("apple\r\nbanana\r\nblueberry\r\n", options);

        Assert.That(output, Is.EqualTo("banana" + Environment.NewLine + "blueberry" + Environment.NewLine));
    }
}

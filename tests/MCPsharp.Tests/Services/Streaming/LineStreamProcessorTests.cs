using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;

namespace MCPsharp.Tests.Services.Streaming;

[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class LineStreamProcessorTests
{
    private static readonly string[] ExpectedNumberedDuplicates = { "1: same", "2: other", "3: same", "4: same" };
    private static readonly string[] ExpectedFilteredDuplicates = { "1: dup", "3: dup", "5: dup" };

    private static StreamChunk CreateChunk(string text) => new()
    {
        Data = Encoding.UTF8.GetBytes(text),
        IsLastChunk = true
    };

    [Test]
    public async Task ProcessChunkAsync_DuplicateLines_NumbersEachLineByPosition()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["IncludeLineNumbers"] = true };

        var result = await processor.ProcessChunkAsync(CreateChunk("same\nother\nsame\nsame"), options);

        Assert.That(result.Lines, Is.EqualTo(ExpectedNumberedDuplicates));
    }

    [Test]
    public async Task ProcessChunkAsync_FilteredDuplicateLines_KeepOriginalLineNumbers()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object>
        {
            ["IncludeLineNumbers"] = true,
            ["LineFilter"] = "^dup$"
        };

        var result = await processor.ProcessChunkAsync(CreateChunk("dup\nx\ndup\ny\ndup"), options);

        Assert.That(result.Lines, Is.EqualTo(ExpectedFilteredDuplicates));
    }

    [Test]
    public void ProcessChunkAsync_InvalidEncoding_ReturnsFaultedTask()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["Encoding"] = "not-a-real-encoding" };

        var task = processor.ProcessChunkAsync(CreateChunk("a"), options);

        Assert.That(task.IsFaulted, Is.True);
    }

    [Test]
    public async Task ValidateOptionsAsync_InvalidEncoding_ReturnsFalse()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["Encoding"] = "not-a-real-encoding" };

        Assert.That(await processor.ValidateOptionsAsync(options), Is.False);
    }
}

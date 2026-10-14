using System.Text;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class LineStreamProcessorTests
{
    private static readonly string[] ExpectedNumberedDuplicates = { "1: same", "2: other", "3: same", "4: same" };
    private static readonly string[] ExpectedFilteredDuplicates = { "1: dup", "3: dup", "5: dup" };

    private static readonly string[] ExpectedContinuedNumbers = { "10: a", "11: b" };
    private static readonly string[] ExpectedFirstChunkNumbers = { "1: a" };
    private static readonly string[] ExpectedWithEmptyLines = { "a", "", "b", "" };
    private static readonly string[] ExpectedWithoutEmptyLines = { "a", "b" };

    private static StreamChunk CreateChunk(string text) => new()
    {
        Data = Encoding.UTF8.GetBytes(text),
        IsLastChunk = true
    };

    private static StreamChunk CreateChunk(string text, long startLineNumber)
    {
        var chunk = CreateChunk(text);
        chunk.Metadata[LineStreamProcessor.StartLineNumberKey] = startLineNumber;
        return chunk;
    }

    [Test]
    public async Task ProcessChunkAsync_StartLineNumberMetadata_ContinuesNumbering()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["IncludeLineNumbers"] = true };

        var result = await processor.ProcessChunkAsync(CreateChunk("a\nb", 10), options);

        Assert.That(result.Lines, Is.EqualTo(ExpectedContinuedNumbers));
    }

    [Test]
    public async Task ProcessChunkAsync_SharedInstance_DoesNotCarryNumberingBetweenRuns()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["IncludeLineNumbers"] = true };

        await processor.ProcessChunkAsync(CreateChunk("x\ny\nz", 40), options);
        var result = await processor.ProcessChunkAsync(CreateChunk("a"), options);

        Assert.That(result.Lines, Is.EqualTo(ExpectedFirstChunkNumbers));
    }

    [Test]
    public async Task ProcessChunkAsync_EmptyLines_AreKeptByDefault()
    {
        var processor = new LineStreamProcessor();

        var result = await processor.ProcessChunkAsync(CreateChunk("a\n\nb\n   \n"), new Dictionary<string, object>());

        Assert.That(result.Lines, Is.EqualTo(ExpectedWithEmptyLines));
        var nl = Environment.NewLine;
        Assert.That(Encoding.UTF8.GetString(result.Data), Is.EqualTo($"a{nl}{nl}b{nl}{nl}"));
    }

    [Test]
    public async Task ProcessChunkAsync_RemoveEmptyLines_DropsEmptyAndWhitespaceLines()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["RemoveEmptyLines"] = true };

        var result = await processor.ProcessChunkAsync(CreateChunk("a\n\n  \nb"), options);

        Assert.That(result.Lines, Is.EqualTo(ExpectedWithoutEmptyLines));
        Assert.That(Encoding.UTF8.GetString(result.Data), Is.EqualTo($"a{Environment.NewLine}b"));
    }

    [Test]
    public async Task ProcessChunkAsync_TrailingLineBreak_TerminatesLastLineWithoutAddingOne()
    {
        var processor = new LineStreamProcessor();
        var options = new Dictionary<string, object> { ["IncludeLineNumbers"] = true };

        var result = await processor.ProcessChunkAsync(CreateChunk("a\r\nb\r\n"), options);

        Assert.That(result.Lines, Has.Count.EqualTo(2));
        var nl = Environment.NewLine;
        Assert.That(Encoding.UTF8.GetString(result.Data), Is.EqualTo($"1: a{nl}2: b{nl}"));
    }

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

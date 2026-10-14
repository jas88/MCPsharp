using System.Text;
using MCPsharp.Services.Streaming;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class StreamChunkReaderTests
{
    private static readonly string[] ExpectedFixedChunks = { "abcd", "efgh", "ij" };

    private static async Task<List<string>> ReadAllAsync(string text, int chunkSize, bool alignToLines)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var reader = new StreamChunkReader(stream, chunkSize, alignToLines);
        var chunks = new List<string>();
        while (await reader.ReadChunkAsync(CancellationToken.None) is { } chunk)
            chunks.Add(Encoding.UTF8.GetString(chunk));

        Assert.That(reader.IsAtEnd, Is.True);
        return chunks;
    }

    [Test]
    public async Task ReadChunkAsync_AlignedToLines_EndsEveryChunkButTheLastOnALineBreak()
    {
        var chunks = await ReadAllAsync("one\ntwo\r\nthree\rfour\nfive", 5, alignToLines: true);

        Assert.That(string.Concat(chunks), Is.EqualTo("one\ntwo\r\nthree\rfour\nfive"));
        Assert.That(chunks.SkipLast(1).All(c => c.EndsWith('\n') || c.EndsWith('\r')), Is.True);
        Assert.That(chunks.Any(c => c.EndsWith("two\r", StringComparison.Ordinal)), Is.False, "\\r\\n must not be split");
    }

    [Test]
    public async Task ReadChunkAsync_NotAligned_ReturnsFixedSizeChunks()
    {
        var chunks = await ReadAllAsync("abcdefghij", 4, alignToLines: false);

        Assert.That(chunks, Is.EqualTo(ExpectedFixedChunks));
    }

    [Test]
    public async Task ReadChunkAsync_LineLongerThanCarryLimit_IsSplit()
    {
        var longLine = new string('x', 200);

        var chunks = await ReadAllAsync(longLine + "\nend\n", 4, alignToLines: true);

        Assert.That(string.Concat(chunks), Is.EqualTo(longLine + "\nend\n"));
        Assert.That(chunks.Max(c => c.Length), Is.LessThanOrEqualTo(4 * 16 + 4));
    }

    [TestCase("abc", 0)]
    [TestCase("ab\ncd", 3)]
    [TestCase("ab\r", 0)]
    [TestCase("ab\rc", 3)]
    [TestCase("a\r\n", 3)]
    public void FindLineAlignedCut_ReturnsLengthUpToLastSafeLineBreak(string data, int expected)
    {
        Assert.That(StreamChunkReader.FindLineAlignedCut(Encoding.ASCII.GetBytes(data)), Is.EqualTo(expected));
    }

    [TestCase("", 0)]
    [TestCase("a\nb\r\nc\rd", 3)]
    [TestCase("a\r", 1)]
    [TestCase("\n\n", 2)]
    public void CountLineBreaks_CountsCrLfOnce(string data, long expected)
    {
        Assert.That(TextChunkLines.CountLineBreaks(Encoding.ASCII.GetBytes(data)), Is.EqualTo(expected));
    }
}

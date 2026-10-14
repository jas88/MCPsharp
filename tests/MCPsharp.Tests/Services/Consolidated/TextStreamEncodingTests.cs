using System.Text;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// Byte order mark detection and output transcoding for the consolidated text processors
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class TextStreamEncodingTests
{
    [TestCase(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, 1200, 2)]
    [TestCase(new byte[] { 0xFE, 0xFF, 0x00, 0x41 }, 1201, 2)]
    [TestCase(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 }, 65001, 3)]
    [TestCase(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 }, 12000, 4)]
    [TestCase(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41 }, 12001, 4)]
    [TestCase(new byte[] { 0x41 }, 28591, 0)]
    [TestCase(new byte[0], 28591, 0)]
    public async Task Detect_NonSeekableInput_RecognisesByteOrderMarkAndKeepsContent(byte[] input, int codePage, int markLength)
    {
        using var stream = new NonSeekableStream(input);
        var text = await TextStreamEncoding.DetectAsync(stream, Encoding.Latin1, CancellationToken.None);

        using var output = new MemoryStream();
        var written = await text.WriteByteOrderMarkAsync(output, CancellationToken.None);
        using var reader = text.CreateReader();
        var content = await reader.ReadToEndAsync();

        Assert.That(text.Encoding.CodePage, Is.EqualTo(codePage));
        Assert.That(text.Encoding.GetPreamble(), Is.Empty);
        Assert.That(written, Is.EqualTo(markLength));
        Assert.That(output.ToArray(), Is.EqualTo(input[..markLength]));
        Assert.That(content, Is.EqualTo(input.Length == 0 ? "" : "A"));
    }

    [Test]
    public async Task WriteUtf8_TranscodesToInputEncoding()
    {
        using var stream = new MemoryStream(new byte[] { 0xFE, 0xFF });
        var text = await TextStreamEncoding.DetectAsync(stream, Encoding.UTF8, CancellationToken.None);

        using var output = new MemoryStream();
        var written = await text.WriteUtf8Async(output, Encoding.UTF8.GetBytes("é"), CancellationToken.None);

        Assert.That(written, Is.EqualTo(2));
        Assert.That(output.ToArray(), Is.EqualTo(new byte[] { 0x00, 0xE9 }));
    }

    private sealed class NonSeekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}

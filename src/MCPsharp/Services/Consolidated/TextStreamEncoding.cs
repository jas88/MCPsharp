using System.Text;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// The text encoding of a processor's input and output: the encoding named by a byte order mark at the
/// start of the input, or the configured encoding when there is none. Output is written in the same
/// encoding and starts with the byte order mark only when the input did, so text processors do not
/// change a file's encoding. Bytes that are not valid in the encoding fail the read, and characters
/// that cannot be represented in it fail the write (<see cref="DecoderFallbackException"/> and
/// <see cref="EncoderFallbackException"/>), rather than being silently replaced.
/// </summary>
internal sealed class TextStreamEncoding
{
    private readonly byte[] _byteOrderMark;

    private TextStreamEncoding(Encoding encoding, byte[] byteOrderMark, Stream content)
    {
        Encoding = encoding;
        _byteOrderMark = byteOrderMark;
        Content = content;
    }

    /// <summary>
    /// The text encoding, without a preamble so readers and writers neither expect nor emit one
    /// </summary>
    public Encoding Encoding { get; }

    /// <summary>
    /// The input positioned after the byte order mark, if there was one
    /// </summary>
    public Stream Content { get; }

    public bool IsUtf8 => Encoding.CodePage == Encoding.UTF8.CodePage;

    /// <summary>
    /// Reads the byte order mark, if any, from the start of <paramref name="input"/>.
    /// </summary>
    /// <param name="input">The input stream, positioned at its start.</param>
    /// <param name="configuredEncoding">The encoding to use when the input has no byte order mark.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<TextStreamEncoding> DetectAsync(Stream input, Encoding configuredEncoding, CancellationToken ct)
    {
        var prefix = new byte[4];
        var read = 0;
        while (read < prefix.Length)
        {
            var count = await input.ReadAsync(prefix.AsMemory(read), ct).ConfigureAwait(false);
            if (count == 0)
                break;
            read += count;
        }

        var (markedEncoding, markLength) = DetectByteOrderMark(prefix.AsSpan(0, read));
        var content = SkipBytes(input, prefix[..read], markLength);
        return new TextStreamEncoding(
            WithoutPreamble(markedEncoding ?? configuredEncoding),
            prefix[..markLength],
            content);
    }

    /// <summary>
    /// Creates a reader over <see cref="Content"/> in <see cref="Encoding"/>
    /// </summary>
    public StreamReader CreateReader()
        => new(Content, Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

    /// <summary>
    /// Checks that <paramref name="content"/> is valid in <see cref="Encoding"/>, for input that is not
    /// decoded through <see cref="CreateReader"/>.
    /// </summary>
    /// <exception cref="DecoderFallbackException">The content contains bytes that are not valid in the encoding.</exception>
    public void Validate(ReadOnlySpan<byte> content) => _ = Encoding.GetCharCount(content);

    /// <summary>
    /// Writes the input's byte order mark to <paramref name="output"/> if the input had one.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    public async Task<int> WriteByteOrderMarkAsync(Stream output, CancellationToken ct)
    {
        if (_byteOrderMark.Length > 0)
            await output.WriteAsync(_byteOrderMark, ct).ConfigureAwait(false);
        return _byteOrderMark.Length;
    }

    /// <summary>
    /// Writes UTF-8 text to <paramref name="output"/>, transcoding it to <see cref="Encoding"/>.
    /// </summary>
    /// <returns>The number of bytes written.</returns>
    public async Task<int> WriteUtf8Async(Stream output, ReadOnlyMemory<byte> utf8, CancellationToken ct)
    {
        var bytes = IsUtf8 ? utf8 : Encoding.GetBytes(Encoding.UTF8.GetString(utf8.Span));
        await output.WriteAsync(bytes, ct).ConfigureAwait(false);
        return bytes.Length;
    }

    private static (Encoding? Encoding, int Length) DetectByteOrderMark(ReadOnlySpan<byte> prefix) => prefix switch
    {
        [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(bigEndian: false, byteOrderMark: false), 4),
        [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(bigEndian: true, byteOrderMark: false), 4),
        [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 3),
        [0xFF, 0xFE, ..] => (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2),
        [0xFE, 0xFF, ..] => (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2),
        _ => (null, 0)
    };

    private static Encoding WithoutPreamble(Encoding encoding)
    {
        var plain = encoding.CodePage switch
        {
            65001 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            1200 => new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
            1201 => new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
            12000 => new UTF32Encoding(bigEndian: false, byteOrderMark: false),
            12001 => new UTF32Encoding(bigEndian: true, byteOrderMark: false),
            _ => encoding
        };

        var strict = (Encoding)plain.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        strict.DecoderFallback = new InvalidInputDecoderFallback(plain.WebName);
        return strict;
    }

    private static Stream SkipBytes(Stream input, byte[] prefix, int count)
    {
        if (count == prefix.Length)
            return input;

        if (input.CanSeek)
        {
            input.Seek(count - prefix.Length, SeekOrigin.Current);
            return input;
        }

        return new PrefixedReadStream(prefix[count..], input);
    }
}

namespace MCPsharp.Services.Streaming;

/// <summary>
/// Reads a stream in chunks of roughly a fixed size. When aligned to lines, each chunk except the
/// last ends just after a line break, so text processors never see a line (or a multi-byte UTF-8
/// character) split between two chunks; the remainder is carried into the next chunk. A line longer
/// than <see cref="MaxCarryMultiplier"/> chunks is split rather than buffered without bound.
/// Line alignment works on raw bytes and so assumes an ASCII-compatible encoding such as UTF-8.
/// </summary>
internal sealed class StreamChunkReader
{
    private const int MaxCarryMultiplier = 16;

    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private readonly bool _alignToLines;
    private byte[] _pending = Array.Empty<byte>();

    public StreamChunkReader(Stream stream, int chunkSize, bool alignToLines)
    {
        _stream = stream;
        _buffer = new byte[chunkSize];
        _alignToLines = alignToLines;
    }

    /// <summary>Size of the read buffer</summary>
    public int BufferSize => _buffer.Length;

    /// <summary>True once every byte of the stream has been returned in a chunk</summary>
    public bool IsAtEnd => _pending.Length == 0 && _stream.Position == _stream.Length;

    /// <summary>
    /// Returns the next chunk, or null when the stream is exhausted.
    /// </summary>
    public async Task<byte[]?> ReadChunkAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var read = await _stream.ReadAsync(_buffer.AsMemory(0, _buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_pending.Length == 0)
                    return null;

                var rest = _pending;
                _pending = Array.Empty<byte>();
                return rest;
            }

            var data = new byte[_pending.Length + read];
            _pending.CopyTo(data, 0);
            Array.Copy(_buffer, 0, data, _pending.Length, read);
            _pending = Array.Empty<byte>();

            if (!_alignToLines)
                return data;

            var cut = FindLineAlignedCut(data);
            if (cut == 0)
            {
                if (data.Length < (long)_buffer.Length * MaxCarryMultiplier)
                {
                    _pending = data;
                    continue;
                }

                cut = data.Length;
            }

            _pending = data[cut..];
            return cut == data.Length ? data : data[..cut];
        }
    }

    /// <summary>
    /// Returns the length of the longest prefix of <paramref name="data"/> ending in a line break, or 0
    /// if there is none. A trailing '\r' is not a safe cut because the '\n' of a "\r\n" may follow it.
    /// </summary>
    internal static int FindLineAlignedCut(byte[] data)
    {
        for (var i = data.Length - 1; i >= 0; i--)
        {
            if (data[i] == '\n' || (data[i] == '\r' && i < data.Length - 1))
                return i + 1;
        }

        return 0;
    }
}

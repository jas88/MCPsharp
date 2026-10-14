namespace MCPsharp.Services.Consolidated;

/// <summary>
/// A forward-only, read-only stream that returns <c>prefix</c> followed by the rest of an inner stream,
/// used to put back bytes that were read ahead from a non-seekable stream. The inner stream is not
/// disposed with this stream.
/// </summary>
internal sealed class PrefixedReadStream : Stream
{
    private readonly byte[] _prefix;
    private readonly Stream _inner;
    private int _prefixPosition;

    public PrefixedReadStream(byte[] prefix, Stream inner)
    {
        _prefix = prefix;
        _inner = inner;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var fromPrefix = CopyPrefix(buffer);
        return fromPrefix > 0 ? fromPrefix : _inner.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var fromPrefix = CopyPrefix(buffer.Span);
        return fromPrefix > 0 ? ValueTask.FromResult(fromPrefix) : _inner.ReadAsync(buffer, cancellationToken);
    }

    public override void Flush()
    {
        // Read-only: nothing to flush
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int CopyPrefix(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _prefix.Length - _prefixPosition);
        if (count <= 0)
            return 0;

        _prefix.AsSpan(_prefixPosition, count).CopyTo(buffer);
        _prefixPosition += count;
        return count;
    }
}

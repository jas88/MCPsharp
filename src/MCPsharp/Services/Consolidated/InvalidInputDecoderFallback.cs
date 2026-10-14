using System.Text;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// A decoder fallback that rejects bytes which are not valid in the input's encoding, throwing a
/// <see cref="DecoderFallbackException"/> that names the encoding, rather than replacing them with U+FFFD
/// </summary>
internal sealed class InvalidInputDecoderFallback : DecoderFallback
{
    private readonly string _encodingName;

    public InvalidInputDecoderFallback(string encodingName)
    {
        _encodingName = encodingName;
    }

    public override int MaxCharCount => 0;

    public override DecoderFallbackBuffer CreateFallbackBuffer() => new Buffer(_encodingName);

    private sealed class Buffer : DecoderFallbackBuffer
    {
        private readonly string _encodingName;

        public Buffer(string encodingName)
        {
            _encodingName = encodingName;
        }

        public override int Remaining => 0;

        public override bool Fallback(byte[] bytesUnknown, int index)
            => throw new DecoderFallbackException(
                $"Input is not valid {_encodingName}: cannot decode byte(s) {Convert.ToHexString(bytesUnknown)}",
                bytesUnknown,
                index);

        public override char GetNextChar() => '\0';

        public override bool MovePrevious() => false;
    }
}

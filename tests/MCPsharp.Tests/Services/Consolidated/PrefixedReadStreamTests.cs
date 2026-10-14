using MCPsharp.Services.Consolidated;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// The read-ahead stream used to put back bytes read from a non-seekable input
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class PrefixedReadStreamTests
{
    private static readonly byte[] Prefix = { 1, 2 };
    private static readonly byte[] Rest = { 3, 4 };
    private static readonly byte[] Combined = { 1, 2, 3, 4 };

    [Test]
    public async Task ReadAsync_ReturnsPrefixThenInnerStream()
    {
        using var inner = new MemoryStream(Rest);
        await using var stream = new PrefixedReadStream(Prefix, inner);
        using var output = new MemoryStream();

        await stream.CopyToAsync(output);

        Assert.That(output.ToArray(), Is.EqualTo(Combined));
    }

    [Test]
    public void ReadAsyncMemory_CancelledToken_ThrowsWithoutReturningPrefix()
    {
        using var inner = new MemoryStream(Rest);
        using var stream = new PrefixedReadStream(Prefix, inner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var buffer = new byte[4];

        Assert.That(async () => await stream.ReadAsync(buffer.AsMemory(), cts.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(stream.Read(buffer), Is.EqualTo(2), "The prefix must still be available after a cancelled read");
    }

    [Test]
    public void ReadAsyncArray_CancelledToken_ThrowsWithoutReturningPrefix()
    {
        using var inner = new MemoryStream(Rest);
        using var stream = new PrefixedReadStream(Prefix, inner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var buffer = new byte[4];

        // The array overload is the subject of this test
#pragma warning disable CA1835
        Assert.That(async () => await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token), Throws.InstanceOf<OperationCanceledException>());
#pragma warning restore CA1835
        Assert.That(stream.Read(buffer), Is.EqualTo(2), "The prefix must still be available after a cancelled read");
    }
}

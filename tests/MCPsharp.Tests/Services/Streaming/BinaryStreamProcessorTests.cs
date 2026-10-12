using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;

namespace MCPsharp.Tests.Services.Streaming;

[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class BinaryStreamProcessorTests
{
    private static readonly byte[] SampleData = Encoding.UTF8.GetBytes("checksum me");

    private static StreamChunk CreateChunk() => new()
    {
        Data = SampleData,
        Length = SampleData.Length,
        IsLastChunk = true
    };

    private static Dictionary<string, object> ChecksumOptions(object algorithm) => new()
    {
        ["CalculateChecksums"] = true,
        ["ChecksumAlgorithm"] = algorithm
    };

    [Test]
    public async Task ProcessChunkAsync_DefaultAlgorithm_UsesSha256()
    {
        var processor = new BinaryStreamProcessor();

        var result = await processor.ProcessChunkAsync(CreateChunk(), new Dictionary<string, object>());

        Assert.That(result.Checksum, Is.EqualTo(Convert.ToHexString(SHA256.HashData(SampleData)).ToLowerInvariant()));
    }

    [TestCase("SHA256")]
    [TestCase("sha-256")]
    public async Task ProcessChunkAsync_Sha256_ComputesSha256(string algorithm)
    {
        var processor = new BinaryStreamProcessor();

        var result = await processor.ProcessChunkAsync(CreateChunk(), ChecksumOptions(algorithm));

        Assert.That(result.Checksum, Is.EqualTo(Convert.ToHexString(SHA256.HashData(SampleData)).ToLowerInvariant()));
    }

    [TestCase("SHA512")]
    [TestCase("sha-512")]
    public async Task ProcessChunkAsync_Sha512_ComputesSha512(string algorithm)
    {
        var processor = new BinaryStreamProcessor();

        var result = await processor.ProcessChunkAsync(CreateChunk(), ChecksumOptions(algorithm));

        Assert.That(result.Checksum, Is.EqualTo(Convert.ToHexString(SHA512.HashData(SampleData)).ToLowerInvariant()));
    }

    [TestCase("MD5")]
    [TestCase("SHA1")]
    [TestCase("SHA-1")]
    [TestCase("CRC32")]
    [TestCase("")]
    public async Task ProcessChunkAsync_UnsupportedAlgorithm_Throws(string algorithm)
    {
        var processor = new BinaryStreamProcessor();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await processor.ProcessChunkAsync(CreateChunk(), ChecksumOptions(algorithm)));
    }

    [Test]
    public async Task ProcessChunkAsync_NonStringAlgorithm_Throws()
    {
        var processor = new BinaryStreamProcessor();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await processor.ProcessChunkAsync(CreateChunk(), ChecksumOptions(256)));
    }

    [TestCase("MD5", false)]
    [TestCase("SHA1", false)]
    [TestCase("SHA256", true)]
    [TestCase("SHA512", true)]
    public async Task ValidateOptionsAsync_ReportsAlgorithmSupport(string algorithm, bool expected)
    {
        var processor = new BinaryStreamProcessor();

        var valid = await processor.ValidateOptionsAsync(ChecksumOptions(algorithm));

        Assert.That(valid, Is.EqualTo(expected));
    }
}

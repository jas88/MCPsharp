using MCPsharp.Models.Streaming;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

public partial class StreamingFileProcessorTests
{
    [Test]
    public async Task ProcessMultipleFilesAsync_WithValidFiles_ShouldProcessAll()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("File 1 content"),
            CreateTestFile("File 2 content"),
            CreateTestFile("File 3 content")
        };
        var config = new StreamProcessRequest
        {
            MaxConcurrentFiles = 2,
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024,
            ProcessorOptions = new Dictionary<string, object>()
        };

        // Act
        var results = await _processor.ProcessMultipleFilesAsync(files, config);

        // Debug output
        Console.WriteLine($"Results count: {results?.Count ?? 0}");
        if (results != null)
        {
            foreach (var result in results)
            {
                Console.WriteLine($"Success: {result.Success}, Error: {result.ErrorMessage}");
            }
        }

        // Assert
        Assert.That(results, Is.Not.Null);
        if (results == null)
            throw new InvalidOperationException("Results should not be null");
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.All(r => r.Success), Is.True);
        Assert.That(results.Sum(r => r.ProcessedSize), Is.GreaterThan(0));
    }

    [Test]
    public async Task ProcessMultipleFilesAsync_WithMixedSuccess_ShouldReturnPartialResults()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("Valid content"),
            "/non/existent/file.txt", // Invalid file
            CreateTestFile("Another valid content")
        };
        var config = new StreamProcessRequest
        {
            MaxConcurrentFiles = 3,
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024,
            ProcessorOptions = new Dictionary<string, object>()
        };

        // Act
        var results = await _processor.ProcessMultipleFilesAsync(files, config);

        // Assert
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.Count(r => r.Success), Is.EqualTo(2));
        Assert.That(results.Count(r => !r.Success), Is.EqualTo(1));
    }

    [Test]
    public async Task ProcessMultipleFilesAsync_WithConcurrencyLimit_ShouldLimitParallelism()
    {
        // Arrange
        var files = Enumerable.Range(0, 10)
            .Select(i => CreateTestFile($"Content {i}"))
            .ToArray();
        var config = new StreamProcessRequest
        {
            MaxConcurrentFiles = 3,
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024,
            ProcessorOptions = new Dictionary<string, object>()
        };

        // Track how many files are being processed at once: progress is created when a file starts
        // and completed when it finishes. The delay holds each start open so concurrent runs overlap.
        var inFlight = 0;
        var maxInFlight = 0;
        _mockProgressTracker.CreateProgressAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>())
            .Returns(async call =>
            {
                var current = Interlocked.Increment(ref inFlight);
                InterlockedMax(ref maxInFlight, current);
                await Task.Delay(20);
                return call.ArgAt<string>(0);
            });
        _mockProgressTracker.CompleteProgressAsync(Arg.Any<string>())
            .Returns(_ =>
            {
                Interlocked.Decrement(ref inFlight);
                return Task.CompletedTask;
            });

        // Act
        var results = await _processor.ProcessMultipleFilesAsync(files, config);

        // Assert
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Count, Is.EqualTo(10));
        Assert.That(results.All(r => r.Success), Is.True);
        Assert.That(maxInFlight, Is.LessThanOrEqualTo(3), "no more files than MaxConcurrentFiles at once");
        Assert.That(maxInFlight, Is.GreaterThan(1), "files are processed concurrently");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref target, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }

    [Test]
    public async Task ProcessFileWithTransformAsync_WithValidTransform_ShouldApplyTransform()
    {
        // Arrange
        var testFile = CreateTestFile("original content\nmore content");
        var config = new StreamProcessRequest
        {
            FilePath = testFile,
            OutputPath = testFile + ".processed",
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024,
            ProcessorOptions = new Dictionary<string, object>()
        };
        var transform = new Func<string, string>(content => content.ToUpperInvariant());

        // Act
        var result = await _processor.ProcessFileWithTransformAsync(testFile, config, transform);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.BytesProcessed, Is.EqualTo("ORIGINAL CONTENT\nMORE CONTENT".Length));
    }

    [Test]
    public async Task ProcessFileWithTransformAsync_WithTransformException_ShouldFail()
    {
        // Arrange
        var testFile = CreateTestFile("content");
        var config = new StreamProcessRequest
        {
            FilePath = testFile,
            OutputPath = testFile + ".processed",
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024,
            ProcessorOptions = new Dictionary<string, object>()
        };
        var transform = new Func<string, string>(_ => throw new InvalidOperationException("Transform failed"));

        // Act
        var result = await _processor.ProcessFileWithTransformAsync(testFile, config, transform);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Transform failed"));
    }

    [Test]
    public async Task ProcessFileWithFilterAsync_WithMatchingFilter_ShouldProcess()
    {
        // Arrange
        var testFile = CreateTestFile("content with keyword");
        var config = new StreamProcessRequest { FilePath = testFile };
        var filter = new Func<string, bool>(content => content.Contains("keyword"));

        // Act
        var result = await _processor.ProcessFileWithFilterAsync(testFile, config, filter);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.WasProcessed, Is.True);
    }

    [Test]
    public async Task ProcessFileWithFilterAsync_WithNonMatchingFilter_ShouldSkip()
    {
        // Arrange
        var testFile = CreateTestFile("content without special word");
        var config = new StreamProcessRequest { FilePath = testFile };
        var filter = new Func<string, bool>(content => content.Contains("keyword"));

        // Act
        var result = await _processor.ProcessFileWithFilterAsync(testFile, config, filter);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.WasProcessed, Is.False);
    }
}

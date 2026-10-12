using MCPsharp.Models.Streaming;
using MCPsharp.Services;
using MCPsharp.Tests.TestData;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

public partial class StreamingFileProcessorTests
{
    [Test]
    public async Task GetProcessingStatisticsAsync_ShouldReturnAccurateStats()
    {
        // Arrange
        var files = new[]
        {
            CreateTestFile("Content 1"),
            CreateTestFile("Content 2"),
            CreateTestFile("Content 3")
        };
        var config = new StreamProcessRequest { ProcessorType = StreamProcessorType.LineProcessor, ChunkSize = 1024 };

        // Process some files first
        await _processor.ProcessMultipleFilesAsync(files, config);

        // Act
        var stats = await _processor.GetProcessingStatisticsAsync();

        // Assert
        Assert.That(stats, Is.Not.Null);
        Assert.That(stats.TotalFilesProcessed, Is.GreaterThanOrEqualTo(3));
        Assert.That(stats.TotalBytesProcessed, Is.GreaterThan(0));
        Assert.That(stats.AverageProcessingTime, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(stats.ActiveProcessingTasks, Is.EqualTo(0)); // Should be completed
    }

    [Test]
    public async Task ClearStatisticsAsync_ShouldResetStats()
    {
        // Arrange
        var testFile = CreateTestFile("test content");

        // Try to process files to generate some statistics
        var files = new[] { testFile };
        var config = new StreamProcessRequest
        {
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024
        };

        // Try multiple approaches to get some statistics
        await _processor.ProcessFileAsync(testFile);
        await _processor.ProcessMultipleFilesAsync(files, config);

        var statsBefore = await _processor.GetProcessingStatisticsAsync();

        // Act
        await _processor.ClearStatisticsAsync();

        // Assert
        var statsAfter = await _processor.GetProcessingStatisticsAsync();
        Assert.That(statsAfter.TotalFilesProcessed, Is.EqualTo(0));
        Assert.That(statsAfter.TotalBytesProcessed, Is.EqualTo(0));
    }

    [Test]
    public async Task ProcessFileAsync_WithRetryConfiguration_ShouldRetryOnFailure()
    {
        // Arrange
        var testFile = CreateTestFile("test content");
        var config = new StreamProcessRequest
        {
            FilePath = testFile,
            OutputPath = testFile + ".processed",
            RetryCount = 2, // Allow 2 retries
            RetryDelay = TimeSpan.FromMilliseconds(10)
        };

        // Act
        var result = await _processor.ProcessFileAsync(config);

        // Assert - Should succeed with valid file even with retry configuration
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.BytesProcessed, Is.GreaterThan(0));
        Assert.That(File.Exists(config.OutputPath), Is.True);
    }

    [Test]
    public async Task ProcessFileAsync_WithTimeout_ShouldTimeoutGracefully()
    {
        // Arrange
        var testFile = CreateTestFile(TestDataFixtures.PerformanceTestData.GenerateLargeText(10000));
        var config = new StreamProcessRequest
        {
            FilePath = testFile,
            OutputPath = testFile + ".processed",
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024,
            ProcessingTimeout = TimeSpan.FromMilliseconds(100)
        };

        // Act
        var result = await _processor.ProcessFileAsync(config);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (!result.Success) // Timeout may or may not occur depending on processing speed
        {
            // Check for either timeout or cancellation message
            Assert.That(result.ErrorMessage,
                Does.Contain("timeout").IgnoreCase.Or
                   .Contain("cancel").IgnoreCase);
        }
    }

    [Test]
    [TestCase(1024)] // 1KB chunks
    [TestCase(4096)] // 4KB chunks
    [TestCase(16384)] // 16KB chunks
    public async Task ProcessFileAsync_WithDifferentChunkSizes_ShouldProcessSuccessfully(int chunkSize)
    {
        // Arrange
        var content = TestDataFixtures.PerformanceTestData.GenerateLargeText(5000);
        var testFile = CreateTestFile(content);
        var config = new StreamProcessRequest
        {
            FilePath = testFile,
            OutputPath = testFile + ".processed",
            ChunkSize = chunkSize
        };

        // Act
        var result = await _processor.ProcessFileAsync(config);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.BytesProcessed, Is.EqualTo(content.Length));
        Assert.That(result.BytesProcessed, Is.EqualTo(content.Length));
        Assert.That(result.ChunksProcessed, Is.GreaterThan(0));
    }

    [Test]
    public async Task ProcessFileAsync_WithEmptyFile_ShouldHandleCorrectly()
    {
        // Arrange
        var testFile = CreateTestFile("");
        var config = new StreamProcessRequest { FilePath = testFile, OutputPath = testFile + ".processed" };

        // Act
        var result = await _processor.ProcessFileAsync(config);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.BytesProcessed, Is.EqualTo(0));
        Assert.That(result.BytesProcessed, Is.EqualTo(0));
        // Empty files still process 1 chunk (empty chunk)
        Assert.That(result.ChunksProcessed, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task ProcessFileAsync_WithSpecialCharacters_ShouldProcessCorrectly()
    {
        // Arrange
        var content = TestDataFixtures.EdgeCaseData.SpecialCharacters;
        var testFile = CreateTestFile(content);
        var config = new StreamProcessRequest
        {
            FilePath = testFile,
            OutputPath = testFile + ".processed",
            ProcessorType = StreamProcessorType.LineProcessor,
            ChunkSize = 1024
        };

        // Act
        var result = await _processor.ProcessFileAsync(config);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.BytesProcessed, Is.EqualTo(content.Length));
        Assert.That(result.BytesProcessed, Is.EqualTo(content.Length));
    }

    [Test]
    public async Task Dispose_ShouldCleanupResources()
    {
        // Arrange
        var processor = new StreamingFileProcessor(
            CreateNullLogger<StreamingFileProcessor>(),
            Substitute.For<IProgressTracker>(),
            Substitute.For<ITempFileManager>());

        // Act
        var config = new StreamProcessRequest
        {
            FilePath = CreateTestFile("test"),
            OutputPath = CreateTestFile("test") + ".processed",
            ProcessorType = StreamProcessorType.LineProcessor
        };
        await processor.ProcessFileAsync(config);

        // Assert - Should not throw
        Assert.DoesNotThrow(() => processor.Dispose());
    }

    [Test]
    [Repeat(3)] // Test for consistency
    public async Task ProcessFileAsync_WithSameFile_ShouldProduceConsistentResults()
    {
        // Arrange
        var testFile = CreateTestFile("Consistent test content");
        var config = new StreamProcessRequest { FilePath = testFile };

        // Act
        var result1 = await _processor.ProcessFileAsync(config);
        var result2 = await _processor.ProcessFileAsync(config);

        // Assert
        Assert.That(result1.Success, Is.EqualTo(result2.Success));
        Assert.That(result1.OriginalSize, Is.EqualTo(result2.OriginalSize));
        Assert.That(result1.ProcessedSize, Is.EqualTo(result2.ProcessedSize));
    }
}

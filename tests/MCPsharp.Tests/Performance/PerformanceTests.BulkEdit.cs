using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Performance;

public partial class PerformanceTests
{
    [Test]
    [Category("BulkEdit")]
    public async Task Performance_BulkReplaceAsync_WithManyFiles_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = Enumerable.Range(0, 1000)
            .Select(i => CreateTempFile($"Content {i} with some text to replace", ".cs"))
            .ToList();

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var result = await _bulkEdit.BulkReplaceAsync(
                files,
                @"Content \d+",
                "REPLACED",
                new BulkEditOptions { MaxParallelism = Environment.ProcessorCount, CreateBackups = false });

            Assert.That(result.Success, Is.True, "Bulk replace should succeed");
        }, iterations: 1, warmup: 0); // Only run once due to setup cost

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        Console.WriteLine($"BulkReplaceAsync (1000 files) Performance: {performance}");

        // Verify processing rate
        var filesPerSecond = files.Count / performance.AverageTime.TotalSeconds;
        Assert.That(filesPerSecond, Is.GreaterThan(33), $"Should process at least 33 files/second, got {filesPerSecond:F2}");
    }

    [Test]
    [Category("BulkEdit")]
    public async Task Performance_BulkReplaceAsync_WithLargeFiles_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var largeFiles = Enumerable.Range(0, 10)
            .Select(i => CreateTempFile(TestDataFixtures.PerformanceTestData.GenerateLargeText(50000), ".cs"))
            .ToList();

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var result = await _bulkEdit.BulkReplaceAsync(
                largeFiles,
                @"Line \d+:",
                "PROCESSED Line:",
                new BulkEditOptions { MaxParallelism = 4, CreateBackups = false });

            Assert.That(result.Success, Is.True, "Bulk replace on large files should succeed");
        }, iterations: 3, warmup: 1);

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(45));
        Console.WriteLine($"BulkReplaceAsync (large files) Performance: {performance}");

        // Verify memory efficiency
        var totalSize = largeFiles.Sum(f => new System.IO.FileInfo(f).Length);
        var sizePerSecond = totalSize / (1024.0 * 1024.0) / performance.AverageTime.TotalSeconds;
        Assert.That(sizePerSecond, Is.GreaterThan(5), $"Should process at least 5 MB/second, got {sizePerSecond:F2}");
    }

    [Test]
    [Category("BulkEdit")]
    public async Task Performance_PreviewBulkChangesAsync_WithManyFiles_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = Enumerable.Range(0, 500)
            .Select(i => CreateTempFile($"File {i} content with pattern", ".cs"))
            .ToList();

        var request = new BulkEditRequest
        {
            OperationType = BulkEditOperationType.BulkReplace,
            Files = files,
            RegexPattern = @"content",
            RegexReplacement = "REPLACED",
            Options = new BulkEditOptions { PreviewMode = true }
        };

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var preview = await _bulkEdit.PreviewBulkChangesAsync(request);
            Assert.That(preview.Success, Is.True, "Preview should succeed");
            Assert.That(preview.FilePreviews.Count, Is.EqualTo(files.Count), "Should preview all files");
        }, iterations: 3, warmup: 1);

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(25));
        Console.WriteLine($"PreviewBulkChangesAsync (500 files) Performance: {performance}");
    }

    [Test]
    [Category("BulkEdit")]
    public async Task Performance_MultiFileEditAsync_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = Enumerable.Range(0, 100)
            .Select(i => CreateTempFile($"Content {i}", ".cs"))
            .ToList();

        var operations = new List<MultiFileEditOperation>
        {
            new()
            {
                FilePattern = "*.cs",
                Priority = 1,
                Edits = new List<TextEdit>
                {
                    new ReplaceEdit
                    {
                        StartLine = 1,
                        StartColumn = 1,
                        EndLine = 1,
                        EndColumn = 1,
                        NewText = "MODIFIED "
                    }
                }
            },
            new()
            {
                FilePattern = "*.cs",
                Priority = 2,
                Edits = new List<TextEdit>
                {
                    new ReplaceEdit
                    {
                        StartLine = 1,
                        StartColumn = 1,
                        EndLine = 1,
                        EndColumn = 1,
                        NewText = "PROCESSED "
                    }
                }
            }
        };

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var result = await _bulkEdit.MultiFileEditAsync(operations);
            Assert.That(result.Success, Is.True, "Multi-file edit should succeed");
        }, iterations: 5, warmup: 2);

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        Console.WriteLine($"MultiFileEditAsync Performance: {performance}");
    }

    [Test]
    [Category("Memory")]
    public async Task Memory_BulkEditOnLargeFiles_ShouldNotExceedMemoryLimit()
    {
        // Arrange
        var initialMemory = GC.GetTotalMemory(true);
        var largeFiles = Enumerable.Range(0, 5)
            .Select(i => CreateTempFile(TestDataFixtures.PerformanceTestData.GenerateLargeText(100000), ".cs"))
            .ToList();

        // Act
        var beforeEditMemory = GC.GetTotalMemory(true);

        var result = await _bulkEdit.BulkReplaceAsync(
            largeFiles,
            "Line",
            "PROCESSED",
            new BulkEditOptions { CreateBackups = false });

        var afterEditMemory = GC.GetTotalMemory(true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var finalMemory = GC.GetTotalMemory(true);

        // Assert
        Assert.That(result.Success, Is.True, "Bulk edit should succeed");

        var memoryUsed = afterEditMemory - beforeEditMemory;
        var totalFileSize = largeFiles.Sum(f => new System.IO.FileInfo(f).Length);
        var memoryEfficiency = (double)memoryUsed / totalFileSize;

        Console.WriteLine($"Memory efficiency ratio: {memoryEfficiency:F2}");
        Console.WriteLine($"Memory used: {memoryUsed / (1024.0 * 1024.0):F2} MB");
        Console.WriteLine($"Total file size: {totalFileSize / (1024.0 * 1024.0):F2} MB");

        // Memory usage should be reasonable compared to file size
        Assert.That(memoryEfficiency, Is.LessThan(3.0), "Memory usage should not exceed 3x file size");

        // Memory should be cleaned up after operation (allow more realistic memory usage)
        Assert.That(finalMemory - initialMemory, Is.LessThan(150 * 1024 * 1024), "Memory leak should be minimal");
    }
}

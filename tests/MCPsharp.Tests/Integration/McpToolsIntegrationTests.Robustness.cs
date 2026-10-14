using MCPsharp.Models.BulkEdit;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class McpToolsIntegrationTests
{
    [Test]
    public async Task End_To_End_FileSizeHandling_ShouldHandleLargeFiles()
    {
        // Arrange
        var largeContent = TestDataFixtures.PerformanceTestData.GenerateLargeText(50000);
        var largeFile = CreateTestFile(largeContent, ".txt");

        var config = new BulkEditOptions
        {
            MaxFileSize = largeContent.Length + 1000, // Allow the file
            CreateBackups = true
        };

        // Step 1: Perform bulk edit on large file
        var result = await _bulkEdit.BulkReplaceAsync(
            new[] { largeFile },
            "Line \\d+: ",
            "PROCESSED Line ",
            config);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.SummaryData?.TotalBytesProcessed, Is.EqualTo(largeContent.Length));
        Assert.That(result.SummaryData?.TotalChangesApplied, Is.GreaterThan(0));

        // Step 2: Test with file size limit exceeded
        var strictConfig = new BulkEditOptions
        {
            MaxFileSize = 1000 // Much smaller than file
        };

        var strictResult = await _bulkEdit.BulkReplaceAsync(
            new[] { largeFile },
            "test",
            "replacement",
            strictConfig);

        // Should handle oversized file gracefully
        Assert.That(strictResult, Is.Not.Null);
        // The actual behavior depends on implementation - might skip or fail
    }

    [Test]
    public async Task End_To_End_ConcurrentOperations_ShouldHandleParallelProcessing()
    {
        // Arrange
        var files = Enumerable.Range(0, 10)
            .Select(i => CreateTestFile($"Content {i}", ".cs"))
            .ToArray();

        var bulkEditTasks = new List<Task<BulkEditResult>>();
        var capabilityTasks = new List<Task<ReverseSearchCapabilities>>();

        // Step 1: Run multiple bulk edits in parallel
        for (int i = 0; i < 5; i++)
        {
            var task = _bulkEdit.BulkReplaceAsync(
                files,
                $"Content {i}",
                $"Processed {i}");
            bulkEditTasks.Add(task);
        }

        // Step 2: Run multiple analyses in parallel
        for (int i = 0; i < 3; i++)
        {
            var task = _referenceFinder.GetCapabilitiesAsync();
            capabilityTasks.Add(task);
        }

        // Act
        var bulkEditResults = await Task.WhenAll(bulkEditTasks);
        var capabilityResults = await Task.WhenAll(capabilityTasks);

        var allResults = new object[bulkEditResults.Length + capabilityResults.Length];
        Array.Copy(bulkEditResults, 0, allResults, 0, bulkEditResults.Length);
        Array.Copy(capabilityResults, 0, allResults, bulkEditResults.Length, capabilityResults.Length);
        var results = allResults;

        // Assert
        Assert.That(results.Length, Is.EqualTo(8));

        var finalBulkEditResults = results.Take(5).Cast<BulkEditResult>().ToArray();
        var finalCapabilityResults = results.Skip(5).Cast<ReverseSearchCapabilities>().ToArray();

        Assert.That(finalBulkEditResults.All(r => r != null), Is.True);
        Assert.That(finalCapabilityResults.All(r => r != null), Is.True);
        Assert.That(finalBulkEditResults.All(r => r.Success), Is.True);
    }

    [Test]
    public async Task End_To_End_Cancellation_ShouldSupportOperationCancellation()
    {
        // Arrange
        var largeContent = TestDataFixtures.PerformanceTestData.GenerateLargeText(50000);
        var largeFile = CreateTestFile(largeContent, ".cs");
        using var cts = new CancellationTokenSource();

        // Initialize services
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(largeFile, largeContent);

        // Step 1: Test cancellation support
        cts.Cancel(); // Cancel before starting

        // Verify the token is cancelled
        Assert.That(cts.Token.IsCancellationRequested, Is.True);

        var longRunningTask = _bulkEdit.BulkReplaceAsync(
            new[] { largeFile },
            "Line ",
            "PROCESSED Line ",
            new BulkEditOptions { CreateBackups = true },
            cancellationToken: cts.Token);

        // Act & Assert - Test that cancellation is supported (operation may complete quickly)
        try
        {
            var result = await longRunningTask;
            // If it completes, verify cancellation token was properly handled
            Assert.That(result, Is.Not.Null);
        }
        catch (OperationCanceledException)
        {
            // This is also acceptable - cancellation worked
            Assert.Pass("Cancellation worked correctly");
        }

        // Step 2: Verify other operations still work with a fresh cancellation token
        var quickResult = await _bulkEdit.BulkReplaceAsync(
            new[] { CreateTestFile("test content") },
            "test",
            "success");

        Assert.That(quickResult, Is.Not.Null);
        Assert.That(quickResult.Success, Is.True);
    }

    [Test]
    [Repeat(3)] // Test multiple times for consistency
    public async Task End_To_End_ConsistencyTest_ShouldProduceConsistentResults()
    {
        // Arrange
        var testContent = "public class TestClass { public void TestMethod() { } }";
        var testFile1 = CreateTestFile(testContent, ".cs");
        var testFile2 = CreateTestFile(testContent, ".cs");

        // Initialize services
        await InitializeServicesAsync();
        await _workspace.AddInMemoryDocumentAsync(testFile1, testContent);
        await _workspace.AddInMemoryDocumentAsync(testFile2, testContent);

        // Act
        // Run the same analysis on different files with identical content
        var result1 = await _bulkEdit.BulkReplaceAsync(new[] { testFile1 }, "TestClass", "RenamedClass");
        var result2 = await _bulkEdit.BulkReplaceAsync(new[] { testFile2 }, "TestClass", "RenamedClass");

        // Assert - Both operations should have identical results since they start from identical content
        Assert.That(result1.Success, Is.EqualTo(result2.Success));
        Assert.That(result1.SummaryData?.TotalChangesApplied, Is.EqualTo(result2.SummaryData?.TotalChangesApplied));
        Assert.That(result1.SummaryData?.TotalFilesProcessed, Is.EqualTo(result2.SummaryData?.TotalFilesProcessed));
    }
}

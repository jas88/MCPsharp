using System.Diagnostics;
using MCPsharp.Models.BulkEdit;
using NUnit.Framework;

namespace MCPsharp.Tests.Performance;

public partial class PerformanceTests
{
    [Test]
    [Category("Concurrency")]
    [Category("Manual")]
    [Explicit("Performance test - run manually, not reliable on shared CI runners")]
    public async Task Performance_ConcurrentBulkEdits_ShouldScaleWithParallelism()
    {
        // Arrange
        var fileSets = Enumerable.Range(0, 4)
            .Select(setIndex => Enumerable.Range(0, 100)
                .Select(i => CreateTempFile($"Set{setIndex} File{i} content", ".cs"))
                .ToList())
            .ToList();

        // Act - Sequential execution
        var sequentialTime = await MeasurePerformanceAsync(async () =>
        {
            foreach (var fileSet in fileSets)
            {
                await _bulkEdit.BulkReplaceAsync(
                    fileSet,
                    "content",
                    "SEQUENTIAL",
                    new BulkEditOptions { CreateBackups = false });
            }
        }, iterations: 1, warmup: 0);

        // Act - Parallel execution
        var parallelTime = await MeasurePerformanceAsync(async () =>
        {
            var tasks = fileSets.Select(fileSet =>
                _bulkEdit.BulkReplaceAsync(
                    fileSet,
                    "content",
                    "PARALLEL",
                    new BulkEditOptions { CreateBackups = false }));

            await Task.WhenAll(tasks);
        }, iterations: 1, warmup: 0);

        // Assert
        Console.WriteLine($"Sequential time: {sequentialTime}");
        Console.WriteLine($"Parallel time: {parallelTime}");

        var speedup = sequentialTime.AverageTime.TotalSeconds / parallelTime.AverageTime.TotalSeconds;
        Console.WriteLine($"Parallel speedup: {speedup:F2}x");

        // Should achieve reasonable parallel speedup (at least 1.5x on multi-core systems)
        Assert.That(speedup, Is.GreaterThan(1.5), "Parallel execution should be significantly faster");
    }

    [Test]
    [Category("Stress")]
    public async Task Stress_RapidSequentialOperations_ShouldMaintainPerformance()
    {
        // Arrange
        var testFile = CreateTempFile("Test content for stress testing", ".cs");
        var operationTimes = new List<TimeSpan>();

        // Act - Perform many rapid operations
        for (int i = 0; i < 50; i++)
        {
            var stopwatch = Stopwatch.StartNew();

            var result = await _bulkEdit.BulkReplaceAsync(
                new[] { testFile },
                $"content {i % 10}", // Vary the pattern slightly
                $"replaced {i}",
                new BulkEditOptions { CreateBackups = false });

            stopwatch.Stop();
            operationTimes.Add(stopwatch.Elapsed);

            Assert.That(result.Success, Is.True, $"Operation {i} should succeed");
        }

        // Assert
        var averageTime = TimeSpan.FromTicks((long)operationTimes.Average(t => t.Ticks));
        var maxTime = operationTimes.Max();
        var minTime = operationTimes.Min();

        Console.WriteLine($"Average operation time: {averageTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"Min operation time: {minTime.TotalMilliseconds:F2}ms");
        Console.WriteLine($"Max operation time: {maxTime.TotalMilliseconds:F2}ms");

        // Performance should remain consistent
        Assert.That(averageTime.TotalMilliseconds, Is.LessThan(100), "Average operation should be fast");
        Assert.That(maxTime.TotalMilliseconds, Is.LessThan(500), "No operation should be too slow");

        // Variance should be reasonable (within 5x of average to allow for system variations)
        Assert.That(maxTime.TotalMilliseconds, Is.LessThan(averageTime.TotalMilliseconds * 5), "Performance variance should be reasonable");
    }

    [Test]
    [Category("Regression")]
    public async Task Regression_PerformanceComparison_ShouldMaintainBenchmarks()
    {
        // Arrange - Standard benchmark scenario
        var files = Enumerable.Range(0, 100)
            .Select(i => CreateTempFile($"Standard benchmark content {i}", ".cs"))
            .ToList();

        // Act
        var benchmark = await MeasurePerformanceAsync(async () =>
        {
            await _bulkEdit.BulkReplaceAsync(
                files,
                "content",
                "BENCHMARK",
                new BulkEditOptions { CreateBackups = false });
        }, iterations: 5, warmup: 2);

        // Assert - Performance should meet baseline expectations
        Console.WriteLine($"Benchmark performance: {benchmark}");

        // These are baseline expectations that should be maintained
        Assert.That(benchmark.AverageTime.TotalSeconds, Is.LessThan(5), "100-file benchmark should complete within 5 seconds");
        Assert.That(benchmark.StandardDeviation.TotalSeconds, Is.LessThan(1), "Performance should be consistent");

        var filesPerSecond = files.Count / benchmark.AverageTime.TotalSeconds;
        Assert.That(filesPerSecond, Is.GreaterThan(20), "Should process at least 20 files per second");
    }

    [Test]
    [Category("Scaling")]
    [Category("Manual")]
    [Explicit("Performance test - run manually, not reliable on shared CI runners")]
    public async Task Scaling_FileCountScaling_ShouldScaleLinearly()
    {
        // Arrange
        var fileCounts = new[] { 10, 50, 100, 200 };
        var results = new List<(int fileCount, TimeSpan time)>();

        foreach (var fileCount in fileCounts)
        {
            var files = Enumerable.Range(0, fileCount)
                .Select(i => CreateTempFile($"Scaling test content {i}", ".cs"))
                .ToList();

            var time = await MeasurePerformanceAsync(async () =>
            {
                await _bulkEdit.BulkReplaceAsync(
                    files,
                    "content",
                    "SCALED",
                    new BulkEditOptions { CreateBackups = false });
            }, iterations: 3, warmup: 1);

            results.Add((fileCount, time.AverageTime));
            Console.WriteLine($"File count: {fileCount}, Time: {time.AverageTime.TotalMilliseconds:F2}ms");
        }

        // Assert - Scaling should be roughly linear
        var linearRegression = CalculateLinearRegression(results);
        Console.WriteLine($"Scaling factor: {linearRegression.slope:F4} ms/file, R²: {linearRegression.rSquared:F3}");

        // R² should be close to 1 for linear scaling
        Assert.That(linearRegression.rSquared, Is.GreaterThan(0.8), "Scaling should be reasonably linear");

        // Slope should be reasonable (not too steep)
        Assert.That(linearRegression.slope, Is.LessThan(100), "Should process files efficiently");
    }
}

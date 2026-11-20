using System.Diagnostics;
using MCPsharp.Models.BulkEdit;
using NUnit.Framework;

namespace MCPsharp.Tests.Performance;

public partial class PerformanceTests
{
    [Test]
    [Category("Concurrency")]
    public async Task Performance_ConcurrentBulkEdits_ShouldProcessFilesInParallelUpToProcessorCount()
    {
        // Arrange
        const int filesPerEdit = 100;
        var options = new BulkEditOptions { CreateBackups = false };
        var fileSets = Enumerable.Range(0, 4)
            .Select(setIndex => CreateTestFiles(filesPerEdit, i => $"Set{setIndex} File{i} content"))
            .ToList();
        var leastInParallel = Math.Min(2, Environment.ProcessorCount);

        // Act - One bulk edit, then four at once
        var single = await _bulkEdit.BulkReplaceAsync(fileSets[0], "content", "SINGLE", options);
        var concurrent = await Task.WhenAll(fileSets.Select(fileSet =>
            _bulkEdit.BulkReplaceAsync(fileSet, "content|SINGLE", "PARALLEL", options)));

        // Assert - Every edit is applied to all of its files
        Assert.That(single.ModifiedFiles, Is.EqualTo(filesPerEdit), "The bulk edit should edit all of its files");
        Assert.That(concurrent.Select(r => r.ModifiedFiles), Has.All.EqualTo(filesPerEdit), "Each concurrent bulk edit should edit all of its files");
        Assert.That(fileSets.SelectMany(fileSet => fileSet).Select(File.ReadAllText), Has.All.EndsWith("PARALLEL"));

        // The service processes the files of all its bulk edits in parallel, one per processor at most. As one edit of
        // 100 files keeps every processor busy, four edits at once aren't faster than four in a row by any margin a
        // test can rely on; check the parallelism itself, from when each file's processing started and ended.
        var singleInProgress = MostFilesInProgress(single.FileResults);
        var concurrentInProgress = MostFilesInProgress(concurrent.SelectMany(r => r.FileResults));
        Console.WriteLine($"Most files in progress at once: {singleInProgress} in one edit, {concurrentInProgress} in four ({Environment.ProcessorCount} processors)");
        Assert.That(singleInProgress, Is.InRange(leastInParallel, Environment.ProcessorCount), "A bulk edit should process its files in parallel, one per processor at most");
        Assert.That(concurrentInProgress, Is.InRange(leastInParallel, Environment.ProcessorCount), "Concurrent bulk edits should share the limit of one file per processor");
    }

    /// <summary>
    /// The most files whose processing was in progress at the same moment
    /// </summary>
    private static int MostFilesInProgress(IEnumerable<FileBulkEditResult> files)
    {
        // A file that started as another ended did not overlap it, so at the same time ends come before starts
        var events = files
            .SelectMany(file => new[] { (Time: file.ProcessStartTime, Change: 1), (Time: file.ProcessEndTime, Change: -1) })
            .OrderBy(e => e.Time)
            .ThenBy(e => e.Change);

        var inProgress = 0;
        var most = 0;
        foreach (var (_, change) in events)
        {
            inProgress += change;
            most = Math.Max(most, inProgress);
        }

        return most;
    }

    [Test]
    [Category("Stress")]
    public async Task Stress_RapidSequentialOperations_ShouldMaintainPerformance()
    {
        // Arrange
        const int warmupOperations = 5;
        const int measuredOperations = 50;
        var testFile = CreateTempFile("Test content for stress testing", ".cs");
        var operationTimes = new List<double>();

        Task<BulkEditResult> ReplaceAsync(int i) => _bulkEdit.BulkReplaceAsync(
            new[] { testFile },
            $"content {i % 10}", // Vary the pattern slightly
            $"replaced {i}",
            new BulkEditOptions { CreateBackups = false });

        // The first calls pay for JIT compilation and first-use initialisation, so they are not measured
        for (int i = 0; i < warmupOperations; i++)
        {
            Assert.That((await ReplaceAsync(i)).Success, Is.True, $"Warm-up operation {i} should succeed");
        }

        // Act - Perform many rapid operations
        for (int i = 0; i < measuredOperations; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await ReplaceAsync(i);
            stopwatch.Stop();
            operationTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

            Assert.That(result.Success, Is.True, $"Operation {i} should succeed");
        }

        // Assert
        operationTimes.Sort();
        var median = Percentile(operationTimes, 0.5);
        var p95 = Percentile(operationTimes, 0.95);
        var max = operationTimes[^1];

        Console.WriteLine($"Median operation time: {median:F2}ms");
        Console.WriteLine($"95th percentile operation time: {p95:F2}ms");
        Console.WriteLine($"Min operation time: {operationTimes[0]:F2}ms");
        Console.WriteLine($"Max operation time: {max:F2}ms");

        // The median and 95th percentile ignore a couple of stalls (a GC pause, a descheduled thread),
        // which the mean and maximum don't, while a slowdown of more than a few operations still fails
        Assert.That(median, Is.LessThan(100), "A typical operation should be fast");
        Assert.That(max, Is.LessThan(500), "No operation should be too slow");
        Assert.That(p95, Is.LessThan(median * 5), "Nearly all operations should take about as long as a typical one");
    }

    /// <summary>
    /// Nearest-rank percentile of values sorted in ascending order
    /// </summary>
    private static double Percentile(List<double> sortedValues, double fraction) =>
        sortedValues[Math.Max(0, (int)Math.Ceiling(fraction * sortedValues.Count) - 1)];

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

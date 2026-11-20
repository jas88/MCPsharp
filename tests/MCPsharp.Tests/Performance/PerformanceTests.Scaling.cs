using System.Diagnostics;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Tests.Services.BulkEdit;
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

        // Act - One bulk edit, one capped at two files at once, then four at once
        var single = await _bulkEdit.BulkReplaceAsync(fileSets[0], "content", "SINGLE", options);
        var capped = await _bulkEdit.BulkReplaceAsync(fileSets[1], "content", "CAPPED", new BulkEditOptions { CreateBackups = false, MaxParallelism = 2 });
        var concurrent = await Task.WhenAll(fileSets.Select(fileSet =>
            _bulkEdit.BulkReplaceAsync(fileSet, "content|SINGLE|CAPPED", "PARALLEL", options)));

        // Assert - Every edit is applied to all of its files
        Assert.That(single.ModifiedFiles, Is.EqualTo(filesPerEdit), "The bulk edit should edit all of its files");
        Assert.That(capped.ModifiedFiles, Is.EqualTo(filesPerEdit), "The capped bulk edit should edit all of its files");
        Assert.That(concurrent.Select(r => r.ModifiedFiles), Has.All.EqualTo(filesPerEdit), "Each concurrent bulk edit should edit all of its files");
        Assert.That(fileSets.SelectMany(fileSet => fileSet).Select(File.ReadAllText), Has.All.EndsWith("PARALLEL"));

        // The service processes the files of all its bulk edits in parallel, one per processor at most. As one edit of
        // 100 files keeps every processor busy, four edits at once aren't faster than four in a row by any margin a
        // test can rely on; check the parallelism itself, from when each file's processing started and ended.
        var singleInProgress = BulkEditConcurrency.MostFilesInProgress(single.FileResults);
        var cappedInProgress = BulkEditConcurrency.MostFilesInProgress(capped.FileResults);
        var concurrentInProgress = BulkEditConcurrency.MostFilesInProgress(concurrent.SelectMany(r => r.FileResults));
        Console.WriteLine($"Most files in progress at once: {singleInProgress} in one edit, {cappedInProgress} in one capped at 2, {concurrentInProgress} in four ({Environment.ProcessorCount} processors)");
        Assert.That(singleInProgress, Is.InRange(leastInParallel, Environment.ProcessorCount), "A bulk edit should process its files in parallel, one per processor at most");
        Assert.That(cappedInProgress, Is.InRange(leastInParallel, Math.Min(2, Environment.ProcessorCount)), "A bulk edit with MaxParallelism 2 should process two files at once at most");
        Assert.That(concurrentInProgress, Is.InRange(leastInParallel, Environment.ProcessorCount), "Concurrent bulk edits should share the limit of one file per processor");
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
    public async Task Scaling_FileCountScaling_ShouldScaleLinearly()
    {
        // Arrange
        const int warmupRounds = 2;
        const int measuredRounds = 13;
        var fileCounts = new[] { 50, 100, 200, 400 };
        var fileSets = fileCounts.Select(count => CreateTestFiles(count, i => $"Scaling test content {i}")).ToList();
        var times = fileCounts.Select(_ => new List<TimeSpan>()).ToList();
        var options = new BulkEditOptions { CreateBackups = false };

        // Act - Each round edits every set once, alternately replacing the word and putting it back, so every run
        // reads, edits and writes every file, and a stall slows one run of each count rather than every run of one
        for (var round = 0; round < warmupRounds + measuredRounds; round++)
        {
            var (pattern, replacement) = round % 2 == 0 ? ("content", "SCALED") : ("SCALED", "content");
            for (var index = 0; index < fileSets.Count; index++)
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await _bulkEdit.BulkReplaceAsync(fileSets[index], pattern, replacement, options);
                stopwatch.Stop();

                Assert.That(result.ModifiedFiles, Is.EqualTo(fileCounts[index]), $"Round {round} should edit all {fileCounts[index]} files");
                if (round >= warmupRounds)
                {
                    times[index].Add(stopwatch.Elapsed);
                }
            }
        }

        // The fastest run of each count is the one least slowed by anything else running
        var results = fileCounts.Select((fileCount, index) => (fileCount, time: times[index].Min())).ToList();
        foreach (var (fileCount, time) in results)
        {
            Console.WriteLine($"File count: {fileCount}, Time: {time.TotalMilliseconds:F2}ms");
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

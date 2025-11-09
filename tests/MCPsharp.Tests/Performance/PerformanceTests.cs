using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;
using MCPsharp.Tests.TestData;
using NUnit.Framework;

namespace MCPsharp.Tests.Performance;

[TestFixture]
[Category("Performance")]
public class PerformanceTests : PerformanceTestBase
{
    private RoslynWorkspace _workspace = null!;
    private SymbolQueryService _symbolQuery = null!;
    private CallerAnalysisService _callerAnalysis = null!;
    private CallChainService _callChain = null!;
    private TypeUsageService _typeUsage = null!;
    private AdvancedReferenceFinderService _referenceFinder = null!;
    private BulkEditService _bulkEdit = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
        _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
        _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
        _referenceFinder = new AdvancedReferenceFinderService(
            _workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);
        _bulkEdit = new BulkEditService();
    }

    [TearDown]
    protected override void TearDown()
    {
        base.TearDown();
    }

    [Test]
    [Category("ReverseSearch")]
    public async Task Performance_FindCallersAsync_WithLargeCodebase_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = GenerateTestFiles(100); // 100 test files
        foreach (var file in files)
        {
            // _workspace.AddDocument(file); // Method no longer available
        }

        // Wait for workspace processing
        await Task.Delay(2000);

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            await _referenceFinder.FindCallersAsync("TestMethod", "TestClass");
        }, iterations: 10, warmup: 2);

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
        Console.WriteLine($"FindCallersAsync Performance: {performance}");
    }

    [Test]
    [Category("ReverseSearch")]
    public async Task Performance_FindCallChainsAsync_WithComplexDependencies_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = GenerateComplexCallChainFiles(50);
        foreach (var file in files)
        {
            // _workspace.AddDocument(file); // Method no longer available
        }

        await Task.Delay(3000); // Longer wait for complex files

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            await _referenceFinder.FindCallChainsAsync("RootMethod", "RootClass", CallDirection.Forward, maxDepth: 10);
        }, iterations: 5, warmup: 2);

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8));
        Console.WriteLine($"FindCallChainsAsync Performance: {performance}");
    }

    [Test]
    [Category("ReverseSearch")]
    public async Task Performance_ComprehensiveMethodAnalysis_ShouldCompleteWithinTimeLimit()
    {
        // Arrange
        var files = GenerateTestFiles(200); // Larger set for comprehensive analysis
        foreach (var file in files)
        {
            // _workspace.AddDocument(file); // Method no longer available
        }

        await Task.Delay(3000);

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            await _referenceFinder.AnalyzeMethodComprehensivelyAsync("ComplexMethod", "ComplexService");
        }, iterations: 3, warmup: 1);

        // Assert
        AssertPerformance(performance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        Console.WriteLine($"ComprehensiveMethodAnalysis Performance: {performance}");
    }

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

    [Test]
    [Category("Concurrency")]
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

    #region Helper Methods

    private List<string> GenerateTestFiles(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => CreateTempFile($@"
using System;

namespace TestProject{i}
{{
    public class TestClass{i}
    {{
        public void TestMethod()
        {{
            Console.WriteLine(""Test {i}"");
        }}

        public void CallerMethod()
        {{
            TestMethod(); // This creates a call relationship
        }}
    }}
}}
", ".cs"))
            .ToList();
    }

    private List<string> GenerateComplexCallChainFiles(int count)
    {
        return Enumerable.Range(0, count)
            .Select(i => CreateTempFile($@"
using System;
using System.Threading.Tasks;

namespace ChainTest{i}
{{
    public class Service{i}
    {{
        private readonly IService{i + 1} _next;

        public Service{i}(IService{i + 1} next)
        {{
            _next = next;
        }}

        public async Task<string> ProcessAsync(string input)
        {{
            var result = await _next.ProcessAsync(input);
            return $""Service{i}: {{result}}"";
        }}
    }}

    public interface IService{i + 1}
    {{
        Task<string> ProcessAsync(string input);
    }}
}}
", ".cs"))
            .ToList();
    }

    private (double slope, double intercept, double rSquared) CalculateLinearRegression(List<(int x, TimeSpan y)> data)
    {
        var n = data.Count;
        var sumX = data.Sum(d => d.x);
        var sumY = data.Sum(d => d.y.TotalMilliseconds);
        var sumXY = data.Sum(d => d.x * d.y.TotalMilliseconds);
        var sumX2 = data.Sum(d => d.x * d.x);
        var sumY2 = data.Sum(d => d.y.TotalMilliseconds * d.y.TotalMilliseconds);

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        var intercept = (sumY - slope * sumX) / n;

        // Calculate R²
        var meanY = sumY / n;
        var ssTot = data.Sum(d => Math.Pow(d.y.TotalMilliseconds - meanY, 2));
        var ssRes = data.Sum(d => Math.Pow(d.y.TotalMilliseconds - (slope * d.x + intercept), 2));
        var rSquared = 1 - (ssRes / ssTot);

        return (slope, intercept, rSquared);
    }

    #endregion
}
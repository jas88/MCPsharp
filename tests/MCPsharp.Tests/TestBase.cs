using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests;

/// <summary>
/// Base test class providing common functionality for all test classes
/// </summary>
[TestFixture]
public abstract class TestBase
{
    protected ILogger<T> CreateNullLogger<T>() => NullLogger<T>.Instance;
    protected ILogger CreateNullLogger() => NullLogger.Instance;
    protected ILoggerFactory CreateNullLoggerFactory() => NullLoggerFactory.Instance;

    /// <summary>
    /// Creates a temporary directory for test operations
    /// </summary>
    /// <returns>Path to temporary directory</returns>
    protected string CreateTempDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "MCPsharp_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);
        return tempPath;
    }

    /// <summary>
    /// Creates a temporary file with the specified content
    /// </summary>
    /// <param name="content">File content</param>
    /// <param name="extension">File extension (including dot)</param>
    /// <param name="directory">Directory to create file in (optional)</param>
    /// <returns>Path to temporary file</returns>
    protected string CreateTempFile(string content, string extension = ".cs", string? directory = null)
    {
        var dir = directory ?? CreateTempDirectory();
        var filePath = Path.Combine(dir, $"{Guid.NewGuid()}{extension}");
        File.WriteAllText(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Cleans up a directory recursively
    /// </summary>
    /// <param name="path">Directory path to clean up</param>
    protected void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Cleans up a file
    /// </summary>
    /// <param name="path">File path to clean up</param>
    protected void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [SetUp]
    protected virtual void Setup()
    {
        // Common setup for all tests
    }

    [TearDown]
    protected virtual void TearDown()
    {
        // Common cleanup for all tests
    }
}

/// <summary>
/// Base test class for services that need file system operations
/// </summary>
public abstract class FileServiceTestBase : TestBase
{
    protected string TempDirectory { get; private set; } = string.Empty;
    protected List<string> FilesToCleanup { get; } = new();
    protected List<string> DirectoriesToCleanup { get; } = new();

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        TempDirectory = CreateTempDirectory();
        DirectoriesToCleanup.Add(TempDirectory);
    }

    [TearDown]
    protected override void TearDown()
    {
        // Clean up all files
        foreach (var file in FilesToCleanup)
        {
            CleanupFile(file);
        }
        FilesToCleanup.Clear();

        // Clean up all directories in reverse order (nested directories first)
        foreach (var dir in DirectoriesToCleanup.AsEnumerable().Reverse())
        {
            CleanupDirectory(dir);
        }
        DirectoriesToCleanup.Clear();

        base.TearDown();
    }

    /// <summary>
    /// Creates a test file and tracks it for cleanup
    /// </summary>
    protected string CreateTestFile(string content, string extension = ".cs", string? subdirectory = null)
    {
        var dir = string.IsNullOrEmpty(subdirectory) ? TempDirectory : Path.Combine(TempDirectory, subdirectory);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            DirectoriesToCleanup.Add(dir);
        }

        var filePath = CreateTempFile(content, extension, dir);
        FilesToCleanup.Add(filePath);
        return filePath;
    }

    /// <summary>
    /// Creates a test directory and tracks it for cleanup
    /// </summary>
    protected string CreateTestDirectory(string name)
    {
        var dir = Path.Combine(TempDirectory, name);
        Directory.CreateDirectory(dir);
        DirectoriesToCleanup.Add(dir);
        return dir;
    }
}

/// <summary>
/// Base test class for performance tests
/// </summary>
public abstract class PerformanceTestBase : TestBase
{
    private const int DefaultWarmupIterations = 3;
    private const int DefaultMeasurementIterations = 10;

    /// <summary>
    /// Measures the execution time of an operation
    /// </summary>
    /// <param name="action">Action to measure</param>
    /// <param name="iterations">Number of iterations to run</param>
    /// <param name="warmup">Number of warmup iterations</param>
    /// <returns>Execution time statistics</returns>
    protected PerformanceResult MeasurePerformance(Action action, int iterations = DefaultMeasurementIterations, int warmup = DefaultWarmupIterations)
    {
        // Warmup
        for (int i = 0; i < warmup; i++)
        {
            action();
        }

        var times = new List<TimeSpan>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed);
        }

        return new PerformanceResult(times);
    }

    /// <summary>
    /// Measures the execution time of an async operation
    /// </summary>
    /// <param name="action">Async action to measure</param>
    /// <param name="iterations">Number of iterations to run</param>
    /// <param name="warmup">Number of warmup iterations</param>
    /// <returns>Execution time statistics</returns>
    protected async Task<PerformanceResult> MeasurePerformanceAsync(Func<Task> action, int iterations = DefaultMeasurementIterations, int warmup = DefaultWarmupIterations)
    {
        // Warmup
        for (int i = 0; i < warmup; i++)
        {
            await action();
        }

        var times = new List<TimeSpan>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await action();
            stopwatch.Stop();
            times.Add(stopwatch.Elapsed);
        }

        return new PerformanceResult(times);
    }

    /// <summary>
    /// Asserts that performance meets the specified criteria
    /// </summary>
    /// <param name="result">Performance result to validate</param>
    /// <param name="maxAverageTime">Maximum allowed average time</param>
    /// <param name="maxTime">Maximum allowed time for any iteration</param>
    protected void AssertPerformance(PerformanceResult result, TimeSpan maxAverageTime, TimeSpan? maxTime = null)
    {
        Assert.That(result.AverageTime, Is.LessThanOrEqualTo(maxAverageTime),
            $"Average time {result.AverageTime.TotalMilliseconds:F2}ms exceeds maximum {maxAverageTime.TotalMilliseconds:F2}ms");

        if (maxTime.HasValue)
        {
            Assert.That(result.MaxTime, Is.LessThanOrEqualTo(maxTime.Value),
                $"Maximum time {result.MaxTime.TotalMilliseconds:F2}ms exceeds limit {maxTime.Value.TotalMilliseconds:F2}ms");
        }
    }
}

/// <summary>
/// Result of a performance measurement
/// </summary>
public record PerformanceResult
{
    public TimeSpan AverageTime { get; }
    public TimeSpan MinTime { get; }
    public TimeSpan MaxTime { get; }
    public TimeSpan StandardDeviation { get; }
    public int Iterations { get; }

    public PerformanceResult(List<TimeSpan> times)
    {
        if (times == null || times.Count == 0)
            throw new ArgumentException("Times list cannot be null or empty", nameof(times));

        Iterations = times.Count;
        AverageTime = TimeSpan.FromTicks((long)times.Average(t => t.Ticks));
        MinTime = times.Min();
        MaxTime = times.Max();

        var variance = times.Average(t => Math.Pow(t.Ticks - AverageTime.Ticks, 2));
        StandardDeviation = TimeSpan.FromTicks((long)Math.Sqrt(variance));
    }

    public override string ToString()
    {
        return $"Average: {AverageTime.TotalMilliseconds:F2}ms, Min: {MinTime.TotalMilliseconds:F2}ms, Max: {MaxTime.TotalMilliseconds:F2}ms, StdDev: {StandardDeviation.TotalMilliseconds:F2}ms ({Iterations} iterations)";
    }
}

/// <summary>
/// Base test class for integration tests
/// </summary>
public abstract class IntegrationTestBase : FileServiceTestBase
{
    /// <summary>
    /// Sets up a realistic test environment with multiple files and directories
    /// </summary>
    protected virtual void SetupRealisticEnvironment()
    {
        // Create typical project structure
        CreateTestDirectory("src");
        CreateTestDirectory("tests");
        CreateTestDirectory("docs");
        CreateTestDirectory("config");

        // Create sample files
        CreateTestFile("using System;\n\npublic class SampleClass\n{\n    public void SampleMethod() { }\n}", ".cs", "src");
        CreateTestFile("{\"Setting\": \"Value\"}", ".json", "config");
        CreateTestFile("# Test Documentation\n\nThis is a test.", ".md", "docs");
    }
}
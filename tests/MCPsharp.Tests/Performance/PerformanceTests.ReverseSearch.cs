using MCPsharp.Models.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Performance;

/// <summary>
/// Reverse search over a real workspace: generated sources in a folder, which MCPsharp loads as an adhoc project.
/// Each timed run checks what it found, so it times a search that did its work, and the limits apply to the fastest
/// run, which anything else running slowed least, with the slowest only checked for a hang.
/// </summary>
public partial class PerformanceTests
{
    [Test]
    [Category("ReverseSearch")]
    public async Task Performance_FindCallersAsync_WithLargeCodebase_ShouldCompleteWithinTimeLimit()
    {
        // Arrange - TestClass.TestMethod, called once from each of 100 files
        const int callerCount = 100;
        await LoadWorkspaceAsync(CallerSources(callerCount));

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var callers = await _referenceFinder.FindCallersAsync("TestMethod", "TestClass");
            Assert.That(callers?.TotalCallers, Is.EqualTo(callerCount), "Every caller should be found");
        }, iterations: 10, warmup: 2);

        // Assert
        Console.WriteLine($"FindCallersAsync Performance: {performance}");
        AssertFastestRunWithin(performance, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
    }

    [Test]
    [Category("ReverseSearch")]
    public async Task Performance_FindCallChainsAsync_WithComplexDependencies_ShouldCompleteWithinTimeLimit()
    {
        // Arrange - RootClass.RootMethod calls the first of 50 linked classes, each calling the next and a logger
        const int maxDepth = 10;
        await LoadWorkspaceAsync(CallChainSources(50));

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var chains = await _referenceFinder.FindCallChainsAsync("RootMethod", "RootClass", CallDirection.Forward, maxDepth);
            Assert.That(chains?.Paths, Is.Not.Empty, "The calls from RootMethod should be followed");
            Assert.That(chains!.Paths.Max(path => path.Length), Is.EqualTo(maxDepth), "The chain of links should be followed to the depth limit");
        }, iterations: 5, warmup: 2);

        // Assert
        Console.WriteLine($"FindCallChainsAsync Performance: {performance}");
        AssertFastestRunWithin(performance, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8));
    }

    [Test]
    [Category("ReverseSearch")]
    public async Task Performance_ComprehensiveMethodAnalysis_ShouldCompleteWithinTimeLimit()
    {
        // Arrange - ComplexService.ComplexMethod, which calls helpers, called once from each of 200 files
        const int clientCount = 200;
        await LoadWorkspaceAsync(ServiceSources(clientCount));

        // Act
        var performance = await MeasurePerformanceAsync(async () =>
        {
            var analysis = await _referenceFinder.AnalyzeMethodComprehensivelyAsync("ComplexMethod", "ComplexService");
            Assert.That(analysis.Callers?.TotalCallers, Is.EqualTo(clientCount), "Every caller should be found");
            Assert.That(analysis.TotalBackwardPaths, Is.EqualTo(clientCount), "Each caller's chain to ComplexMethod should be found");
            Assert.That(analysis.TotalForwardPaths, Is.Positive, "The calls from ComplexMethod should be followed");
        }, iterations: 3, warmup: 1);

        // Assert
        Console.WriteLine($"ComprehensiveMethodAnalysis Performance: {performance}");
        AssertFastestRunWithin(performance, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// Write <paramref name="sources"/> into a folder, deleted after the test, and load the workspace from it
    /// </summary>
    private async Task LoadWorkspaceAsync(List<(string FileName, string Source)> sources)
    {
        var directory = CreateTempDirectory();
        foreach (var (fileName, source) in sources)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, fileName), source);
        }

        await _workspace.InitializeAsync(directory);
        Assert.That(_workspace.GetAllDocuments(), Has.Count.EqualTo(sources.Count), "Every source should be in the workspace");
    }

    /// <summary>
    /// TestClass.TestMethod, and <paramref name="callerCount"/> files whose classes each call it once
    /// </summary>
    private static List<(string FileName, string Source)> CallerSources(int callerCount)
    {
        var sources = new List<(string, string)>
        {
            ("TestClass.cs", """
                namespace Target;

                public class TestClass
                {
                    public void TestMethod()
                    {
                    }
                }
                """)
        };

        sources.AddRange(Enumerable.Range(0, callerCount).Select(i => ($"Caller{i}.cs", $$"""
            namespace Callers;

            public class Caller{{i}}
            {
                public void Call(Target.TestClass target) => target.TestMethod();
            }
            """)));
        return sources;
    }

    /// <summary>
    /// RootClass.RootMethod, calling Link0.Step, where each of <paramref name="linkCount"/> links calls the next
    /// link's Step and Logger.Log
    /// </summary>
    private static List<(string FileName, string Source)> CallChainSources(int linkCount)
    {
        var sources = new List<(string, string)>
        {
            ("RootClass.cs", """
                namespace Chain;

                public class RootClass
                {
                    public void RootMethod() => new Link0().Step();
                }
                """),
            ("Logger.cs", """
                namespace Chain;

                public static class Logger
                {
                    public static void Log(string message)
                    {
                    }
                }
                """)
        };

        sources.AddRange(Enumerable.Range(0, linkCount).Select(i => ($"Link{i}.cs", $$"""
            namespace Chain;

            public class Link{{i}}
            {
                public void Step()
                {
                    Logger.Log("Link{{i}}");
                    {{(i + 1 < linkCount ? $"new Link{i + 1}().Step();" : string.Empty)}}
                }
            }
            """)));
        return sources;
    }

    /// <summary>
    /// ComplexService.ComplexMethod, calling Helper's methods, and <paramref name="clientCount"/> files whose classes
    /// each call ComplexMethod once
    /// </summary>
    private static List<(string FileName, string Source)> ServiceSources(int clientCount)
    {
        var sources = new List<(string, string)>
        {
            ("ComplexService.cs", """
                namespace Services;

                public class ComplexService
                {
                    private readonly Helper _helper = new();

                    public int ComplexMethod(int value)
                    {
                        var prepared = _helper.Prepare(value);
                        return _helper.Finish(prepared);
                    }
                }

                public class Helper
                {
                    public int Prepare(int value) => value * 2;

                    public int Finish(int value) => Normalize(value) + 1;

                    private static int Normalize(int value) => value < 0 ? -value : value;
                }
                """)
        };

        sources.AddRange(Enumerable.Range(0, clientCount).Select(i => ($"Client{i}.cs", $$"""
            namespace Clients;

            public class Client{{i}}
            {
                public int Use(Services.ComplexService service) => service.ComplexMethod({{i}});
            }
            """)));
        return sources;
    }
}

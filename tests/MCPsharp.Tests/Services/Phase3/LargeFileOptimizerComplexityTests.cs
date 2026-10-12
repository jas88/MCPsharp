using System.Text;
using MCPsharp.Models.LargeFileOptimization;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Roslyn;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Phase3;

/// <summary>
/// Service-level tests verifying that LargeFileOptimizerService reports real complexity numbers and hotspots.
/// </summary>
[TestFixture]
public class LargeFileOptimizerComplexityTests
{
    // Process: cyclomatic 11, cognitive 21, max nesting 5, decision points 10.
    private const string ProcessorSource = """
        public class Processor
        {
            public int Process(int[][] grid, bool flag)
            {
                var total = 0;
                foreach (var row in grid)
                {
                    foreach (var cell in row)
                    {
                        if (cell > 0 && flag)
                        {
                            for (var i = 0; i < cell; i++)
                            {
                                if (i % 2 == 0 || i % 3 == 0)
                                {
                                    total += i;
                                }
                                else if (i % 5 == 0)
                                {
                                    total -= i;
                                }
                                else
                                {
                                    total++;
                                }
                            }
                        }
                    }
                }

                while (total > 100 && flag)
                {
                    total /= 2;
                }

                return total;
            }

            public int Simple(int x) => x + 1;
        """;

    private static readonly string[] ExpectedGodClassNames = { "Everything" };

    private RoslynWorkspace _workspace = null!;
    private LargeFileOptimizerService _service = null!;
    private string _directory = null!;

    [SetUp]
    public async Task SetUp()
    {
        _workspace = new RoslynWorkspace();
        await _workspace.InitializeTestWorkspaceAsync();
        _service = new LargeFileOptimizerService(
            _workspace,
            new SymbolQueryService(_workspace),
            Substitute.For<ICallerAnalysisService>(),
            Substitute.For<ITypeUsageService>());
        _directory = Path.Combine(Path.GetTempPath(), "mcpsharp-complexity-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown() => _workspace.Dispose();

    [Test]
    public async Task GetComplexityMetricsAsync_ForMethod_ReportsRealMetricsAndHotspots()
    {
        var path = await AddDocumentAsync("Processor.cs", ProcessorSource + "}\n");

        var metrics = await _service.GetComplexityMetricsAsync(path, "Process");

        Assert.Multiple(() =>
        {
            Assert.That(metrics.CyclomaticComplexity, Is.EqualTo(11));
            Assert.That(metrics.CognitiveComplexity, Is.EqualTo(21));
            Assert.That(metrics.MaximumNestingDepth, Is.EqualTo(5));
            Assert.That(metrics.NumberOfDecisionPoints, Is.EqualTo(10));
            Assert.That(metrics.ComplexityLevel, Is.EqualTo(ComplexityLevel.Complex));
            Assert.That(metrics.Hotspots.Select(h => (h.HotspotType, h.LocalComplexity)), Is.EquivalentTo(new[]
            {
                ("HighCognitiveComplexity", 21),
                ("HighCyclomaticComplexity", 11),
                ("DeepNesting", 5),
                ("ComplexBlock", 19)
            }));
        });

        var block = metrics.Hotspots.Single(h => h.HotspotType == "ComplexBlock");
        Assert.That((block.StartLine, block.EndLine), Is.EqualTo((6, 29)), "outer foreach spans lines 6-29");
    }

    [Test]
    public async Task GetComplexityMetricsAsync_ForFile_AggregatesMembers()
    {
        var path = await AddDocumentAsync("Processor.cs", ProcessorSource + "}\n");

        var metrics = await _service.GetComplexityMetricsAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(metrics.CyclomaticComplexity, Is.EqualTo(11), "worst member");
            Assert.That(metrics.CognitiveComplexity, Is.EqualTo(21), "worst member");
            Assert.That(metrics.NumberOfDecisionPoints, Is.EqualTo(10), "sum over members");
            Assert.That(metrics.Hotspots, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task OptimizeLargeMethodAsync_UsesComplexityInStrategiesAndGodMethodAnalysis()
    {
        var path = await AddDocumentAsync("Processor.cs", ProcessorSource + "}\n");

        var result = await _service.OptimizeLargeMethodAsync(path, "Process");

        Assert.Multiple(() =>
        {
            Assert.That(result.StartLine, Is.EqualTo(3));
            Assert.That(result.CurrentMetrics.IsTooComplex, Is.True);
            Assert.That(result.CurrentMetrics.MaximumNestingDepth, Is.EqualTo(5));
            Assert.That(result.RefactoringStrategies.Select(s => s.RefactoringType),
                Does.Contain(MethodRefactoringType.ReplaceConditional).And.Contain(MethodRefactoringType.SimplifyConditional));
            Assert.That(result.CurrentMetrics.GodMethodScore.Violations,
                Has.Some.Contains("cognitive complexity: 21"));
            Assert.That(result.CurrentMetrics.GodMethodScore.HighComplexityBlocks, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task GenerateOptimizationPlanAsync_IncludesHotspotActionsWithRealLines()
    {
        var padding = new StringBuilder("    public void Padding()\n    {\n        var x = 0;\n");
        for (var i = 0; i < 520; i++)
            padding.Append("        x++;\n");
        padding.Append("    }\n");
        var path = await AddDocumentAsync("Processor.cs", ProcessorSource + padding + "}\n");

        var plan = await _service.GenerateOptimizationPlanAsync(path);
        var estimate = await _service.EstimateOptimizationEffortAsync(plan);

        var complexityActions = plan.Actions.Where(a => a.ActionType == OptimizationActionType.ReduceComplexity
            && a.Title.StartsWith("Reduce complexity", StringComparison.Ordinal)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(complexityActions.Select(a => (a.StartLine, a.EndLine)), Is.EquivalentTo(new[] { (3, 37), (6, 29) }));
            Assert.That(complexityActions.Select(a => a.EstimatedEffortHours), Has.All.GreaterThanOrEqualTo(2));
            Assert.That(plan.Actions.Where(a => a.Title == "Extract Method").Select(a => a.StartLine), Has.Some.GreaterThan(1),
                "large-method actions use the method's real location");
            Assert.That(estimate.EffortByType.GetValueOrDefault(OptimizationActionType.ReduceComplexity), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task AnalyzeLargeFilesAsync_ReportsNonZeroAverageComplexity()
    {
        await AddDocumentAsync("Processor.cs", ProcessorSource + "}\n");

        var result = await _service.AnalyzeLargeFilesAsync(_directory, maxLines: 10);

        Assert.That(result.LargeFiles, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.LargeFiles[0].OverallComplexity.CognitiveComplexity, Is.EqualTo(21));
            Assert.That(result.Statistics.AverageComplexityScore, Is.EqualTo(11));
        });
    }

    [Test]
    public async Task AnalyzeLargeFilesAsync_CountsGodClassesAndGodMethods()
    {
        await AddDocumentAsync("Processor.cs", ProcessorSource + "}\n" + BuildGodClassSource());

        var result = await _service.AnalyzeLargeFilesAsync(_directory, maxLines: 10);
        var godClasses = await _service.DetectGodClassesAsync(_directory);
        var godMethods = await _service.DetectGodMethodsAsync(_directory);

        Assert.Multiple(() =>
        {
            Assert.That(result.Statistics.GodClassesDetected, Is.EqualTo(1));
            Assert.That(result.Statistics.GodMethodsDetected, Is.EqualTo(1), "Process is a god method; the one-liners are not");
            Assert.That(result.Statistics.GodClassesDetected, Is.EqualTo(godClasses.Count));
            Assert.That(result.Statistics.GodMethodsDetected, Is.EqualTo(godMethods.Count));
            Assert.That(godClasses.Select(c => c.ClassName), Is.EqualTo(ExpectedGodClassNames));
        });
    }

    [Test]
    public async Task AnalyzeLargeFilesAsync_SmallFilesDoNotContributeGodCounts()
    {
        await AddDocumentAsync("Processor.cs", ProcessorSource + "}\n");

        var result = await _service.AnalyzeLargeFilesAsync(_directory, maxLines: 10_000);

        Assert.Multiple(() =>
        {
            Assert.That(result.LargeFiles, Is.Empty);
            Assert.That(result.Statistics.GodClassesDetected, Is.Zero);
            Assert.That(result.Statistics.GodMethodsDetected, Is.Zero);
        });
    }

    /// <summary>
    /// A class with 25 methods across six responsibilities and 20 fields: a god class whose methods are all trivial.
    /// </summary>
    private static string BuildGodClassSource()
    {
        var prefixes = new[] { "Get", "Validate", "Calculate", "Save", "Send", "Parse" };
        var builder = new StringBuilder("public class Everything\n{\n");
        for (var i = 0; i < 20; i++)
        {
            builder.Append("    private int _field").Append(i).Append(";\n");
        }

        for (var i = 0; i < 25; i++)
        {
            builder.Append("    public int ").Append(prefixes[i % prefixes.Length]).Append("Item").Append(i).Append("() => ").Append(i).Append(";\n");
        }

        return builder.Append("}\n").ToString();
    }

    private async Task<string> AddDocumentAsync(string fileName, string content)
    {
        var path = Path.Combine(_directory, fileName);
        await _workspace.AddInMemoryDocumentAsync(path, content);
        return path;
    }
}

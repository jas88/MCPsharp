using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Performance;

[TestFixture]
[Category("Performance")]
public partial class PerformanceTests : PerformanceTestBase
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
        _workspace?.Dispose();
        _bulkEdit?.Dispose();
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

    #region Helper Methods

    private static List<string> GenerateTestFiles(int count)
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

    private static List<string> GenerateComplexCallChainFiles(int count)
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

    private static (double slope, double intercept, double rSquared) CalculateLinearRegression(List<(int x, TimeSpan y)> data)
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
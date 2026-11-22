using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
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

    #region Helper Methods

    /// <summary>
    /// Create C# files in one directory, which is deleted after the test
    /// </summary>
    private List<string> CreateTestFiles(int count, Func<int, string> content)
    {
        var directory = CreateTempDirectory();
        return Enumerable.Range(0, count)
            .Select(i => CreateTempFile(content(i), ".cs", directory))
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

using MCPsharp.Services.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Integration tests for AdvancedReferenceFinderService
/// </summary>
[TestFixture]
public partial class AdvancedReferenceFinderServiceIntegrationTests
{
    private RoslynWorkspace _workspace;
    private SymbolQueryService _symbolQuery;
    private CallerAnalysisService _callerAnalysis;
    private CallChainService _callChain;
    private TypeUsageService _typeUsage;
    private AdvancedReferenceFinderService _advancedReferenceFinder;

    [SetUp]
    public void SetUp()
    {
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
        _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
        _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
        _advancedReferenceFinder = new AdvancedReferenceFinderService(_workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);

        // Initialize workspace with test fixtures
        InitializeWorkspace().Wait();
    }

    private async Task InitializeWorkspace()
    {
        await _workspace.InitializeTestWorkspaceAsync();

        var testFiles = new[]
        {
            ("IService.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public interface IService
{
    void Execute();
    string GetData();
}"),
            ("Service.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Service : IService
{
    public void Execute()
    {
        Process();
    }

    public string GetData()
    {
        return ""data"";
    }

    private void Process()
    {
        // Internal processing
    }
}"),
            ("Manager.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Manager
{
    private readonly Service _service;

    public Manager(Service service)
    {
        _service = service;
    }

    public void Run()
    {
        _service.Execute();
        var data = _service.GetData();
    }
}"),
            ("RecursiveService.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class RecursiveService
{
    public void RecursiveMethod(int depth)
    {
        if (depth > 0)
        {
            RecursiveMethod(depth - 1);
        }
    }

    public void IndirectRecursive()
    {
        HelperMethod();
    }

    private void HelperMethod()
    {
        IndirectRecursive(); // Creates cycle
    }
}")
        };

        // Add files to workspace using in-memory document method
        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine("TestFixtures", fileName);
            await _workspace.AddInMemoryDocumentAsync(filePath, content);
        }

        // Ensure compilation is ready
        if (_workspace.GetCompilations().Count == 0)
        {
            throw new InvalidOperationException("Failed to create compilation for test workspace");
        }
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
    }
}
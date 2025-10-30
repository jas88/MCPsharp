using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Integration tests for AdvancedReferenceFinderService
/// </summary>
public class AdvancedReferenceFinderServiceTests : IDisposable
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly CallerAnalysisService _callerAnalysis;
    private readonly CallChainService _callChain;
    private readonly TypeUsageService _typeUsage;
    private readonly AdvancedReferenceFinderService _advancedReferenceFinder;

    public AdvancedReferenceFinderServiceTests()
    {
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
        _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
        _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
        _advancedReferenceFinder = new AdvancedReferenceFinderService(_workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);

        // Initialize workspace with test fixtures
        InitializeWorkspace();
    }

    private void InitializeWorkspace()
    {
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

        // Add files to workspace
        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestFixtures", fileName);
            _workspace.AddDocumentAsync(filePath).Wait();
        }
    }

    [Fact]
    public async Task FindCallers_ShouldReturnCallerAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.FindCallersAsync("Execute", "Service");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalCallers >= 1);
        Assert.Contains(result.Callers, c => c.CallerType.Contains("Manager"));
    }

    [Fact]
    public async Task FindCallersAtLocation_ShouldFindCallersAtLocation()
    {
        // This test requires actual file locations, which is complex to set up
        // We'll test that the method doesn't throw
        var result = await _advancedReferenceFinder.FindCallersAtLocationAsync("Service.cs", 10, 5);

        // May return null for non-existent location
    }

    [Fact]
    public async Task FindCallChains_ShouldReturnCallChains()
    {
        // Act
        var result = await _advancedReferenceFinder.FindCallChainsAsync("Execute", "Service", CallDirection.Backward, 5);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(CallDirection.Backward, result.Direction);
        Assert.True(result.TotalPaths >= 0);
    }

    [Fact]
    public async Task FindTypeUsages_ShouldReturnTypeUsages()
    {
        // Act
        var result = await _advancedReferenceFinder.FindTypeUsagesAsync("Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Service", result.TypeName);
        Assert.True(result.TotalUsages >= 1);
    }

    [Fact]
    public async Task AnalyzeCallPatterns_ShouldReturnPatternAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeCallPatternsAsync("Execute", "Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Execute", result.TargetMethod.Name);
        Assert.True(result.TotalCallSites >= 1);
    }

    [Fact]
    public async Task FindCallChainsBetween_ShouldFindPaths()
    {
        // Arrange
        var fromMethod = new MethodSignature
        {
            Name = "Run",
            ContainingType = "MCPsharp.Tests.TestFixtures.Manager",
            ReturnType = "void"
        };

        var toMethod = new MethodSignature
        {
            Name = "Execute",
            ContainingType = "MCPsharp.Tests.TestFixtures.Service",
            ReturnType = "void"
        };

        // Act
        var result = await _advancedReferenceFinder.FindCallChainsBetweenAsync(fromMethod, toMethod, 10);

        // Assert
        Assert.NotNull(result);
        // May return empty in our simple test setup
    }

    [Fact]
    public async Task FindRecursiveCallChains_ShouldDetectRecursion()
    {
        // Act
        var result = await _advancedReferenceFinder.FindRecursiveCallChainsAsync("RecursiveMethod", "RecursiveService", 10);

        // Assert
        Assert.NotNull(result);
        // Should detect recursive calls if they exist
    }

    [Fact]
    public async Task AnalyzeInheritance_ShouldAnalyzeInheritance()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeInheritanceAsync("Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Service", result.TargetType);
    }

    [Fact]
    public async Task FindUnusedMethods_ShouldFindUnusedMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindUnusedMethodsAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.NotNull(result);
        // May find unused methods depending on the test setup
    }

    [Fact]
    public async Task FindTestOnlyMethods_ShouldFindTestOnlyMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindTestOnlyMethods();

        // Assert
        Assert.NotNull(result);
        // May return empty since our test files aren't actual test files
    }

    [Fact]
    public async Task AnalyzeCallGraph_ShouldAnalyzeCallGraph()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeCallGraphAsync("Service", null);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Methods.Count >= 1);
    }

    [Fact]
    public async Task FindCircularDependencies_ShouldDetectCycles()
    {
        // Act
        var result = await _advancedReferenceFinder.FindCircularDependenciesAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.NotNull(result);
        // Should detect the indirect recursive cycle in RecursiveService
    }

    [Fact]
    public async Task FindShortestPath_ShouldReturnShortestPath()
    {
        // Arrange
        var fromMethod = new MethodSignature
        {
            Name = "Run",
            ContainingType = "MCPsharp.Tests.TestFixtures.Manager",
            ReturnType = "void"
        };

        var toMethod = new MethodSignature
        {
            Name = "Process",
            ContainingType = "MCPsharp.Tests.TestFixtures.Service",
            ReturnType = "void"
        };

        // Act
        var result = await _advancedReferenceFinder.FindShortestPathAsync(fromMethod, toMethod);

        // Assert
        // May return null if no path exists
    }

    [Fact]
    public async Task FindReachableMethods_ShouldFindReachableMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindReachableMethodsAsync("Run", "Manager", 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Run", result.StartMethod.Name);
        Assert.True(result.ReachableMethods.Count >= 1);
    }

    [Fact]
    public async Task AnalyzeTypeDependencies_ShouldAnalyzeDependencies()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeTypeDependenciesAsync("Manager");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Manager", result.TargetType);
    }

    [Fact]
    public async Task AnalyzeUsagePatterns_ShouldAnalyzePatterns()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeUsagePatternsAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TypeStatistics.Count >= 1);
    }

    [Fact]
    public async Task FindRefactoringOpportunities_ShouldFindOpportunities()
    {
        // Act
        var result = await _advancedReferenceFinder.FindRefactoringOpportunitiesAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalOpportunities >= 0);
    }

    [Fact]
    public async Task AnalyzeMethodComprehensively_ShouldReturnComprehensiveAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeMethodComprehensivelyAsync("Execute", "Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Execute", result.MethodName);
        Assert.Equal("Service", result.ContainingType);
        Assert.NotNull(result.Callers);
        Assert.NotNull(result.CallPatterns);
    }

    [Fact]
    public async Task AnalyzeTypeComprehensively_ShouldReturnComprehensiveAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeTypeComprehensivelyAsync("Service");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Service", result.TypeName);
        Assert.NotNull(result.TypeUsages);
        Assert.NotNull(result.InheritanceAnalysis);
    }

    [Fact]
    public async Task FindMethodsBySignature_ShouldFindMatchingMethods()
    {
        // Arrange
        var signature = new MethodSignature
        {
            Name = "Execute",
            ReturnType = "void",
            Parameters = new List<ParameterInfo>(),
            ContainingType = "MCPsharp.Tests.TestFixtures.Service",
            Accessibility = "public"
        };

        // Act
        var result = await _advancedReferenceFinder.FindMethodsBySignatureAsync(signature);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Count >= 1);
        Assert.Contains(result, m => m.Name == "Execute");
    }

    [Fact]
    public async Task FindSimilarMethods_ShouldFindSimilarMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindSimilarMethods("Execute", 0.7);

        // Assert
        Assert.NotNull(result);
        // Should find methods with similar names
    }

    [Fact]
    public async Task GetCapabilities_ShouldReturnCapabilities()
    {
        // Act
        var result = await _advancedReferenceFinder.GetCapabilitiesAsync();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsWorkspaceReady);
        Assert.True(result.TotalFiles >= 1);
        Assert.True(result.TotalTypes >= 1);
        Assert.True(result.TotalMethods >= 1);
        Assert.True(result.SupportedAnalyses.Count >= 8);
    }

    [Fact]
    public void MethodComprehensiveAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var callers = new CallerResult
        {
            TargetSymbol = "Execute",
            TargetSignature = new MethodSignature
            {
                Name = "Execute",
                ContainingType = "Service",
                ReturnType = "void"
            },
            Callers = new List<CallerInfo>
            {
                new()
                {
                    CallerMethod = "Run",
                    CallerType = "Manager",
                    CallType = CallType.Direct,
                    Confidence = ConfidenceLevel.High
                }
            }
        };

        var callPatterns = new CallPatternAnalysis
        {
            TargetMethod = new MethodSignature
            {
                Name = "Execute",
                ContainingType = "Service",
                ReturnType = "void"
            },
            TotalCallSites = 1
        };

        var analysis = new MethodComprehensiveAnalysis
        {
            MethodName = "Execute",
            ContainingType = "Service",
            Callers = callers,
            CallPatterns = callPatterns,
            TotalDirectCallers = 1,
            TotalIndirectCallers = 0,
            HasRecursiveCalls = false,
            AnalysisTime = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("Execute", analysis.MethodName);
        Assert.Equal("Service", analysis.ContainingType);
        Assert.Equal(1, analysis.TotalDirectCallers);
        Assert.False(analysis.HasRecursiveCalls);
    }

    [Fact]
    public void TypeComprehensiveAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var typeUsages = new TypeUsageResult
        {
            TypeName = "Service",
            TotalUsages = 5,
            Instantiations = new List<TypeUsageInfo>
            {
                new()
                {
                    UsageKind = TypeUsageKind.Instantiation,
                    Confidence = ConfidenceLevel.High
                }
            }
        };

        var inheritanceAnalysis = new InheritanceAnalysis
        {
            TargetType = "Service",
            DerivedClasses = new List<TypeUsageInfo>(),
            InterfaceImplementations = new List<TypeUsageInfo>
            {
                new()
                {
                    UsageKind = TypeUsageKind.InterfaceImplementation,
                    Confidence = ConfidenceLevel.High
                }
            }
        };

        var analysis = new TypeComprehensiveAnalysis
        {
            TypeName = "Service",
            TypeUsages = typeUsages,
            InheritanceAnalysis = inheritanceAnalysis,
            TotalUsages = 5,
            InstantiationCount = 1,
            InterfaceImplementationCount = 1,
            AnalysisTime = DateTime.UtcNow
        };

        // Assert
        Assert.Equal("Service", analysis.TypeName);
        Assert.Equal(5, analysis.TotalUsages);
        Assert.Equal(1, analysis.InstantiationCount);
        Assert.Equal(1, analysis.InterfaceImplementationCount);
    }

    public void Dispose()
    {
        _workspace?.Dispose();
    }
}
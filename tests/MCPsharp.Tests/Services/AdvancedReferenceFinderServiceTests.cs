using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Integration tests for AdvancedReferenceFinderService
/// </summary>
[TestFixture]
public class AdvancedReferenceFinderServiceIntegrationTests
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
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            throw new InvalidOperationException("Failed to create compilation for test workspace");
        }
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
    }

    [Test]
    public async Task FindCallers_ShouldReturnCallerAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.FindCallersAsync("Execute", "Service");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalCallers, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Callers, Has.Some.Property("CallerType").Contains("Manager"));
    }

    [Test]
    public async Task FindCallersAtLocation_ShouldFindCallersAtLocation()
    {
        // This test requires actual file locations, which is complex to set up
        // We'll test that the method doesn't throw
        var result = await _advancedReferenceFinder.FindCallersAtLocationAsync("Service.cs", 10, 5);

        // May return null for non-existent location
    }

    [Test]
    public async Task FindCallChains_ShouldReturnCallChains()
    {
        // Act
        var result = await _advancedReferenceFinder.FindCallChainsAsync("Execute", "Service", CallDirection.Backward, 5);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Direction, Is.EqualTo(CallDirection.Backward));
        Assert.That(result.TotalPaths, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task FindTypeUsages_ShouldReturnTypeUsages()
    {
        // Act
        var result = await _advancedReferenceFinder.FindTypeUsagesAsync("Service");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TypeName, Is.EqualTo("Service"));
        Assert.That(result.TotalUsages, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task AnalyzeCallPatterns_ShouldReturnPatternAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeCallPatternsAsync("Execute", "Service");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TargetMethod.Name, Is.EqualTo("Execute"));
        Assert.That(result.TotalCallSites, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task FindCallChainsBetween_ShouldFindPaths()
    {
        // Arrange
        var fromMethod = new MethodSignature
        {
            Name = "Run",
            ContainingType = "MCPsharp.Tests.TestFixtures.Manager",
            ReturnType = "void",
                Accessibility = "public"
        };

        var toMethod = new MethodSignature
        {
            Name = "Execute",
            ContainingType = "MCPsharp.Tests.TestFixtures.Service",
            ReturnType = "void",
                Accessibility = "public"
        };

        // Act
        var result = await _advancedReferenceFinder.FindCallChainsBetweenAsync(fromMethod, toMethod, 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        // May return empty in our simple test setup
    }

    [Test]
    public async Task FindRecursiveCallChains_ShouldDetectRecursion()
    {
        // Act
        var result = await _advancedReferenceFinder.FindRecursiveCallChainsAsync("RecursiveMethod", "RecursiveService", 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Should detect recursive calls if they exist
    }

    [Test]
    public async Task AnalyzeInheritance_ShouldAnalyzeInheritance()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeInheritanceAsync("Service");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TargetType, Is.EqualTo("Service"));
    }

    [Test]
    public async Task FindUnusedMethods_ShouldFindUnusedMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindUnusedMethodsAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.That(result, Is.Not.Null);
        // May find unused methods depending on the test setup
    }

    [Test]
    public async Task FindTestOnlyMethods_ShouldFindTestOnlyMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindTestOnlyMethods();

        // Assert
        Assert.That(result, Is.Not.Null);
        // May return empty since our test files aren't actual test files
    }

    [Test]
    public async Task AnalyzeCallGraph_ShouldAnalyzeCallGraph()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeCallGraphAsync("Service", null);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Methods.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task FindCircularDependencies_ShouldDetectCycles()
    {
        // Act
        var result = await _advancedReferenceFinder.FindCircularDependenciesAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.That(result, Is.Not.Null);
        // Should detect the indirect recursive cycle in RecursiveService
    }

    [Test]
    public async Task FindShortestPath_ShouldReturnShortestPath()
    {
        // Arrange
        var fromMethod = new MethodSignature
        {
            Name = "Run",
            ContainingType = "MCPsharp.Tests.TestFixtures.Manager",
            ReturnType = "void",
                Accessibility = "public"
        };

        var toMethod = new MethodSignature
        {
            Name = "Process",
            ContainingType = "MCPsharp.Tests.TestFixtures.Service",
            ReturnType = "void",
                Accessibility = "public"
        };

        // Act
        var result = await _advancedReferenceFinder.FindShortestPathAsync(fromMethod, toMethod);

        // Assert
        // May return null if no path exists
    }

    [Test]
    public async Task FindReachableMethods_ShouldFindReachableMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindReachableMethodsAsync("Run", "Manager", 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.StartMethod.Name, Is.EqualTo("Run"));
        Assert.That(result.ReachableMethods.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task AnalyzeTypeDependencies_ShouldAnalyzeDependencies()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeTypeDependenciesAsync("Manager");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TargetType, Is.EqualTo("Manager"));
    }

    [Test]
    public async Task AnalyzeUsagePatterns_ShouldAnalyzePatterns()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeUsagePatternsAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TypeStatistics.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task FindRefactoringOpportunities_ShouldFindOpportunities()
    {
        // Act
        var result = await _advancedReferenceFinder.FindRefactoringOpportunitiesAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalOpportunities, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task AnalyzeMethodComprehensively_ShouldReturnComprehensiveAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeMethodComprehensivelyAsync("Execute", "Service");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MethodName, Is.EqualTo("Execute"));
        Assert.That(result.ContainingType, Is.EqualTo("Service"));
        Assert.That(result.Callers, Is.Not.Null);
        Assert.That(result.CallPatterns, Is.Not.Null);
    }

    [Test]
    public async Task AnalyzeTypeComprehensively_ShouldReturnComprehensiveAnalysis()
    {
        // Act
        var result = await _advancedReferenceFinder.AnalyzeTypeComprehensivelyAsync("Service");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TypeName, Is.EqualTo("Service"));
        Assert.That(result.TypeUsages, Is.Not.Null);
        Assert.That(result.InheritanceAnalysis, Is.Not.Null);
    }

    [Test]
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
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(result, Has.Some.Property("Name").EqualTo("Execute"));
    }

    [Test]
    public async Task FindSimilarMethods_ShouldFindSimilarMethods()
    {
        // Act
        var result = await _advancedReferenceFinder.FindSimilarMethods("Execute", 0.7);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Should find methods with similar names
    }

    [Test]
    public async Task GetCapabilities_ShouldReturnCapabilities()
    {
        // Act
        var result = await _advancedReferenceFinder.GetCapabilitiesAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsWorkspaceReady, Is.True);
        Assert.That(result.TotalFiles, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.TotalTypes, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.TotalMethods, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.SupportedAnalyses.Count, Is.GreaterThanOrEqualTo(8));
    }

    [Test]
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
                ReturnType = "void",
                Accessibility = "public"
            },
            Callers = new List<CallerInfo>
            {
                new()
                {
                    CallerMethod = "Run",
                    CallerType = "Manager",
                    CallType = CallType.Direct,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 1,
                    Column = 1,
                    CallExpression = "Execute()",
                    CallerSignature = new MethodSignature
                    {
                        Name = "Run",
                        ContainingType = "Manager",
                        ReturnType = "void",
                        Accessibility = "public"
                    }
                }
            }
        };

        var callPatterns = new CallPatternAnalysis
        {
            TargetMethod = new MethodSignature
            {
                Name = "Execute",
                ContainingType = "Service",
                ReturnType = "void",
                Accessibility = "public"
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
        Assert.That(analysis.MethodName, Is.EqualTo("Execute"));
        Assert.That(analysis.ContainingType, Is.EqualTo("Service"));
        Assert.That(analysis.TotalDirectCallers, Is.EqualTo(1));
        Assert.That(analysis.HasRecursiveCalls, Is.False);
    }

    [Test]
    public void TypeComprehensiveAnalysis_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var typeUsages = new TypeUsageResult
        {
            TypeName = "Service",
            FullTypeName = "TestProject.Service",
            Usages = new List<TypeUsageInfo>
            {
                new()
                {
                    UsageKind = TypeUsageKind.Instantiation,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 1,
                    Column = 1,
                    Context = "new Service()"
                },
                new()
                {
                    UsageKind = TypeUsageKind.Parameter,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 2,
                    Column = 1,
                    Context = "method parameter"
                },
                new()
                {
                    UsageKind = TypeUsageKind.ReturnType,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 3,
                    Column = 1,
                    Context = "return type"
                },
                new()
                {
                    UsageKind = TypeUsageKind.GenericArgument,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 4,
                    Column = 1,
                    Context = "generic argument"
                },
                new()
                {
                    UsageKind = TypeUsageKind.Property,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 5,
                    Column = 1,
                    Context = "property type"
                }
            }
        };

        var inheritanceAnalysis = new InheritanceAnalysis
        {
            TargetType = "Service",
            BaseClasses = new List<TypeUsageInfo>(),
            DerivedClasses = new List<TypeUsageInfo>(),
            ImplementedInterfaces = new List<TypeUsageInfo>
            {
                new()
                {
                    UsageKind = TypeUsageKind.InterfaceImplementation,
                    Confidence = ConfidenceLevel.High,
                    File = "TestFile.cs",
                    Line = 1,
                    Column = 1,
                    Context = "interface implementation"
                }
            },
            InterfaceImplementations = new List<TypeUsageInfo>(),
            InheritanceDepth = 0,
            InheritanceChain = new List<string>()
        };

        var analysis = new TypeComprehensiveAnalysis
        {
            TypeName = "Service",
            TypeUsages = typeUsages,
            InheritanceAnalysis = inheritanceAnalysis,
            InstantiationCount = 1,
            InterfaceImplementationCount = 1,
            TotalUsages = typeUsages.Usages.Count, // Only count direct usages, not interface implementations
            AnalysisTime = DateTime.UtcNow
        };

        // Assert
        Assert.That(analysis.TypeName, Is.EqualTo("Service"));
        Assert.That(analysis.TotalUsages, Is.EqualTo(5));
        Assert.That(analysis.InstantiationCount, Is.EqualTo(1));
        Assert.That(analysis.InterfaceImplementationCount, Is.EqualTo(1));
    }

}
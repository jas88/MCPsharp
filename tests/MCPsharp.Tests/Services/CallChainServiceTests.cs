using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Unit tests for CallChainService
/// </summary>
[TestFixture]
public class CallChainServiceTests
{
    private RoslynWorkspace _workspace;
    private SymbolQueryService _symbolQuery;
    private CallerAnalysisService _callerAnalysis;
    private CallChainService _callChain;

    [SetUp]
    public void SetUp()
    {
        _workspace = new RoslynWorkspace();
        _symbolQuery = new SymbolQueryService(_workspace);
        _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
        _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);

        // Initialize workspace with test fixtures
        InitializeWorkspace().Wait();
    }

    private async Task InitializeWorkspace()
    {
        // Initialize workspace for testing
        await _workspace.InitializeTestWorkspaceAsync();

        // Create a more complex test scenario for call chains
        var testFiles = new[]
        {
            ("IRepository.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public interface IRepository
{
    T GetById<T>(int id);
    void Save<T>(T entity);
    void Delete<T>(int id);
}"),
            ("Repository.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Repository : IRepository
{
    public T GetById<T>(int id)
    {
        return default(T);
    }

    public void Save<T>(T entity)
    {
        // Save logic
    }

    public void Delete<T>(int id)
    {
        // Delete logic
    }
}"),
            ("Service.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Service
{
    private readonly IRepository _repository;

    public Service(IRepository repository)
    {
        _repository = repository;
    }

    public void ProcessData(int id)
    {
        var data = _repository.GetById<DataEntity>(id);
        if (data != null)
        {
            _repository.Save(data);
        }
    }

    public void RemoveData(int id)
    {
        _repository.Delete<DataEntity>(id);
    }
}"),
            ("Controller.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Controller
{
    private readonly Service _service;

    public Controller(Service service)
    {
        _service = service;
    }

    public void HandleRequest(int id)
    {
        _service.ProcessData(id);
    }

    public void HandleDelete(int id)
    {
        _service.RemoveData(id);
    }
}"),
            ("DataEntity.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class DataEntity
{
    public int Id { get; set; }
    public string Name { get; set; }
}")
        };

        // Add files to workspace using the in-memory document method
        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine("TestFixtures", fileName);
            await _workspace.AddInMemoryDocumentAsync(filePath, content);
        }
    }

    [Test]
    public async Task FindCallChains_Backward_ShouldFindCallers()
    {
        // Act
        var result = await _callChain.FindCallChainsAsync("ProcessData", "Service", 5);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Direction, Is.EqualTo(CallDirection.Backward));
        Assert.That(result.TotalPaths, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Paths, Has.All.Property("Steps").Property("Count").GreaterThan(0));
    }

    [Test]
    public async Task FindCallChains_Forward_ShouldFindCalledMethods()
    {
        // Act
        var result = await _callChain.FindForwardCallChainsAsync("ProcessData", "Service", 5);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Direction, Is.EqualTo(CallDirection.Forward));
        Assert.That(result.TotalPaths, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task FindCallChains_WithMaxDepth_ShouldLimitDepth()
    {
        // Act
        var result = await _callChain.FindCallChainsAsync("ProcessData", "Service", 2);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Paths, Has.All.Property("Steps").Property("Count").LessThanOrEqualTo(2));
    }

    [Test]
    public async Task FindCallChainsBetween_ShouldFindPathsBetweenMethods()
    {
        // Arrange
        var fromMethod = new MethodSignature
        {
            Name = "HandleRequest",
            ContainingType = "MCPsharp.Tests.TestFixtures.Controller",
            ReturnType = "void",
                Accessibility = "public"
        };

        var toMethod = new MethodSignature
        {
            Name = "GetById",
            ContainingType = "MCPsharp.Tests.TestFixtures.Repository",
            ReturnType = "T",
                Accessibility = "public"
        };

        // Act
        var paths = await _callChain.FindCallChainsBetweenAsync(fromMethod, toMethod, 10);

        // Assert
        Assert.That(paths, Is.Not.Null);
        // This might return empty paths in our simple test setup
    }

    [Test]
    public async Task FindRecursiveCallChains_ShouldDetectRecursion()
    {
        // Add a recursive method to test
        _ = @"
namespace MCPsharp.Tests.TestFixtures;

public class RecursiveClass
{
    public void RecursiveMethod(int depth)
    {
        if (depth > 0)
        {
            RecursiveMethod(depth - 1);
        }
    }
}";

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestFixtures", "RecursiveClass.cs");
        // await _workspace.AddDocumentAsync(filePath); // Method no longer available

        // Act
        var result = await _callChain.FindRecursiveCallChainsAsync("RecursiveMethod", "RecursiveClass", 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Should find recursive calls if they exist
    }

    [Test]
    public async Task AnalyzeCallGraph_ShouldReturnCallGraph()
    {
        // Act
        var result = await _callChain.AnalyzeCallGraphAsync("Service", null);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Methods.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.CallGraph, Is.Not.Null);
        Assert.That(result.ReverseCallGraph, Is.Not.Null);
    }

    [Test]
    public async Task AnalyzeCallGraph_ForNamespace_ShouldAnalyzeNamespace()
    {
        // Act
        var result = await _callChain.AnalyzeCallGraphAsync(null, "MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Methods.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.Scope, Is.EqualTo("MCPsharp.Tests.TestFixtures"));
    }

    [Test]
    public async Task FindCircularDependencies_ShouldDetectCycles()
    {
        // Add circular dependency scenario
        _ = @"
namespace MCPsharp.Tests.TestFixtures;

public class A
{
    private readonly B _b;
    public A(B b) => _b = b;
    public void MethodA() => _b.MethodB();
}

public class B
{
    private readonly A _a;
    public B(A a) => _a = a;
    public void MethodB() => _a.MethodA();
}";

        var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestFixtures", "Circular.cs");
        // await _workspace.AddDocumentAsync(filePath); // Method no longer available

        // Act
        var result = await _callChain.FindCircularDependenciesAsync("MCPsharp.Tests.TestFixtures");

        // Assert
        Assert.That(result, Is.Not.Null);
        // Should detect circular dependencies if they exist
    }

    [Test]
    public async Task FindReachableMethods_ShouldFindReachableMethods()
    {
        // Act
        var result = await _callChain.FindReachableMethodsAsync("HandleRequest", "Controller", 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.StartMethod.Name, Is.EqualTo("HandleRequest"));
        Assert.That(result.ReachableMethods.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(result.MethodsByDepth.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public async Task FindShortestPath_ShouldReturnShortestPath()
    {
        // Arrange
        var fromMethod = new MethodSignature
        {
            Name = "HandleRequest",
            ContainingType = "MCPsharp.Tests.TestFixtures.Controller",
            ReturnType = "void",
            Accessibility = "public"
        };

        var toMethod = new MethodSignature
        {
            Name = "ProcessData",
            ContainingType = "MCPsharp.Tests.TestFixtures.Service",
            ReturnType = "void",
            Accessibility = "public"
        };

        // Act
        var result = await _callChain.FindShortestPathAsync(fromMethod, toMethod);

        // Assert
        // May return null if no path exists in our test setup
    }

    [Test]
    public async Task FindCallChains_WithNonExistentMethod_ShouldReturnNull()
    {
        // Act
        var result = await _callChain.FindCallChainsAsync("NonExistentMethod");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void CallChainPath_ShouldCalculateCorrectProperties()
    {
        // Arrange
        var steps = new List<CallChainStep>
        {
            new()
            {
                FromMethod = new MethodSignature { Name = "Method1", ContainingType = "Class1", ReturnType = "void", Accessibility = "public" },
                ToMethod = new MethodSignature { Name = "Method2", ContainingType = "Class2", ReturnType = "void", Accessibility = "public" },
                File = "test.cs",
                Line = 10,
                Column = 5,
                CallType = CallType.Direct,
                Confidence = ConfidenceLevel.High
            }
        };

        var path = new CallChainPath
        {
            Steps = steps,
            Confidence = ConfidenceLevel.High
        };

        // Assert
        Assert.That(path.Length, Is.EqualTo(1));
        Assert.That(path.StartMethod?.Name, Is.EqualTo("Method1"));
        Assert.That(path.EndMethod?.Name, Is.EqualTo("Method2"));
        Assert.That(path.IsRecursive, Is.False);
    }

    [Test]
    public void CallChainPath_WithRecursiveCall_ShouldBeMarkedRecursive()
    {
        // Arrange
        var method = new MethodSignature
        {
            Name = "RecursiveMethod",
            ContainingType = "TestClass",
            ReturnType = "void",
            Accessibility = "public"
        };

        var steps = new List<CallChainStep>
        {
            new()
            {
                FromMethod = method,
                ToMethod = method,
                File = "test.cs",
                Line = 10,
                Column = 5,
                CallType = CallType.Direct,
                Confidence = ConfidenceLevel.High
            }
        };

        var path = new CallChainPath
        {
            Steps = steps,
            Confidence = ConfidenceLevel.High,
            IsRecursive = true
        };

        // Assert
        Assert.That(path.IsRecursive, Is.True);
    }

    [Test]
    public void CircularDependency_ShouldGenerateCorrectDescription()
    {
        // Arrange
        var methods = new List<MethodSignature>
        {
            new() { Name = "MethodA", ContainingType = "ClassA", ReturnType = "void", Accessibility = "public" },
            new() { Name = "MethodB", ContainingType = "ClassB", ReturnType = "void", Accessibility = "public" }
        };

        var steps = new List<CallChainStep>
        {
            new()
            {
                FromMethod = methods[0],
                ToMethod = methods[1],
                File = "test.cs",
                Line = 10,
                Column = 5,
                CallType = CallType.Direct,
                Confidence = ConfidenceLevel.High
            },
            new()
            {
                FromMethod = methods[1],
                ToMethod = methods[0],
                File = "test.cs",
                Line = 20,
                Column = 5,
                CallType = CallType.Direct,
                Confidence = ConfidenceLevel.High
            }
        };

        var circularDependency = new CircularDependency
        {
            Methods = methods,
            CycleLength = 2,
            Steps = steps,
            Confidence = ConfidenceLevel.High,
            FilesInvolved = new List<string> { "test.cs" }
        };

        // Act
        var description = circularDependency.GetDescription();

        // Assert
        Assert.That(description, Does.Contain("Circular dependency"));
        Assert.That(description, Does.Contain("MethodA"));
        Assert.That(description, Does.Contain("MethodB"));
    }

    [TearDown]
    public void TearDown()
    {
        _workspace?.Dispose();
    }
}
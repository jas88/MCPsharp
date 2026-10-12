using Microsoft.CodeAnalysis;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;
using MCPsharp.Tests.TestData;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class AdvancedReferenceFinderServiceTests
{
    [Test]
    public async Task FindTypeUsagesAsync_WithValidTypeName_ShouldReturnUsages()
    {
        // Arrange
        var typeName = "SampleService";
        var expected = new TypeUsageResult
        {
            TypeName = typeName,
            FullTypeName = $"TestProject.{typeName}",
            Usages = Enumerable.Range(1, 8).Select(i => new TypeUsageInfo
            {
                UsageKind = TypeUsageKind.Instantiation,
                Confidence = ConfidenceLevel.High,
                File = $"test{i}.cs",
                Line = i,
                Column = 1,
                Context = $"new {typeName}()"
            }).ToList()
        };

        _mockTypeUsage.FindTypeUsagesAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindTypeUsagesAsync(typeName);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.TypeName, Is.EqualTo(typeName));
        Assert.That(result.TotalUsages, Is.EqualTo(8));

        await _mockTypeUsage.Received(1).FindTypeUsagesAsync(typeName, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindTypeUsagesAtLocationAsync_WithValidLocation_ShouldReturnUsages()
    {
        // Arrange
        var filePath = "/test/SampleService.cs";
        var line = 10;
        var column = 15;
        var expected = new TypeUsageResult
        {
            TypeName = "SampleType",
            FullTypeName = "TestProject.SampleType",
            Usages = Enumerable.Range(1, 3).Select(i => new TypeUsageInfo
            {
                UsageKind = TypeUsageKind.Instantiation,
                Confidence = ConfidenceLevel.High,
                File = $"test{i}.cs",
                Line = i,
                Column = 1,
                Context = $"new SampleType()"
            }).ToList()
        };

        _mockTypeUsage.FindTypeUsagesAtLocationAsync(filePath, line, column, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindTypeUsagesAtLocationAsync(filePath, line, column);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.TotalUsages, Is.EqualTo(3));

        await _mockTypeUsage.Received(1).FindTypeUsagesAtLocationAsync(filePath, line, column, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeCallPatternsAsync_WithValidMethod_ShouldReturnAnalysis()
    {
        // Arrange
        var methodName = "TestMethod";
        var containingType = "TestClass";
        var expected = new CallPatternAnalysis
        {
            TargetMethod = new MethodSignature
            {
                Name = methodName,
                ContainingType = containingType,
                ReturnType = "void",
                Accessibility = "public"
            },
            TotalCallSites = 5
        };

        _mockCallerAnalysis.AnalyzeCallPatternsAsync(methodName, containingType, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.AnalyzeCallPatternsAsync(methodName, containingType);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TargetMethod, Is.Not.Null);
        Assert.That(result.TargetMethod.Name, Is.EqualTo(methodName));
        Assert.That(result.TargetMethod.ContainingType, Is.EqualTo(containingType));

        await _mockCallerAnalysis.Received(1).AnalyzeCallPatternsAsync(methodName, containingType, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindCallChainsBetweenAsync_WithValidMethods_ShouldReturnChains()
    {
        // Arrange
        var fromMethod = TestDataFixtures.SampleMethodSignature;
        var toMethod = new MethodSignature
        {
            Name = "TargetMethod",
            ContainingType = "TargetClass",
            ReturnType = "void",
            Accessibility = "public"
        };
        var expected = new List<CallChainPath>
        {
            new()
            {
                Steps = new List<CallChainStep>
                {
                    new() { FromMethod = fromMethod, ToMethod = toMethod, File = "test.cs", Line = 1, Column = 1, CallType = CallType.Direct, Confidence = ConfidenceLevel.High }
                },
                Confidence = ConfidenceLevel.High
            }
        };

        _mockCallChain.FindCallChainsBetweenAsync(fromMethod, toMethod, 10, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCallChainsBetweenAsync(fromMethod, toMethod);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Length, Is.EqualTo(1));

        await _mockCallChain.Received(1).FindCallChainsBetweenAsync(fromMethod, toMethod, 10, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindRecursiveCallChainsAsync_WithValidMethod_ShouldReturnRecursiveChains()
    {
        // Arrange
        var methodName = "RecursiveMethod";
        var containingType = "TestClass";
        var fromMethod = new MethodSignature
        {
            Name = methodName,
            ContainingType = containingType,
            ReturnType = "void",
            Accessibility = "public"
        };
        var expected = new List<CallChainPath>
        {
            new()
            {
                IsRecursive = true,
                Steps = new List<CallChainStep>
                {
                    new() { FromMethod = fromMethod, ToMethod = fromMethod, File = "test.cs", Line = 1, Column = 1, CallType = CallType.Direct, Confidence = ConfidenceLevel.High }
                },
                Confidence = ConfidenceLevel.High
            }
        };

        _mockCallChain.FindRecursiveCallChainsAsync(methodName, containingType, 20, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindRecursiveCallChainsAsync(methodName, containingType);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].IsRecursive, Is.True);

        await _mockCallChain.Received(1).FindRecursiveCallChainsAsync(methodName, containingType, 20, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeInheritanceAsync_WithValidType_ShouldReturnInheritanceAnalysis()
    {
        // Arrange
        var typeName = "DerivedClass";
        var expected = new InheritanceAnalysis
        {
            TargetType = typeName,
            BaseClasses = new(),
            DerivedClasses = new(),
            ImplementedInterfaces = new(),
            InterfaceImplementations = new(),
            InheritanceDepth = 0,
            InheritanceChain = new()
        };

        _mockTypeUsage.AnalyzeInheritanceAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.AnalyzeInheritanceAsync(typeName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TargetType, Is.EqualTo(typeName));

        await _mockTypeUsage.Received(1).AnalyzeInheritanceAsync(typeName, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindUnusedMethodsAsync_WithNamespaceFilter_ShouldReturnUnusedMethods()
    {
        // Arrange
        var namespaceFilter = "TestProject.Services";
        var expected = new List<MethodSignature>
        {
            new() { Name = "UnusedMethod1", ContainingType = "TestClass", ReturnType = "void", Accessibility = "public" },
            new() { Name = "UnusedMethod2", ContainingType = "TestClass2", ReturnType = "void", Accessibility = "public" }
        };

        _mockCallerAnalysis.FindUnusedMethodsAsync(namespaceFilter, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindUnusedMethodsAsync(namespaceFilter);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].Name, Is.EqualTo("UnusedMethod1"));

        await _mockCallerAnalysis.Received(1).FindUnusedMethodsAsync(namespaceFilter, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindTestOnlyMethodsAsync_ShouldReturnTestOnlyMethods()
    {
        // Arrange
        var expected = new List<MethodSignature>
        {
            new() { Name = "TestMethod", ContainingType = "TestClass", ReturnType = "void", Accessibility = "public" }
        };

        _mockCallerAnalysis.FindTestOnlyMethodsAsync(Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindTestOnlyMethodsAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("TestMethod"));

        await _mockCallerAnalysis.Received(1).FindTestOnlyMethodsAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeCallGraphAsync_WithTypeFilter_ShouldReturnCallGraphAnalysis()
    {
        // Arrange
        var typeName = "SampleService";
        var methods = Enumerable.Range(1, 15)
            .Select(i => new MethodSignature
            {
                Name = $"Method{i}",
                ContainingType = "SampleService",
                ReturnType = "void",
                Accessibility = "public"
            })
            .ToList();

        var expected = new CallGraphAnalysis
        {
            Scope = typeName,
            Methods = methods,
            Relationships = new List<CallRelationship>(),
            CallGraph = new Dictionary<string, List<string>>(),
            ReverseCallGraph = new Dictionary<string, List<string>>(),
            EntryPoints = new List<string>(),
            LeafMethods = new List<string>(),
            CircularDependencies = new List<CircularDependency>()
        };

        _mockCallChain.AnalyzeCallGraphAsync(typeName, null, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.AnalyzeCallGraphAsync(typeName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Scope, Is.EqualTo(typeName));
        Assert.That(result.Methods, Is.Not.Null);
        Assert.That(result.Methods.Count, Is.EqualTo(15));

        await _mockCallChain.Received(1).AnalyzeCallGraphAsync(typeName, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindCircularDependenciesAsync_WithNamespaceFilter_ShouldReturnDependencies()
    {
        // Arrange
        var namespaceFilter = "TestProject";
        var expected = new List<CircularDependency>
        {
            new() {
                Methods = new List<MethodSignature> {
                    new() {
                        Name = "Method1",
                        ContainingType = "Class1",
                        ReturnType = "void",
                        Accessibility = "public"
                    }
                },
                CycleLength = 1,
                Steps = new List<CallChainStep> {
                    new() {
                        FromMethod = new() { Name = "Method1", ContainingType = "Class1", ReturnType = "void", Accessibility = "public" },
                        ToMethod = new() { Name = "Method1", ContainingType = "Class1", ReturnType = "void", Accessibility = "public" },
                        File = "File1.cs",
                        Line = 1,
                        Column = 1,
                        CallType = CallType.Direct,
                        Confidence = ConfidenceLevel.High
                    }
                },
                Confidence = ConfidenceLevel.High,
                FilesInvolved = new List<string> { "File1.cs" }
            }
        };

        _mockCallChain.FindCircularDependenciesAsync(namespaceFilter, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindCircularDependenciesAsync(namespaceFilter);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].Confidence, Is.EqualTo(ConfidenceLevel.High));

        await _mockCallChain.Received(1).FindCircularDependenciesAsync(namespaceFilter, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindShortestPathAsync_WithValidMethods_ShouldReturnShortestPath()
    {
        // Arrange
        var fromMethod = TestDataFixtures.SampleMethodSignature;
        var toMethod = new MethodSignature { Name = "TestMethod", ContainingType = "TestClass", ReturnType = "void", Accessibility = "public" };
        var expected = new CallChainPath
        {
            Steps = new List<CallChainStep>
            {
                new() { FromMethod = fromMethod, ToMethod = toMethod, File = "test.cs", Line = 1, Column = 1, CallType = CallType.Direct, Confidence = ConfidenceLevel.High }
            },
            Confidence = ConfidenceLevel.High
        };

        _mockCallChain.FindShortestPathAsync(fromMethod, toMethod, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindShortestPathAsync(fromMethod, toMethod);

        // Assert
        Assert.That(result, Is.Not.Null);
        if (result == null)
            throw new InvalidOperationException("Result should not be null");
        Assert.That(result.Length, Is.EqualTo(1));

        await _mockCallChain.Received(1).FindShortestPathAsync(fromMethod, toMethod, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindReachableMethodsAsync_WithValidParameters_ShouldReturnReachabilityAnalysis()
    {
        // Arrange
        var methodName = "StartMethod";
        var containingType = "WorkflowClass";
        var expected = new ReachabilityAnalysis
        {
            StartMethod = new MethodSignature {
                Name = methodName,
                ContainingType = containingType,
                ReturnType = "void",
                Accessibility = "public"
            },
            ReachableMethods = CreateMethodSignatures(12, "ReachableMethod"),
            MethodsByDepth = new Dictionary<int, List<MethodSignature>>
            {
                { 1, CreateMethodSignatures(3, "Depth1Method") },
                { 2, CreateMethodSignatures(4, "Depth2Method") },
                { 3, CreateMethodSignatures(3, "Depth3Method") },
                { 4, CreateMethodSignatures(2, "Depth4Method") }
            },
            UnreachableMethods = new List<MethodSignature>(),
            MaxDepthReached = 5,
            TotalMethodsAnalyzed = 12,
            AnalysisDuration = TimeSpan.FromMilliseconds(100)
        };

        _mockCallChain.FindReachableMethodsAsync(methodName, containingType, 10, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindReachableMethodsAsync(methodName, containingType, 10);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ReachableMethods.Count, Is.EqualTo(12));

        await _mockCallChain.Received(1).FindReachableMethodsAsync(methodName, containingType, 10, Arg.Any<CancellationToken>());
    }
}

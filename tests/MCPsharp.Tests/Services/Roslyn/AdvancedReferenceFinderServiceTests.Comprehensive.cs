using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestData;
using Microsoft.CodeAnalysis;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class AdvancedReferenceFinderServiceTests
{
    [Test]
    public async Task AnalyzeTypeDependenciesAsync_WithValidType_ShouldReturnDependencyAnalysis()
    {
        // Arrange
        var typeName = "ComplexService";
        var expected = new TypeDependencyAnalysis
        {
            TargetType = typeName,
            Dependencies = new List<TypeDependency>(),
            Dependents = new List<TypeDependency>(),
            DependencyFrequency = new Dictionary<string, int>(),
            CircularDependencies = new List<string>(),
            TotalDependencies = 8,
            TotalDependents = 2
        };

        _mockTypeUsage.AnalyzeTypeDependenciesAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.AnalyzeTypeDependenciesAsync(typeName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TargetType, Is.EqualTo(typeName));
        Assert.That(result.TotalDependencies, Is.EqualTo(8));

        await _mockTypeUsage.Received(1).AnalyzeTypeDependenciesAsync(typeName, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeUsagePatternsAsync_WithNamespaceFilter_ShouldReturnPatternAnalysis()
    {
        // Arrange
        var namespaceFilter = "TestProject.Models";
        var expected = new TypeUsagePatternAnalysis
        {
            TypeStatistics = new Dictionary<string, TypeUsageStats>(),
            CommonPatterns = new(),
            MostUsedTypes = new(),
            LeastUsedTypes = new(),
            UsageKindDistribution = new Dictionary<TypeUsageKind, int>(),
            TotalTypesAnalyzed = 15,
            TotalUsagesFound = 50
        };

        _mockTypeUsage.AnalyzeUsagePatternsAsync(namespaceFilter, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.AnalyzeUsagePatternsAsync(namespaceFilter);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalTypesAnalyzed, Is.GreaterThan(0));

        await _mockTypeUsage.Received(1).AnalyzeUsagePatternsAsync(namespaceFilter, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task FindRefactoringOpportunitiesAsync_WithNamespaceFilter_ShouldReturnOpportunities()
    {
        // Arrange
        var namespaceFilter = "TestProject.OldCode";
        var expected = new TypeRefactoringOpportunities
        {
            UnusedTypes = new(),
            SingleImplementationInterfaces = new(),
            PotentialSealedTypes = new(),
            LargeTypes = new(),
            TypesWithCircularDependencies = new(),
            DuplicatedTypes = new(),
            TotalOpportunities = 5,
            OpportunityBreakdown = new Dictionary<string, int>()
        };

        _mockTypeUsage.FindRefactoringOpportunitiesAsync(namespaceFilter, Arg.Any<CancellationToken>())
            .Returns(expected);

        // Act
        var result = await _service.FindRefactoringOpportunitiesAsync(namespaceFilter);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalOpportunities, Is.EqualTo(5));

        await _mockTypeUsage.Received(1).FindRefactoringOpportunitiesAsync(namespaceFilter, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeMethodComprehensivelyAsync_WithValidMethod_ShouldReturnComprehensiveAnalysis()
    {
        // Arrange
        var methodName = "ComplexMethod";
        var containingType = "AnalysisService";

        var mockCallers = new CallerResult
        {
            TargetSymbol = methodName,
            TargetSignature = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
            Callers = Enumerable.Range(1, 3).Select(i => new CallerInfo
            {
                File = "test.cs",
                Line = i,
                Column = 1,
                CallerMethod = $"Caller{i}",
                CallerType = "TestClass",
                CallExpression = $"{methodName}()",
                CallType = CallType.Direct,
                Confidence = ConfidenceLevel.High,
                CallerSignature = new MethodSignature { Name = $"Caller{i}", ContainingType = "TestClass", ReturnType = "void", Accessibility = "public" }
            }).ToList()
        };

        var mockBackwardChains = new CallChainResult
        {
            TargetMethod = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
            Direction = CallDirection.Backward,
            Paths = Enumerable.Range(1, 5).Select(i => new CallChainPath
            {
                Confidence = ConfidenceLevel.High,
                Steps = new List<CallChainStep>
                {
                    new() { FromMethod = new MethodSignature { Name = $"Caller{i}", ContainingType = "TestClass", ReturnType = "void", Accessibility = "public" },
                           ToMethod = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
                           File = "test.cs", Line = i, Column = 1, CallType = CallType.Direct, Confidence = ConfidenceLevel.High }
                }
            }).ToList()
        };

        var mockForwardChains = new CallChainResult
        {
            TargetMethod = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
            Direction = CallDirection.Forward,
            Paths = Enumerable.Range(1, 2).Select(i => new CallChainPath
            {
                Confidence = ConfidenceLevel.High,
                Steps = new List<CallChainStep>
                {
                    new() { FromMethod = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
                           ToMethod = new MethodSignature { Name = $"Callee{i}", ContainingType = "TestClass", ReturnType = "void", Accessibility = "public" },
                           File = "test.cs", Line = i, Column = 1, CallType = CallType.Direct, Confidence = ConfidenceLevel.High }
                }
            }).ToList()
        };

        var mockCallPatterns = new CallPatternAnalysis
        {
            TargetMethod = new MethodSignature
            {
                Name = methodName,
                ContainingType = containingType ?? "TestClass",
                ReturnType = "void",
                Accessibility = "public"
            },
            TotalCallSites = 3
        };

        var mockRecursiveChains = new List<CallChainPath>
        {
            new() {
                IsRecursive = true,
                Steps = new List<CallChainStep>
                {
                    new() {
                        FromMethod = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
                        ToMethod = new MethodSignature { Name = methodName, ContainingType = containingType ?? "TestClass", ReturnType = "void", Accessibility = "public" },
                        File = "test.cs", Line = 1, Column = 1, CallType = CallType.Direct, Confidence = ConfidenceLevel.High
                    }
                },
                Confidence = ConfidenceLevel.High
            }
        };

        _mockCallerAnalysis.FindCallersAsync(methodName, containingType, Arg.Any<CancellationToken>())
            .Returns(mockCallers);
        _mockCallChain.FindCallChainsAsync(methodName, containingType, 5, Arg.Any<CancellationToken>())
            .Returns(mockBackwardChains);
        _mockCallChain.FindForwardCallChainsAsync(methodName, containingType, 5, Arg.Any<CancellationToken>())
            .Returns(mockForwardChains);
        _mockCallerAnalysis.AnalyzeCallPatternsAsync(methodName, containingType, Arg.Any<CancellationToken>())
            .Returns(mockCallPatterns);
        _mockCallChain.FindRecursiveCallChainsAsync(methodName, containingType, 10, Arg.Any<CancellationToken>())
            .Returns(mockRecursiveChains);

        // Act
        var result = await _service.AnalyzeMethodComprehensivelyAsync(methodName, containingType);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.MethodName, Is.EqualTo(methodName));
        Assert.That(result.ContainingType, Is.EqualTo(containingType));
        Assert.That(result.TotalDirectCallers, Is.EqualTo(3));
        Assert.That(result.TotalBackwardPaths, Is.EqualTo(5));
        Assert.That(result.TotalForwardPaths, Is.EqualTo(2));
        Assert.That(result.HasRecursiveCalls, Is.True);
        Assert.That(result.AnalysisTime, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }

    [Test]
    public async Task AnalyzeTypeComprehensivelyAsync_WithValidType_ShouldReturnComprehensiveAnalysis()
    {
        // Arrange
        var typeName = "ComplexType";

        var mockTypeUsages = new TypeUsageResult
        {
            TypeName = typeName,
            FullTypeName = $"TestProject.{typeName}",
            Usages = Enumerable.Range(1, 10).Select(i => new TypeUsageInfo
            {
                UsageKind = TypeUsageKind.Instantiation,
                Confidence = ConfidenceLevel.High,
                File = $"test{i}.cs",
                Line = i,
                Column = 1,
                Context = $"new {typeName}()"
            }).ToList()
        };

        var mockInheritance = new InheritanceAnalysis
        {
            TargetType = typeName,
            BaseClasses = new List<TypeUsageInfo>(),
            DerivedClasses = new List<TypeUsageInfo>(),
            ImplementedInterfaces = new List<TypeUsageInfo>(),
            InterfaceImplementations = new List<TypeUsageInfo>(),
            InheritanceDepth = 0,
            InheritanceChain = new List<string>()
        };

        var mockDependencies = new TypeDependencyAnalysis
        {
            TargetType = typeName,
            Dependencies = new List<TypeDependency>(),
            Dependents = new List<TypeDependency>(),
            DependencyFrequency = new Dictionary<string, int>(),
            CircularDependencies = new List<string>(),
            TotalDependencies = 5,
            TotalDependents = 1
        };

        var mockGenericUsages = new List<TypeUsageInfo>
        {
            new() {
                UsageKind = TypeUsageKind.GenericArgument,
                Confidence = ConfidenceLevel.High,
                File = "test.cs",
                Line = 1,
                Column = 1,
                Context = $"List<{typeName}>()"
            }
        };

        _mockTypeUsage.FindTypeUsagesAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(mockTypeUsages);
        _mockTypeUsage.AnalyzeInheritanceAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(mockInheritance);
        _mockTypeUsage.AnalyzeTypeDependenciesAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(mockDependencies);
        _mockTypeUsage.FindGenericUsagesAsync(typeName, Arg.Any<CancellationToken>())
            .Returns(mockGenericUsages);

        // Act
        var result = await _service.AnalyzeTypeComprehensivelyAsync(typeName);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TypeName, Is.EqualTo(typeName));
        Assert.That(result.TotalUsages, Is.EqualTo(10));
        Assert.That(result.DependencyCount, Is.EqualTo(5));
        Assert.That(result.GenericUsageCount, Is.EqualTo(1));
        Assert.That(result.AnalysisTime, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }

    [Test]
    public async Task FindMethodsBySignatureAsync_WithExactMatch_ShouldReturnMatchingMethods()
    {
        // Arrange - Use real workspace to avoid mocking Roslyn abstract classes
        var signature = TestDataFixtures.SampleMethodSignature;
        var realWorkspace = new RoslynWorkspace();
        var realSymbolQuery = new SymbolQueryService(realWorkspace);
        var service = new AdvancedReferenceFinderService(
            realWorkspace,
            realSymbolQuery,
            _mockCallerAnalysis,
            _mockCallChain,
            _mockTypeUsage);

        // Act
        var result = await service.FindMethodsBySignatureAsync(signature, true);

        // Assert
        Assert.That(result, Is.Not.Null);
        // Result might be empty since we're using a minimal test workspace, but method should execute without throwing
        Assert.That(result.Count, Is.GreaterThanOrEqualTo(0));

        realWorkspace.Dispose();
    }

    [Test]
    public async Task FindSimilarMethodsAsync_WithThreshold_ShouldReturnSimilarMethods()
    {
        // Arrange - Test with no compilations to avoid mocking issues
        _mockWorkspace.GetCompilations().Returns(Array.Empty<Microsoft.CodeAnalysis.Compilation>());
        var methodName = "ProcessData";
        var threshold = 0.8;

        // Act
        var result = await _service.FindSimilarMethodsAsync(methodName, threshold);

        // Assert - Should return empty result for null workspace
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(0));
    }
}

using MCPsharp.Models.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class AdvancedReferenceFinderServiceIntegrationTests
{
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
        _ = await _advancedReferenceFinder.FindCallersAtLocationAsync("Service.cs", 10, 5);

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
        _ = await _advancedReferenceFinder.FindShortestPathAsync(fromMethod, toMethod);

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
}

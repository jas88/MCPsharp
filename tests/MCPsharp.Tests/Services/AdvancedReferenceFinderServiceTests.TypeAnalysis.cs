using MCPsharp.Models.Roslyn;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class AdvancedReferenceFinderServiceIntegrationTests
{
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
}

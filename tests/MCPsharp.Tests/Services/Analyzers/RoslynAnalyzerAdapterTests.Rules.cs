using NUnit.Framework;
using MCPsharp.Services.Analyzers;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class RoslynAnalyzerAdapterTests
{
    #region GetRules Tests

    [Test]
    public void GetRules_ShouldReturnSupportedDiagnostics()
    {
        // Arrange
        var analyzer = new TestDiagnosticProducingAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var rules = adapter.GetRules();

        // Assert
        Assert.That(rules.Length, Is.GreaterThan(0));

        var rule = rules[0];
        Assert.That(rule.Id, Is.EqualTo("TEST001"));
        Assert.That(rule.Title, Is.Not.Empty);
    }

    [Test]
    public void GetRules_WithAnalyzerWithoutDiagnostics_ShouldReturnEmpty()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer(); // No diagnostics
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var rules = adapter.GetRules();

        // Assert
        Assert.That(rules.Length, Is.EqualTo(0));
    }

    [Test]
    public void GetRules_ShouldMapCategoryCorrectly()
    {
        // Arrange
        var analyzer = new TestDiagnosticProducingAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var rules = adapter.GetRules();

        // Assert
        var rule = rules.FirstOrDefault(r => r.Id == "TEST001");
        Assert.That(rule, Is.Not.Null);
        Assert.That(rule!.Category, Is.EqualTo(RuleCategory.CodeQuality));
    }

    [Test]
    public void GetRules_ShouldMapSeverityCorrectly()
    {
        // Arrange
        var analyzer = new TestDiagnosticProducingAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var rules = adapter.GetRules();

        // Assert
        var rule = rules.FirstOrDefault(r => r.Id == "TEST001");
        Assert.That(rule, Is.Not.Null);
        Assert.That(rule!.DefaultSeverity, Is.EqualTo(IssueSeverity.Warning));
    }

    #endregion

    #region GetFixes Tests

    [Test]
    public void GetFixes_ShouldReturnEmpty()
    {
        // Arrange - Code fixes require separate CodeFixProvider infrastructure
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var fixes = adapter.GetFixes("TEST001");

        // Assert
        Assert.That(fixes.Length, Is.EqualTo(0));
    }

    #endregion

    #region GetCapabilities Tests

    [Test]
    public void GetCapabilities_ShouldReturnExpectedCapabilities()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var capabilities = adapter.GetCapabilities();

        // Assert
        Assert.That(capabilities, Is.Not.Null);
        Assert.That(capabilities.SupportedLanguages, Contains.Item("C#"));
        Assert.That(capabilities.SupportedFileTypes, Contains.Item(".cs"));
        Assert.That(capabilities.MaxFileSize, Is.EqualTo(10 * 1024 * 1024));
        Assert.That(capabilities.CanAnalyzeProjects, Is.True);
        Assert.That(capabilities.CanAnalyzeSolutions, Is.True);
        Assert.That(capabilities.SupportsParallelProcessing, Is.True);
        Assert.That(capabilities.CanFixIssues, Is.False);
    }

    #endregion

    #region DisposeAsync Tests

    [Test]
    public async Task DisposeAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act & Assert
        await Assert.DoesNotThrowAsync(async () => await adapter.DisposeAsync());
    }

    #endregion
}

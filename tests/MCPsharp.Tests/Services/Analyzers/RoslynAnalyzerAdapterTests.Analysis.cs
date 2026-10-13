using Microsoft.CodeAnalysis;
using NUnit.Framework;
using MCPsharp.Services.Analyzers;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class RoslynAnalyzerAdapterTests
{
    #region InitializeAsync Tests

    [Test]
    public async Task InitializeAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act & Assert
        await Assert.DoesNotThrowAsync(async () => await adapter.InitializeAsync());
    }

    [Test]
    public async Task InitializeAsync_WithCancellation_ShouldComplete()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - should complete even when cancelled
        await Assert.DoesNotThrowAsync(async () =>
            await adapter.InitializeAsync(cts.Token));
    }

    #endregion

    #region AnalyzeAsync Tests

    [Test]
    public async Task AnalyzeAsync_WithValidCode_ShouldReturnSuccessfulResult()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class ValidCode {}", ".cs");
        var content = "public class ValidCode {}";

        // Act
        var result = await adapter.AnalyzeAsync(filePath, content);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.FilePath, Is.EqualTo(filePath));
        Assert.That(result.AnalyzerId, Is.EqualTo(adapter.Id));
        Assert.That(result.Statistics, Is.Not.Null);
        Assert.That(result.Statistics["IssuesFound"], Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public async Task AnalyzeAsync_WithInvalidCode_ShouldStillSucceed()
    {
        // Arrange - Roslyn analyzers analyze syntax/semantic issues, not compilation errors
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("invalid syntax {{{{", ".cs");
        var content = "invalid syntax {{{{";

        // Act
        var result = await adapter.AnalyzeAsync(filePath, content);

        // Assert - Analysis completes even with invalid syntax
        Assert.That(result.Success, Is.True);
    }

    [Test]
    public async Task AnalyzeAsync_WithDiagnosticProducingAnalyzer_ShouldReportIssues()
    {
        // Arrange
        var analyzer = new TestDiagnosticProducingAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class TestClass {}", ".cs");
        var content = "public class TestClass {}";

        // Act
        var result = await adapter.AnalyzeAsync(filePath, content);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Issues.Length, Is.GreaterThan(0));

        var issue = result.Issues[0];
        Assert.That(issue.RuleId, Is.EqualTo("TEST001"));
        Assert.That(issue.AnalyzerId, Is.EqualTo(adapter.Id));
        Assert.That(issue.FilePath, Is.EqualTo(filePath));
        Assert.That(issue.Confidence, Is.EqualTo(Confidence.High));
    }

    [Test]
    public async Task AnalyzeAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class Test {}", ".cs");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await adapter.AnalyzeAsync(filePath, "public class Test {}", cts.Token));
    }

    [Test]
    public async Task AnalyzeAsync_ShouldSetTimestamps()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class Test {}", ".cs");
        var startTime = DateTime.UtcNow;

        // Act
        var result = await adapter.AnalyzeAsync(filePath, "public class Test {}");

        // Assert
        Assert.That(result.StartTime, Is.GreaterThanOrEqualTo(startTime));
        Assert.That(result.EndTime, Is.GreaterThanOrEqualTo(result.StartTime));
        Assert.That(result.Statistics["AnalysisDuration"], Is.Not.Null);
    }

    #endregion

    #region Diagnostic Mapping Tests

    [Test]
    public async Task AnalyzeAsync_ShouldMapLineNumbersCorrectly()
    {
        // Arrange
        var analyzer = new TestDiagnosticProducingAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var code = "public class Test\n{\n    // Test class\n}";
        var filePath = CreateTestFile(code, ".cs");

        // Act
        var result = await adapter.AnalyzeAsync(filePath, code);

        // Assert
        if (result.Issues.Any())
        {
            var issue = result.Issues[0];
            Assert.That(issue.LineNumber, Is.GreaterThan(0));
            Assert.That(issue.ColumnNumber, Is.GreaterThanOrEqualTo(0));
            Assert.That(issue.EndLineNumber, Is.GreaterThanOrEqualTo(issue.LineNumber));
        }
    }

    [Test]
    public async Task AnalyzeAsync_ShouldIncludeDiagnosticProperties()
    {
        // Arrange
        var analyzer = new TestDiagnosticProducingAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class Test {}", ".cs");

        // Act
        var result = await adapter.AnalyzeAsync(filePath, "public class Test {}");

        // Assert
        if (result.Issues.Any())
        {
            var issue = result.Issues[0];
            Assert.That(issue.Properties, Is.Not.Null);
            Assert.That(issue.Properties.ContainsKey("DiagnosticId"), Is.True);
            Assert.That(issue.Properties.ContainsKey("WarningLevel"), Is.True);
            Assert.That(issue.Properties.ContainsKey("IsSuppressed"), Is.True);
        }
    }

    #endregion
}

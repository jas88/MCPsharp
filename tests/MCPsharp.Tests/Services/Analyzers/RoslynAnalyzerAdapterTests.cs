using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Services.Analyzers;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
[Category("Roslyn")]
public class RoslynAnalyzerAdapterTests : FileServiceTestBase
{
    private ILogger<RoslynAnalyzerAdapter> _mockLogger = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _mockLogger = CreateNullLogger<RoslynAnalyzerAdapter>();
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithNullAnalyzer_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerAdapter(null!, _mockLogger));
    }

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerAdapter(analyzer, null!));
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldInitializeProperties()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();

        // Act
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Assert
        Assert.That(adapter.Id, Is.EqualTo("Roslyn_TestDiagnosticAnalyzer"));
        Assert.That(adapter.Name, Is.EqualTo("TestDiagnosticAnalyzer"));
        Assert.That(adapter.Description, Does.Contain("TestDiagnosticAnalyzer"));
        Assert.That(adapter.Version, Is.Not.Null);
        Assert.That(adapter.Author, Is.Not.Null);
        Assert.That(adapter.IsEnabled, Is.True);
        Assert.That(adapter.SupportedExtensions, Is.EquivalentTo(new[] { ".cs" }));
    }

    [Test]
    public void Constructor_WithAnalyzer_ShouldExtractVersionFromAssembly()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();

        // Act
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Assert
        Assert.That(adapter.Version, Is.Not.Null);
        Assert.That(adapter.Version, Is.GreaterThanOrEqualTo(new Version(1, 0, 0)));
    }

    #endregion

    #region CanAnalyze Tests

    [Test]
    public void CanAnalyze_WithNullPath_ShouldReturnFalse()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var result = adapter.CanAnalyze(null!);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanAnalyze_WithEmptyPath_ShouldReturnFalse()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var result = adapter.CanAnalyze(string.Empty);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanAnalyze_WithCSharpFile_ShouldReturnTrue()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class Test {}", ".cs");

        // Act
        var result = adapter.CanAnalyze(filePath);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CanAnalyze_WithNonCSharpFile_ShouldReturnFalse()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("console.log('test');", ".js");

        // Act
        var result = adapter.CanAnalyze(filePath);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanAnalyze_WithDirectoryContainingCSharpFiles_ShouldReturnTrue()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        CreateTestFile("public class Test {}", ".cs");

        // Act
        var result = adapter.CanAnalyze(TempDirectory);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CanAnalyze_WithDirectoryWithoutCSharpFiles_ShouldReturnFalse()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        CreateTestFile("{\"test\": true}", ".json");

        // Act
        var result = adapter.CanAnalyze(TempDirectory);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanAnalyze_WithNonExistentPath_ShouldReturnFalse()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act
        var result = adapter.CanAnalyze("/non/existent/path.cs");

        // Assert
        Assert.That(result, Is.False);
    }

    #endregion

    #region InitializeAsync Tests

    [Test]
    public void InitializeAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await adapter.InitializeAsync());
    }

    [Test]
    public void InitializeAsync_WithCancellation_ShouldComplete()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - should complete even when cancelled
        Assert.DoesNotThrowAsync(async () =>
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
        Assert.That(result.Issues, Is.Not.Null);
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
        Assert.That(result, Is.Not.Null);
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
        Assert.That(issue.Severity, Is.Not.Null);
        Assert.That(issue.Confidence, Is.EqualTo(Confidence.High));
    }

    [Test]
    public void AnalyzeAsync_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);
        var filePath = CreateTestFile("public class Test {}", ".cs");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
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
        Assert.That(rules, Is.Not.Null);
        Assert.That(rules.Length, Is.GreaterThan(0));

        var rule = rules[0];
        Assert.That(rule.Id, Is.EqualTo("TEST001"));
        Assert.That(rule.Title, Is.Not.Empty);
        Assert.That(rule.Category, Is.Not.Null);
        Assert.That(rule.DefaultSeverity, Is.Not.Null);
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
        Assert.That(rules, Is.Not.Null);
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
        Assert.That(fixes, Is.Not.Null);
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
    public void DisposeAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        var analyzer = new TestDiagnosticAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(analyzer, _mockLogger);

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await adapter.DisposeAsync());
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

    #region Helper Classes

    /// <summary>
    /// Test diagnostic analyzer that produces no diagnostics
    /// </summary>
    private class TestDiagnosticAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray<DiagnosticDescriptor>.Empty;

        public override void Initialize(AnalysisContext context)
        {
            // No analysis actions
        }
    }

    /// <summary>
    /// Test diagnostic analyzer that produces diagnostics
    /// </summary>
    private class TestDiagnosticProducingAnalyzer : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            id: "TEST001",
            title: "Test Diagnostic",
            messageFormat: "This is a test diagnostic",
            category: "Testing",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "A test diagnostic for unit testing.",
            helpLinkUri: "https://example.com/TEST001");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
        }

        private void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
        {
            // Report diagnostic for every syntax tree
            var diagnostic = Diagnostic.Create(Rule, Location.None);
            context.ReportDiagnostic(diagnostic);
        }
    }

    #endregion
}

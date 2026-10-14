using System.Collections.Immutable;
using MCPsharp.Services.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
[Category("Roslyn")]
public partial class RoslynAnalyzerAdapterTests : FileServiceTestBase
{
    private static readonly string[] ExpectedCSharpExtensions = [".cs"];

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
        Assert.That(adapter.SupportedExtensions, Is.EquivalentTo(ExpectedCSharpExtensions));
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

    #region Helper Classes

    /// <summary>
    /// Test diagnostic analyzer that produces no diagnostics
    /// </summary>
    private sealed class TestDiagnosticAnalyzer : DiagnosticAnalyzer
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
    private sealed class TestDiagnosticProducingAnalyzer : DiagnosticAnalyzer
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

using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
[Category("Roslyn")]
public partial class RoslynAnalyzerServiceTests : FileServiceTestBase
{
    private static readonly string[] CSharpLanguages = ["C#"];
    private static readonly string[] CSharpFileTypes = [".cs"];

    private static readonly string[] SingleAnalyzerId = ["analyzer1"];
    private static readonly string[] NonExistentAnalyzerId = ["non-existent"];

    private RoslynAnalyzerLoader _mockLoader = null!;
    private IAnalyzerHost _mockAnalyzerHost = null!;
    private ILogger<RoslynAnalyzerService> _mockLogger = null!;
    private RoslynAnalyzerService _service = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();

        _mockLoader = Substitute.For<RoslynAnalyzerLoader>(
            CreateNullLogger<RoslynAnalyzerLoader>(),
            CreateNullLoggerFactory());
        _mockAnalyzerHost = Substitute.For<IAnalyzerHost>();
        _mockLogger = CreateNullLogger<RoslynAnalyzerService>();

        _service = new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_WithNullLoader_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerService(null!, _mockAnalyzerHost, _mockLogger));
    }

    [Test]
    public void Constructor_WithNullAnalyzerHost_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerService(_mockLoader, null!, _mockLogger));
    }

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, null!));
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrow(() =>
            new RoslynAnalyzerService(_mockLoader, _mockAnalyzerHost, _mockLogger));
    }

    #endregion

    #region GetLoadedAnalyzers Tests

    [Test]
    public void GetLoadedAnalyzers_Initially_ShouldReturnEmpty()
    {
        // Act
        var result = _service.GetLoadedAnalyzers();

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task GetLoadedAnalyzers_AfterLoading_ShouldReturnLoadedAnalyzers()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test", ".dll");
        var analyzer = CreateMockAnalyzer("test-analyzer");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));

        await _service.LoadAnalyzersAsync(assemblyPath);

        // Act
        var result = _service.GetLoadedAnalyzers();

        // Assert
        Assert.That(result.Length, Is.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo("test-analyzer"));
    }

    #endregion

    #region GetAnalyzer Tests

    [Test]
    public void GetAnalyzer_WithNonExistentId_ShouldReturnNull()
    {
        // Act
        var result = _service.GetAnalyzer("non-existent");

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAnalyzer_WithLoadedAnalyzer_ShouldReturnAnalyzer()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test", ".dll");
        var analyzer = CreateMockAnalyzer("test-analyzer");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));

        await _service.LoadAnalyzersAsync(assemblyPath);

        // Act
        var result = _service.GetAnalyzer("test-analyzer");

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo("test-analyzer"));
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public async Task LoadAnalyzersAsync_ConcurrentCalls_ShouldBeThreadSafe()
    {
        // Arrange
        var assemblyPath1 = CreateTestFile("test1", ".dll");
        var assemblyPath2 = CreateTestFile("test2", ".dll");

        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath1, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer1));
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath2, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer2));

        // Act - Load concurrently
        var task1 = _service.LoadAnalyzersAsync(assemblyPath1);
        var task2 = _service.LoadAnalyzersAsync(assemblyPath2);
        await Task.WhenAll(task1, task2);

        // Assert
        var loaded = _service.GetLoadedAnalyzers();
        Assert.That(loaded.Length, Is.EqualTo(2));
    }

    #endregion

    #region Helper Methods

    private static IAnalyzer CreateMockAnalyzer(string id)
    {
        var analyzer = Substitute.For<IAnalyzer>();

        // Configure properties (non-arg-matching returns)
        analyzer.Id.Returns(id);
        analyzer.Name.Returns($"Test Analyzer {id}");
        analyzer.Description.Returns($"Test description for {id}");
        analyzer.Version.Returns(new Version(1, 0, 0));
        analyzer.Author.Returns("Test Author");
        analyzer.IsEnabled.Returns(true);
        analyzer.SupportedExtensions.Returns(ImmutableArray.Create(".cs"));
        analyzer.Configuration.Returns(new AnalyzerConfiguration());

        analyzer.CanAnalyze(Arg.Any<string>()).Returns(true);
        analyzer.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AnalysisResult
            {
                Success = true,
                FilePath = string.Empty,
                AnalyzerId = id,
                Issues = ImmutableArray<AnalyzerIssue>.Empty
            }));
        analyzer.GetRules().Returns(ImmutableArray<AnalyzerRule>.Empty);
        analyzer.GetFixes(Arg.Any<string>()).Returns(ImmutableArray<AnalyzerFix>.Empty);
        analyzer.GetCapabilities().Returns(new AnalyzerCapabilities
        {
            SupportedLanguages = CSharpLanguages,
            SupportedFileTypes = CSharpFileTypes
        });
        analyzer.DisposeAsync().Returns(Task.CompletedTask);

        return analyzer;
    }

    #endregion
}

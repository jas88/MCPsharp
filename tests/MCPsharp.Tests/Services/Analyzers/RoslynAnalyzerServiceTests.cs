using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
public class RoslynAnalyzerServiceTests : FileServiceTestBase
{
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

    #region LoadAnalyzersAsync Tests

    [Test]
    public async Task LoadAnalyzersAsync_WithValidAssembly_ShouldLoadAndRegisterAnalyzers()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");
        var analyzers = ImmutableArray.Create(analyzer1, analyzer2);

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(analyzers);

        // Act
        var result = await _service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result[0].Id, Is.EqualTo("analyzer1"));
        Assert.That(result[1].Id, Is.EqualTo("analyzer2"));

        await _mockLoader.Received(1).LoadAnalyzersFromAssemblyAsync(
            assemblyPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithFailure_ShouldReturnEmpty()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray<IAnalyzer>.Empty);

        // Act
        var result = await _service.LoadAnalyzersAsync(assemblyPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersAsync_ShouldStoreLoadedAnalyzers()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        var analyzer = CreateMockAnalyzer("test-analyzer");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));

        // Act
        await _service.LoadAnalyzersAsync(assemblyPath);
        var loadedAnalyzers = _service.GetLoadedAnalyzers();

        // Assert
        Assert.That(loadedAnalyzers.Length, Is.EqualTo(1));
        Assert.That(loadedAnalyzers[0].Id, Is.EqualTo("test-analyzer"));
    }

    [Test]
    public async Task LoadAnalyzersAsync_WithCancellation_ShouldReturnEmpty()
    {
        // Arrange
        var assemblyPath = CreateTestFile("test assembly", ".dll");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns<ImmutableArray<IAnalyzer>>(callInfo => throw new OperationCanceledException());

        // Act
        var result = await _service.LoadAnalyzersAsync(assemblyPath, cts.Token);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    #endregion

    #region LoadAnalyzersFromDirectoryAsync Tests

    [Test]
    public async Task LoadAnalyzersFromDirectoryAsync_WithValidDirectory_ShouldLoadAllAnalyzers()
    {
        // Arrange
        var directory = CreateTestDirectory("analyzers");
        var assembly1 = Path.Combine(directory, "analyzer1.dll");
        var assembly2 = Path.Combine(directory, "analyzer2.dll");
        File.WriteAllText(assembly1, "test");
        File.WriteAllText(assembly2, "test");

        var assemblies = ImmutableArray.Create(assembly1, assembly2);
        _mockLoader.DiscoverAnalyzerAssemblies(directory, true)
            .Returns(assemblies);

        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");

        _mockLoader.LoadAnalyzersFromAssemblyAsync(assembly1, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer1));
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assembly2, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer2));

        // Act
        var result = await _service.LoadAnalyzersFromDirectoryAsync(directory);

        // Assert
        Assert.That(result.Length, Is.EqualTo(2));
        _mockLoader.Received(1).DiscoverAnalyzerAssemblies(directory, true);
    }

    [Test]
    public async Task LoadAnalyzersFromDirectoryAsync_WithEmptyDirectory_ShouldReturnEmpty()
    {
        // Arrange
        var directory = CreateTestDirectory("empty");

        _mockLoader.DiscoverAnalyzerAssemblies(directory, true)
            .Returns(ImmutableArray<string>.Empty);

        // Act
        var result = await _service.LoadAnalyzersFromDirectoryAsync(directory);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAnalyzersFromDirectoryAsync_WithError_ShouldReturnEmpty()
    {
        // Arrange
        var directory = CreateTestDirectory("error");

        _mockLoader.DiscoverAnalyzerAssemblies(directory, true)
            .Returns(x => throw new UnauthorizedAccessException());

        // Act
        var result = await _service.LoadAnalyzersFromDirectoryAsync(directory);

        // Assert
        Assert.That(result.Length, Is.EqualTo(0));
    }

    #endregion

    #region RunAnalyzersAsync Tests

    [Test]
    public async Task RunAnalyzersAsync_WithNoLoadedAnalyzers_ShouldReturnFailure()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");

        // Act
        var result = await _service.RunAnalyzersAsync(targetPath);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("No analyzers"));
    }

    [Test]
    public async Task RunAnalyzersAsync_WithLoadedAnalyzers_ShouldRunAll()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");
        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");

        // Load analyzers
        var assemblyPath = CreateTestFile("test", ".dll");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer1, analyzer2));
        await _service.LoadAnalyzersAsync(assemblyPath);

        // Setup analyzer host
        _mockAnalyzerHost.RunAnalyzerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(new AnalyzerResult
            {
                Success = true,
                Findings = new System.Collections.Generic.List<Finding>()
            });

        // Act
        var result = await _service.RunAnalyzersAsync(targetPath);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.AnalyzersRun.Length, Is.EqualTo(2));
        await _mockAnalyzerHost.Received(2).RunAnalyzerAsync(
            Arg.Any<string>(),
            targetPath,
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzersAsync_WithSpecificAnalyzerIds_ShouldRunOnlySpecified()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");
        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");

        // Load analyzers
        var assemblyPath = CreateTestFile("test", ".dll");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer1, analyzer2));
        await _service.LoadAnalyzersAsync(assemblyPath);

        // Setup analyzer host
        _mockAnalyzerHost.RunAnalyzerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(new AnalyzerResult
            {
                Success = true,
                Findings = new System.Collections.Generic.List<Finding>()
            });

        // Act - Run only analyzer1
        var result = await _service.RunAnalyzersAsync(
            targetPath,
            analyzerIds: new[] { "analyzer1" });

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.AnalyzersRun.Length, Is.EqualTo(1));
        Assert.That(result.AnalyzersRun[0], Is.EqualTo("analyzer1"));

        await _mockAnalyzerHost.Received(1).RunAnalyzerAsync(
            "analyzer1",
            targetPath,
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzersAsync_WithNonExistentAnalyzerId_ShouldSkipIt()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");
        var analyzer = CreateMockAnalyzer("analyzer1");

        var assemblyPath = CreateTestFile("test", ".dll");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));
        await _service.LoadAnalyzersAsync(assemblyPath);

        // Act - Try to run non-existent analyzer
        var result = await _service.RunAnalyzersAsync(
            targetPath,
            analyzerIds: new[] { "non-existent" });

        // Assert
        Assert.That(result.Success, Is.False);
        await _mockAnalyzerHost.DidNotReceive().RunAnalyzerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzersAsync_WithCancellation_ShouldReturnCancelledResult()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");
        var analyzer = CreateMockAnalyzer("analyzer1");

        var assemblyPath = CreateTestFile("test", ".dll");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));
        await _service.LoadAnalyzersAsync(assemblyPath);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockAnalyzerHost.RunAnalyzerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>())
            .Returns<AnalyzerResult>(x => throw new OperationCanceledException());

        // Act
        var result = await _service.RunAnalyzersAsync(targetPath, cancellationToken: cts.Token);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("cancelled").IgnoreCase);
    }

    [Test]
    public async Task RunAnalyzersAsync_WithAnalyzerError_ShouldContinueWithOthers()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");
        var analyzer1 = CreateMockAnalyzer("analyzer1");
        var analyzer2 = CreateMockAnalyzer("analyzer2");

        var assemblyPath = CreateTestFile("test", ".dll");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer1, analyzer2));
        await _service.LoadAnalyzersAsync(assemblyPath);

        // First analyzer throws, second succeeds
        _mockAnalyzerHost.RunAnalyzerAsync("analyzer1", Arg.Any<string>(), Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>())
            .Returns<AnalyzerResult>(x => throw new InvalidOperationException("Test error"));
        _mockAnalyzerHost.RunAnalyzerAsync("analyzer2", Arg.Any<string>(), Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>())
            .Returns(new AnalyzerResult { Success = true, Findings = new System.Collections.Generic.List<Finding>() });

        // Act
        var result = await _service.RunAnalyzersAsync(targetPath);

        // Assert
        Assert.That(result.Success, Is.True);
        // Both should have been attempted
        await _mockAnalyzerHost.Received(2).RunAnalyzerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzersAsync_ShouldSetTimestamps()
    {
        // Arrange
        var targetPath = CreateTestFile("public class Test {}", ".cs");
        var analyzer = CreateMockAnalyzer("analyzer1");

        var assemblyPath = CreateTestFile("test", ".dll");
        _mockLoader.LoadAnalyzersFromAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(analyzer));
        await _service.LoadAnalyzersAsync(assemblyPath);

        _mockAnalyzerHost.RunAnalyzerAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<AnalyzerOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(new AnalyzerResult { Success = true, Findings = new System.Collections.Generic.List<Finding>() });

        var startTime = DateTime.UtcNow;

        // Act
        var result = await _service.RunAnalyzersAsync(targetPath);

        // Assert
        Assert.That(result.StartTime, Is.GreaterThanOrEqualTo(startTime));
        Assert.That(result.EndTime, Is.GreaterThanOrEqualTo(result.StartTime));
        Assert.That(result.Statistics, Is.Not.Null);
        Assert.That(result.Statistics!.ContainsKey("Duration"), Is.True);
    }

    #endregion

    #region GetLoadedAnalyzers Tests

    [Test]
    public void GetLoadedAnalyzers_Initially_ShouldReturnEmpty()
    {
        // Act
        var result = _service.GetLoadedAnalyzers();

        // Assert
        Assert.That(result, Is.Not.Null);
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

    private IAnalyzer CreateMockAnalyzer(string id)
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
            SupportedLanguages = new[] { "C#" },
            SupportedFileTypes = new[] { ".cs" }
        });
        analyzer.DisposeAsync().Returns(Task.CompletedTask);

        return analyzer;
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
public class AnalyzerHostTests : FileServiceTestBase
{
    private ISecurityManager _mockSecurityManager = null!;
    private IAnalyzerRegistry _mockAnalyzerRegistry = null!;
    private IAnalyzerSandbox _mockSandbox = null!;
    private ILogger<AnalyzerHost> _mockLogger = null!;
    private AnalyzerHost _analyzerHost = null!;

    [SetUp]
    public override void Setup()
    {
        base.Setup();
        _mockSecurityManager = Substitute.For<ISecurityManager>();
        _mockAnalyzerRegistry = Substitute.For<IAnalyzerRegistry>();
        _mockSandbox = Substitute.For<IAnalyzerSandbox>();
        _mockLogger = CreateNullLogger<AnalyzerHost>();

        _analyzerHost = new AnalyzerHost(_mockSecurityManager, _mockAnalyzerRegistry, _mockSandbox, _mockLogger);
    }

    [Test]
    public void Constructor_WithNullDependencies_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(null!, _mockAnalyzerRegistry, _mockSandbox, _mockLogger));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockSecurityManager, null!, _mockSandbox, _mockLogger));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockSecurityManager, _mockAnalyzerRegistry, null!, _mockLogger));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockSecurityManager, _mockAnalyzerRegistry, _mockSandbox, null!));
    }

    [Test]
    public void Constructor_WithValidDependencies_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => new AnalyzerHost(
            _mockSecurityManager,
            _mockAnalyzerRegistry,
            _mockSandbox,
            _mockLogger));
    }

    [Test]
    public async Task LoadAnalyzerAsync_WithValidAssembly_ShouldLoadSuccessfully()
    {
        // Arrange
        var assemblyPath = CreateTestFile("dummy assembly content", ".dll");
        var analyzerId = "test-analyzer";
        var expectedAnalyzer = Substitute.For<IAnalyzer>();
        expectedAnalyzer.Id.Returns(analyzerId);
        expectedAnalyzer.Name.Returns("Test Analyzer");
        expectedAnalyzer.Version.Returns("1.0.0");

        _mockSecurityManager.ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(new SecurityValidationResult { IsValid = true });
        _mockSandbox.LoadAnalyzerAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(expectedAnalyzer);

        // Act
        var result = await _analyzerHost.LoadAnalyzerAsync(assemblyPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Analyzer, Is.EqualTo(expectedAnalyzer));
        Assert.That(result.AnalyzerId, Is.EqualTo(analyzerId));

        await _mockSecurityManager.Received(1).ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>());
        await _mockSandbox.Received(1).LoadAnalyzerAsync(assemblyPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAnalyzerAsync_WithInvalidAssembly_ShouldFailSecurityValidation()
    {
        // Arrange
        var assemblyPath = CreateTestFile("malicious content", ".dll");

        _mockSecurityManager.ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(new SecurityValidationResult
            {
                IsValid = false,
                Issues = new[] { "Security violation detected" }
            });

        // Act
        var result = await _analyzerHost.LoadAnalyzerAsync(assemblyPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Security"));

        await _mockSecurityManager.Received(1).ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>());
        await _mockSandbox.DidNotReceive().LoadAnalyzerAsync(assemblyPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAnalyzerAsync_WithNonExistentFile_ShouldFail()
    {
        // Arrange
        var nonExistentPath = "/non/existent/analyzer.dll";

        // Act
        var result = await _analyzerHost.LoadAnalyzerAsync(nonExistentPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not found").IgnoreCase);
    }

    [Test]
    public async Task UnloadAnalyzerAsync_WithLoadedAnalyzer_ShouldUnloadSuccessfully()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = Substitute.For<IAnalyzer>();
        analyzer.Id.Returns(analyzerId);

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

        // Act
        var result = await _analyzerHost.UnloadAnalyzerAsync(analyzerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);

        _mockAnalyzerRegistry.Received(1).IsAnalyzerLoaded(analyzerId);
        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
        await _mockSandbox.Received(1).UnloadAnalyzerAsync(analyzer);
        _mockAnalyzerRegistry.Received(1).UnregisterAnalyzer(analyzerId);
    }

    [Test]
    public async Task UnloadAnalyzerAsync_WithNonExistentAnalyzer_ShouldFail()
    {
        // Arrange
        var analyzerId = "non-existent-analyzer";

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(false);

        // Act
        var result = await _analyzerHost.UnloadAnalyzerAsync(analyzerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not loaded"));

        _mockAnalyzerRegistry.Received(1).IsAnalyzerLoaded(analyzerId);
        _mockAnalyzerRegistry.DidNotReceive().GetAnalyzer(analyzerId);
    }

    [Test]
    public async Task RunAnalyzerAsync_WithValidParameters_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        var options = new AnalyzerOptions
        {
            EnableSemanticAnalysis = true,
            EnablePerformanceAnalysis = false
        };

        var analyzer = Substitute.For<IAnalyzer>();
        var expectedResult = new AnalyzerResult
        {
            Success = true,
            AnalyzerId = analyzerId,
            Findings = new List<Finding>
            {
                new() { Severity = FindingSeverity.Info, Message = "Test finding" }
            }
        };

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(targetPath, options, Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath, options);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.True);
        Assert.That(result.Findings.Count, Is.EqualTo(1));
        Assert.That(result.AnalyzerId, Is.EqualTo(analyzerId));
    }

    [Test]
    public async Task RunAnalyzerAsync_WithNonLoadedAnalyzer_ShouldFail()
    {
        // Arrange
        var analyzerId = "non-loaded-analyzer";
        var targetPath = CreateTestDirectory("target");

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(false);

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("not loaded"));
    }

    [Test]
    public async Task RunAnalyzerAsync_WithAnalyzerThatCannotAnalyze_ShouldFail()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(false);

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("cannot analyze"));

        analyzer.DidNotReceive().AnalyzeAsync(Arg.Any<string>(), Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzerAsync_WithAnalysisException_ShouldHandleGracefully()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(targetPath, Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AnalyzerResult>(new InvalidOperationException("Analysis failed")));

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Analysis failed"));
    }

    [Test]
    public async Task GetLoadedAnalyzersAsync_ShouldReturnLoadedAnalyzers()
    {
        // Arrange
        var expectedAnalyzers = new List<IAnalyzer>
        {
            CreateMockAnalyzer("analyzer-1", "Analyzer 1", "1.0.0"),
            CreateMockAnalyzer("analyzer-2", "Analyzer 2", "2.0.0")
        };

        _mockAnalyzerRegistry.GetLoadedAnalyzers().Returns(expectedAnalyzers);

        // Act
        var result = await _analyzerHost.GetLoadedAnalyzersAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result.Any(a => a.Id == "analyzer-1"), Is.True);
        Assert.That(result.Any(a => a.Id == "analyzer-2"), Is.True);

        _mockAnalyzerRegistry.Received(1).GetLoadedAnalyzers();
    }

    [Test]
    public async Task GetAnalyzerCapabilitiesAsync_ShouldReturnCapabilities()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = Substitute.For<IAnalyzer>();
        var expectedCapabilities = new AnalyzerCapabilities
        {
            SupportedLanguages = new[] { "C#", "VB.NET" },
            SupportedFileTypes = new[] { ".cs", ".vb" },
            MaxFileSize = 10 * 1024 * 1024,
            CanAnalyzeProjects = true,
            CanAnalyzeSolutions = true
        };

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.GetCapabilities().Returns(expectedCapabilities);

        // Act
        var result = await _analyzerHost.GetAnalyzerCapabilitiesAsync(analyzerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.SupportedLanguages, Is.EqualTo(expectedCapabilities.SupportedLanguages));
        Assert.That(result.SupportedFileTypes, Is.EqualTo(expectedCapabilities.SupportedFileTypes));
        Assert.That(result.MaxFileSize, Is.EqualTo(expectedCapabilities.MaxFileSize));
    }

    [Test]
    public async Task GetAnalyzerCapabilitiesAsync_WithNonLoadedAnalyzer_ShouldFail()
    {
        // Arrange
        var analyzerId = "non-loaded-analyzer";

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(false);

        // Act
        var result = await _analyzerHost.GetAnalyzerCapabilitiesAsync(analyzerId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RunMultipleAnalyzersAsync_ShouldExecuteAllAnalyzers()
    {
        // Arrange
        var analyzerIds = new[] { "analyzer-1", "analyzer-2", "analyzer-3" };
        var targetPath = CreateTestDirectory("target");
        var options = new AnalyzerOptions();

        var analyzers = analyzerIds.Select(id => CreateMockAnalyzer(id, $"Analyzer {id}", "1.0.0")).ToArray();
        var expectedResults = analyzerIds.Select(id => new AnalyzerResult
        {
            Success = true,
            AnalyzerId = id,
            Findings = new List<Finding> { new() { Message = $"Finding from {id}" } }
        }).ToArray();

        for (int i = 0; i < analyzerIds.Length; i++)
        {
            _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerIds[i]).Returns(true);
            _mockAnalyzerRegistry.GetAnalyzer(analyzerIds[i]).Returns(analyzers[i]);
            analyzers[i].CanAnalyze(targetPath).Returns(true);
            analyzers[i].AnalyzeAsync(targetPath, options, Arg.Any<CancellationToken>())
                .Returns(expectedResults[i]);
        }

        // Act
        var results = await _analyzerHost.RunMultipleAnalyzersAsync(analyzerIds, targetPath, options);

        // Assert
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.All(r => r.Success), Is.True);
        Assert.That(results.Select(r => r.AnalyzerId), Is.EquivalentTo(analyzerIds));
    }

    [Test]
    public async Task RunMultipleAnalyzersAsync_WithPartialFailure_ShouldReturnPartialResults()
    {
        // Arrange
        var analyzerIds = new[] { "working-analyzer", "failing-analyzer", "working-analyzer-2" };
        var targetPath = CreateTestDirectory("target");
        var options = new AnalyzerOptions();

        var workingAnalyzer1 = CreateMockAnalyzer("working-analyzer", "Working Analyzer", "1.0.0");
        var failingAnalyzer = CreateMockAnalyzer("failing-analyzer", "Failing Analyzer", "1.0.0");
        var workingAnalyzer2 = CreateMockAnalyzer("working-analyzer-2", "Working Analyzer 2", "1.0.0");

        _mockAnalyzerRegistry.IsAnalyzerLoaded("working-analyzer").Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer("working-analyzer").Returns(workingAnalyzer1);
        workingAnalyzer1.CanAnalyze(targetPath).Returns(true);
        workingAnalyzer1.AnalyzeAsync(targetPath, options, Arg.Any<CancellationToken>())
            .Returns(new AnalyzerResult { Success = true, AnalyzerId = "working-analyzer" });

        _mockAnalyzerRegistry.IsAnalyzerLoaded("failing-analyzer").Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer("failing-analyzer").Returns(failingAnalyzer);
        failingAnalyzer.CanAnalyze(targetPath).Returns(true);
        failingAnalyzer.AnalyzeAsync(targetPath, options, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AnalyzerResult>(new InvalidOperationException("Failed")));

        _mockAnalyzerRegistry.IsAnalyzerLoaded("working-analyzer-2").Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer("working-analyzer-2").Returns(workingAnalyzer2);
        workingAnalyzer2.CanAnalyze(targetPath).Returns(true);
        workingAnalyzer2.AnalyzeAsync(targetPath, options, Arg.Any<CancellationToken>())
            .Returns(new AnalyzerResult { Success = true, AnalyzerId = "working-analyzer-2" });

        // Act
        var results = await _analyzerHost.RunMultipleAnalyzersAsync(analyzerIds, targetPath, options);

        // Assert
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.Count(r => r.Success), Is.EqualTo(2));
        Assert.That(results.Count(r => !r.Success), Is.EqualTo(1));
        Assert.That(results.First(r => !r.Success).AnalyzerId, Is.EqualTo("failing-analyzer"));
    }

    [Test]
    public async Task ValidateAnalyzerAsync_ShouldReturnValidationResult()
    {
        // Arrange
        var assemblyPath = CreateTestFile("valid assembly", ".dll");
        var expectedResult = new AnalyzerValidationResult
        {
            IsValid = true,
            AnalyzerId = "validated-analyzer",
            Name = "Valid Analyzer",
            Version = "1.0.0",
            SecurityValidation = new SecurityValidationResult { IsValid = true },
            CompatibilityValidation = new CompatibilityValidationResult { IsCompatible = true }
        };

        _mockSecurityManager.ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(expectedResult.SecurityValidation);
        _mockSandbox.ValidateAnalyzerAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        // Act
        var result = await _analyzerHost.ValidateAnalyzerAsync(assemblyPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.IsValid, Is.EqualTo(expectedResult.IsValid));
        Assert.That(result.AnalyzerId, Is.EqualTo(expectedResult.AnalyzerId));

        await _mockSecurityManager.Received(1).ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>());
        await _mockSandbox.Received(1).ValidateAnalyzerAsync(assemblyPath, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAnalysisStatisticsAsync_ShouldReturnAccurateStats()
    {
        // Arrange
        var expectedStats = new AnalysisStatistics
        {
            TotalAnalysesRun = 15,
            SuccessfulAnalyses = 12,
            FailedAnalyses = 3,
            AverageAnalysisTime = TimeSpan.FromSeconds(2.5),
            TotalFindingsGenerated = 48,
            LoadedAnalyzersCount = 3
        };

        _mockAnalyzerRegistry.GetStatistics().Returns(expectedStats);

        // Act
        var result = await _analyzerHost.GetAnalysisStatisticsAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.TotalAnalysesRun, Is.EqualTo(expectedStats.TotalAnalysesRun));
        Assert.That(result.SuccessfulAnalyses, Is.EqualTo(expectedStats.SuccessfulAnalyses));
        Assert.That(result.LoadedAnalyzersCount, Is.EqualTo(expectedStats.LoadedAnalyzersCount));

        _mockAnalyzerRegistry.Received(1).GetStatistics();
    }

    [Test]
    public async Task Dispose_ShouldCleanupResources()
    {
        // Arrange
        var analyzer = CreateMockAnalyzer("test-analyzer", "Test Analyzer", "1.0.0");
        _mockAnalyzerRegistry.GetLoadedAnalyzers().Returns(new[] { analyzer });

        // Act
        await _analyzerHost.DisposeAsync();

        // Assert
        await _mockSandbox.Received(1).UnloadAnalyzerAsync(analyzer);
        _mockAnalyzerRegistry.Received(1).UnregisterAnalyzer(analyzer.Id);
    }

    [Test]
    public async Task Cancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var analyzerId = "slow-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath, cancellationToken: cts.Token));

        Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _analyzerHost.LoadAnalyzerAsync("/test/path", cts.Token));
    }

    [Test]
    [Repeat(3)] // Test multiple times for consistency
    public async Task RunAnalyzerAsync_WithSameInput_ShouldProduceConsistentResults()
    {
        // Arrange
        var analyzerId = "consistent-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();
        var expectedResult = new AnalyzerResult
        {
            Success = true,
            AnalyzerId = analyzerId,
            Findings = new List<Finding> { new() { Message = "Consistent finding" } }
        };

        _mockAnalyzerRegistry.IsAnalyzerLoaded(analyzerId).Returns(true);
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(targetPath, Arg.Any<AnalyzerOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedResult);

        // Act
        var result1 = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);
        var result2 = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result1.Success, Is.EqualTo(result2.Success));
        Assert.That(result1.AnalyzerId, Is.EqualTo(result2.AnalyzerId));
        Assert.That(result1.Findings.Count, Is.EqualTo(result2.Findings.Count));
    }

    private IAnalyzer CreateMockAnalyzer(string id, string name, string version)
    {
        var analyzer = Substitute.For<IAnalyzer>();
        analyzer.Id.Returns(id);
        analyzer.Name.Returns(name);
        analyzer.Version.Returns(version);
        analyzer.GetCapabilities().Returns(new AnalyzerCapabilities
        {
            SupportedLanguages = new[] { "C#" },
            SupportedFileTypes = new[] { ".cs" }
        });
        return analyzer;
    }
}
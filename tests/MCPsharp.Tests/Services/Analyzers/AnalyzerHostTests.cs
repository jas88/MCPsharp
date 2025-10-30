using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
    private IFixEngine _mockFixEngine = null!;
    private ILogger<AnalyzerHost> _mockLogger = null!;
    private ILoggerFactory _mockLoggerFactory = null!;
    private AnalyzerHost _analyzerHost = null!;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();
        _mockSecurityManager = Substitute.For<ISecurityManager>();
        _mockAnalyzerRegistry = Substitute.For<IAnalyzerRegistry>();
        _mockSandbox = Substitute.For<IAnalyzerSandbox>();
        _mockFixEngine = Substitute.For<IFixEngine>();
        _mockLogger = CreateNullLogger<AnalyzerHost>();
        _mockLoggerFactory = CreateNullLoggerFactory();

        _analyzerHost = new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine);
    }

    [Test]
    public void Constructor_WithNullDependencies_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(null!, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, null!, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, null!, _mockSecurityManager, _mockFixEngine));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, null!, _mockFixEngine));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, null!));
    }

    [Test]
    public void Constructor_WithValidDependencies_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => new AnalyzerHost(
            _mockLogger,
            _mockLoggerFactory,
            _mockAnalyzerRegistry,
            _mockSecurityManager,
            _mockFixEngine));
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
        expectedAnalyzer.Version.Returns(new Version("1.0.0"));

        _mockSecurityManager.ValidateAssemblyAsync(assemblyPath, Arg.Any<CancellationToken>())
            .Returns(new SecurityValidationResult { IsValid = true, IsSigned = false, IsTrusted = false, HasMaliciousPatterns = false });
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
                Violations = ImmutableArray.Create("Security violation detected")
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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

        // Act
        var result = await _analyzerHost.UnloadAnalyzerAsync(analyzerId);

        // Assert
        Assert.That(result, Is.True);

        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
        _mockAnalyzerRegistry.Received(1).UnregisterAnalyzerAsync(analyzerId);
    }

    [Test]
    public async Task UnloadAnalyzerAsync_WithNonExistentAnalyzer_ShouldFail()
    {
        // Arrange
        var analyzerId = "non-existent-analyzer";

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns((IAnalyzer?)null);

        // Act
        var result = await _analyzerHost.UnloadAnalyzerAsync(analyzerId);

        // Assert
        Assert.That(result, Is.False);

        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
        _mockAnalyzerRegistry.DidNotReceive().UnregisterAnalyzerAsync(analyzerId);
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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(Substitute.For<IAnalyzer>());
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisResult
            {
                Success = true,
                AnalyzerId = analyzerId,
                Issues = ImmutableArray.Create(new AnalyzerIssue
                {
                    RuleId = "test-rule",
                    Title = "Test Finding",
                    Description = "Test finding",
                    Severity = IssueSeverity.Info,
                    FilePath = targetPath,
                    LineNumber = 1,
                    ColumnNumber = 1
                })
            });

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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns((IAnalyzer?)null);

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("not loaded"));
    }

    [Test]
    public async Task RunAnalyzerAsync_WithAnalyzerThatCannotAnalyze_ShouldFail()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(Substitute.For<IAnalyzer>());
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(false);

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("cannot analyze"));

        analyzer.DidNotReceive().AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunAnalyzerAsync_WithAnalysisException_ShouldHandleGracefully()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(Substitute.For<IAnalyzer>());
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AnalysisResult>(new InvalidOperationException("Analysis failed")));

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Analysis failed"));
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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(Substitute.For<IAnalyzer>());
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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns((IAnalyzer?)null);

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
            _mockAnalyzerRegistry.GetAnalyzer(analyzerIds[i]).Returns(analyzers[i]);
            analyzers[i].CanAnalyze(targetPath).Returns(true);
            analyzers[i].AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new AnalysisResult
                {
                    Success = true,
                    AnalyzerId = analyzerIds[i],
                    Issues = ImmutableArray.Create(new AnalyzerIssue
                    {
                        RuleId = "test-rule",
                        Title = $"Finding from {analyzerIds[i]}",
                        Description = $"Finding from {analyzerIds[i]}",
                        Severity = IssueSeverity.Info,
                        FilePath = targetPath,
                        LineNumber = 1,
                        ColumnNumber = 1
                    })
                });
        }

        // Act
        var results = new List<AnalyzerResult>();
        foreach (var analyzerId in analyzerIds)
        {
            var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath, options);
            results.Add(result);
        }

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

        _mockAnalyzerRegistry.GetAnalyzer("working-analyzer").Returns(workingAnalyzer1);
        workingAnalyzer1.CanAnalyze(targetPath).Returns(true);
        workingAnalyzer1.AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisResult { Success = true, AnalyzerId = "working-analyzer" });

        _mockAnalyzerRegistry.GetAnalyzer("failing-analyzer").Returns(failingAnalyzer);
        failingAnalyzer.CanAnalyze(targetPath).Returns(true);
        failingAnalyzer.AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AnalysisResult>(new InvalidOperationException("Failed")));

        _mockAnalyzerRegistry.GetAnalyzer("working-analyzer-2").Returns(workingAnalyzer2);
        workingAnalyzer2.CanAnalyze(targetPath).Returns(true);
        workingAnalyzer2.AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisResult { Success = true, AnalyzerId = "working-analyzer-2" });

        // Act
        var results = new List<AnalyzerResult>();
        foreach (var analyzerId in analyzerIds)
        {
            var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath, options);
            results.Add(result);
        }

        // Assert
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Count, Is.EqualTo(3));
        Assert.That(results.Count(r => r.Success), Is.EqualTo(2));
        Assert.That(results.Count(r => !r.Success), Is.EqualTo(1));
        Assert.That(results.First(r => !r.Success).AnalyzerId, Is.EqualTo("failing-analyzer"));
    }

    [Test]
    public void GetLoadedAnalyzers_ShouldReturnLoadedAnalyzers()
    {
        // Arrange
        var expectedAnalyzers = new[]
        {
            CreateMockAnalyzer("analyzer-1", "Analyzer 1", "1.0.0"),
            CreateMockAnalyzer("analyzer-2", "Analyzer 2", "2.0.0")
        };

        _mockAnalyzerRegistry.GetRegisteredAnalyzers().Returns(expectedAnalyzers.Select(a => new AnalyzerInfo
        {
            Id = a.Id,
            Name = a.Name,
            Version = a.Version,
            Description = a.Description,
            Author = a.Author,
            SupportedExtensions = a.SupportedExtensions,
            IsEnabled = a.IsEnabled
        }).ToImmutableArray());

        // Act
        var result = _analyzerHost.GetLoadedAnalyzers();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.EqualTo(2));
        Assert.That(result.Any(ai => ai.Id == "analyzer-1"), Is.True);
        Assert.That(result.Any(ai => ai.Id == "analyzer-2"), Is.True);

        _mockAnalyzerRegistry.Received(1).GetRegisteredAnalyzers();
    }

    [Test]
    public async Task GetHealthStatusAsync_ShouldReturnHealthStatus()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

        // Act
        var result = await _analyzerHost.GetHealthStatusAsync();

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Length, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void GetAnalyzer_WithValidId_ShouldReturnAnalyzer()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

        // Act
        var result = _analyzerHost.GetAnalyzer(analyzerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Id, Is.EqualTo(analyzerId));

        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
    }

    [Test]
    public void GetAnalyzer_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var analyzerId = "non-existent-analyzer";

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns((IAnalyzer?)null);

        // Act
        var result = _analyzerHost.GetAnalyzer(analyzerId);

        // Assert
        Assert.That(result, Is.Null);

        _mockAnalyzerRegistry.Received(1).GetAnalyzer(analyzerId);
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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(Substitute.For<IAnalyzer>());
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

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(Substitute.For<IAnalyzer>());
        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(targetPath, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisResult
            {
                Success = true,
                AnalyzerId = analyzerId,
                Issues = ImmutableArray.Create(new AnalyzerIssue
                {
                    RuleId = "test-rule",
                    Title = "Consistent finding",
                    Description = "Consistent finding",
                    Severity = IssueSeverity.Info,
                    FilePath = targetPath,
                    LineNumber = 1,
                    ColumnNumber = 1
                })
            });

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
        analyzer.Version.Returns(new Version(version));
        analyzer.CanAnalyze(Arg.Any<string>()).Returns(true); // Default to true for mock analyzer
        analyzer.GetCapabilities().Returns(new AnalyzerCapabilities
        {
            SupportedLanguages = new[] { "C#" },
            SupportedFileTypes = new[] { ".cs" }
        });
        return analyzer;
    }
}
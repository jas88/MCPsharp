using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Services.Analyzers;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

[TestFixture]
[Category("Unit")]
[Category("Analyzers")]
public partial class AnalyzerHostTests : FileServiceTestBase
{
    private static readonly string[] CSharpLanguages = ["C#"];
    private static readonly string[] CSharpFileTypes = [".cs"];

    private ISecurityManager _mockSecurityManager = null!;
    private IAnalyzerRegistry _mockAnalyzerRegistry = null!;
    private IAnalyzerSandbox _mockSandbox = null!;
    private IAnalyzerSandboxFactory _mockSandboxFactory = null!;
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
        _mockSandboxFactory = Substitute.For<IAnalyzerSandboxFactory>();
        _mockFixEngine = Substitute.For<IFixEngine>();

        // Set up default security manager behavior
        _mockSecurityManager.ValidateAssemblyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityValidationResult
            {
                IsValid = true,
                IsSigned = false,
                IsTrusted = false,
                HasMaliciousPatterns = false,
                Warnings = ImmutableArray<string>.Empty
            });
        _mockSecurityManager.IsOperationAllowedAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        _mockSecurityManager.SetPermissionsAsync(Arg.Any<string>(), Arg.Any<AnalyzerPermissions>())
            .Returns(Task.CompletedTask);
        _mockSecurityManager.LogSecurityEventAsync(Arg.Any<SecurityEvent>())
            .Returns(Task.CompletedTask);

        // Set up default analyzer registry behavior
        _mockAnalyzerRegistry.ValidateCompatibilityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompatibilityResult
            {
                IsCompatible = true,
                Warnings = ImmutableArray<string>.Empty
            });
        _mockAnalyzerRegistry.RegisterAnalyzerAsync(Arg.Any<IAnalyzer>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _mockAnalyzerRegistry.UnregisterAnalyzerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _mockAnalyzerRegistry.GetLoadedAnalyzers().Returns(new List<IAnalyzer>());
        _mockAnalyzerRegistry.GetRegisteredAnalyzers().Returns(ImmutableArray<AnalyzerInfo>.Empty);

        // Set up default sandbox factory behavior
        _mockSandboxFactory.CreateSandbox().Returns(_mockSandbox);
        // Note: Returning null for LoadAnalyzerAsync to simulate analyzer not found scenario
        // Suppressing CS8620 as it's unavoidable with NSubstitute's API design
#pragma warning disable CS8620 // Argument nullability mismatch in NSubstitute
        IAnalyzer? nullAnalyzer = default;
        _mockSandbox.LoadAnalyzerAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(nullAnalyzer));
#pragma warning restore CS8620

        _mockLogger = CreateNullLogger<AnalyzerHost>();
        _mockLoggerFactory = CreateNullLoggerFactory();

        _analyzerHost = new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine, _mockSandboxFactory);
    }

    [Test]
    public void Constructor_WithNullDependencies_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(null!, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine, _mockSandboxFactory));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, null!, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine, _mockSandboxFactory));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, null!, _mockSecurityManager, _mockFixEngine, _mockSandboxFactory));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, null!, _mockFixEngine, _mockSandboxFactory));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, null!, _mockSandboxFactory));
        Assert.Throws<ArgumentNullException>(() => new AnalyzerHost(_mockLogger, _mockLoggerFactory, _mockAnalyzerRegistry, _mockSecurityManager, _mockFixEngine, null!));
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
            _mockFixEngine,
            _mockSandboxFactory));
    }

    [TearDown]
    protected override void TearDown()
    {
        _mockSandbox?.Dispose();
        _mockLoggerFactory?.Dispose();
        base.TearDown();
    }

    private static IAnalyzer CreateMockAnalyzer(string id, string name, string version)
    {
        var analyzer = Substitute.For<IAnalyzer>();
        analyzer.Id.Returns(id);
        analyzer.Name.Returns(name);
        analyzer.Description.Returns($"Description for {name}");
        analyzer.Author.Returns("Test Author");
        analyzer.Version.Returns(new Version(version));
        analyzer.IsEnabled.Returns(true);
        analyzer.Configuration.Returns(new AnalyzerConfiguration());
        analyzer.SupportedExtensions.Returns(ImmutableArray.Create(".cs"));
        analyzer.CanAnalyze(Arg.Any<string>()).Returns(true); // Default to true for mock analyzer
        analyzer.GetCapabilities().Returns(new AnalyzerCapabilities
        {
            SupportedLanguages = CSharpLanguages,
            SupportedFileTypes = CSharpFileTypes
        });
        analyzer.GetRules().Returns(ImmutableArray<AnalyzerRule>.Empty);
        analyzer.GetFixes(Arg.Any<string>()).Returns(ImmutableArray<AnalyzerFix>.Empty);
        analyzer.InitializeAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisResult
            {
                Success = true,
                AnalyzerId = id,
                Issues = ImmutableArray<AnalyzerIssue>.Empty
            });
        analyzer.DisposeAsync().Returns(Task.CompletedTask);
        return analyzer;
    }
}
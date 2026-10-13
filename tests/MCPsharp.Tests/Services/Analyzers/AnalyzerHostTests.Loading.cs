using System.Collections.Immutable;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class AnalyzerHostTests
{
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
#pragma warning disable CS4014 // NSubstitute verification - intentionally not awaited
        _mockAnalyzerRegistry.Received(1).UnregisterAnalyzerAsync(analyzerId);
#pragma warning restore CS4014
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
#pragma warning disable CS4014 // NSubstitute verification - intentionally not awaited
        _mockAnalyzerRegistry.DidNotReceive().UnregisterAnalyzerAsync(analyzerId);
#pragma warning restore CS4014
    }
}

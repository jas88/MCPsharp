using System.Collections.Immutable;
using NSubstitute;
using NUnit.Framework;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class RoslynAnalyzerServiceTests
{
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
            analyzerIds: SingleAnalyzerId);

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
            analyzerIds: NonExistentAnalyzerId);

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
}

using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Analyzers;

public partial class AnalyzerHostTests
{
    [Test]
    public async Task RunAnalyzerAsync_WithValidParameters_ShouldExecuteSuccessfully()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        var testFile = CreateTestFile("public class Test { }", ".cs", targetPath);
        var options = new AnalyzerOptions
        {
            EnableSemanticAnalysis = true,
            EnablePerformanceAnalysis = false
        };

        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
                    FilePath = testFile,
                    LineNumber = 1,
                    ColumnNumber = 1
                })
            });

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);

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
        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(false);

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("cannot analyze"));

#pragma warning disable CS4014 // NSubstitute verification - intentionally not awaited
        analyzer.DidNotReceive().AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
#pragma warning restore CS4014
    }

    [Test]
    public async Task RunAnalyzerAsync_WithAnalysisException_ShouldHandleGracefully()
    {
        // Arrange
        var analyzerId = "test-analyzer";
        var targetPath = CreateTestDirectory("target");
        _ = CreateTestFile("public class Test { }", ".cs", targetPath);
        var analyzer = CreateMockAnalyzer(analyzerId, "Test Analyzer", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);
        analyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AnalysisResult>(new InvalidOperationException("Analysis failed")));

        // Act
        var result = await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Analysis failed"));
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
        var testFile = CreateTestFile("public class Test { }", ".cs", targetPath);
        var options = new AnalyzerOptions();

        var workingAnalyzer1 = CreateMockAnalyzer("working-analyzer", "Working Analyzer", "1.0.0");
        var failingAnalyzer = CreateMockAnalyzer("failing-analyzer", "Failing Analyzer", "1.0.0");
        var workingAnalyzer2 = CreateMockAnalyzer("working-analyzer-2", "Working Analyzer 2", "1.0.0");

        _mockAnalyzerRegistry.GetAnalyzer("working-analyzer").Returns(workingAnalyzer1);
        workingAnalyzer1.CanAnalyze(targetPath).Returns(true);
        workingAnalyzer1.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AnalysisResult { Success = true, AnalyzerId = "working-analyzer" });

        _mockAnalyzerRegistry.GetAnalyzer("failing-analyzer").Returns(failingAnalyzer);
        failingAnalyzer.CanAnalyze(targetPath).Returns(true);
        failingAnalyzer.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AnalysisResult>(new InvalidOperationException("Failed")));

        _mockAnalyzerRegistry.GetAnalyzer("working-analyzer-2").Returns(workingAnalyzer2);
        workingAnalyzer2.CanAnalyze(targetPath).Returns(true);
        workingAnalyzer2.AnalyzeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
    public async Task Cancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var analyzerId = "slow-analyzer";
        var targetPath = CreateTestDirectory("target");
        var testFile = CreateTestFile("public class Test { }", ".cs", targetPath);
        var analyzer = Substitute.For<IAnalyzer>();
        using var cts = new CancellationTokenSource();

        _mockAnalyzerRegistry.GetAnalyzer(analyzerId).Returns(analyzer);
        analyzer.CanAnalyze(targetPath).Returns(true);

        // Act & Assert - Test RunAnalyzerAsync
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await _analyzerHost.RunAnalyzerAsync(analyzerId, targetPath, cancellationToken: cts.Token));
    }

    [Test]
    [Repeat(3)] // Test multiple times for consistency
    public async Task RunAnalyzerAsync_WithSameInput_ShouldProduceConsistentResults()
    {
        // Arrange
        var analyzerId = "consistent-analyzer";
        var targetPath = CreateTestDirectory("target");
        var analyzer = Substitute.For<IAnalyzer>();

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
}

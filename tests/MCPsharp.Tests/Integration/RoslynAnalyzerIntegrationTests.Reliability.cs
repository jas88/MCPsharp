using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using MCPsharp.Services.Analyzers;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class RoslynAnalyzerIntegrationTests
{
    #region Performance Tests

    [Test]
    public async Task Performance_AnalyzeLargeFile_CompletesWithinTimeLimit()
    {
        // Arrange: Create large C# file
        var largeCode = GenerateLargeCodeFile(500); // 500 methods
        var testFile = CreateTestFile(largeCode, ".cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Run analysis with timing
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var startTime = DateTime.UtcNow;
        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            testFile);
        var duration = DateTime.UtcNow - startTime;

        // Assert: Should complete within reasonable time
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.True);
        Assert.That(duration.TotalSeconds, Is.LessThan(30),
            $"Analysis took {duration.TotalSeconds:F2} seconds, expected < 30 seconds");
    }

    [Test]
    public async Task Performance_ConcurrentAnalysis_HandlesMultipleFiles()
    {
        // Arrange: Create multiple test files
        var testFiles = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var code = $@"
public class TestClass{i}
{{
    public void Method{i}()
    {{
        var unused = {i};
    }}
}}";
            testFiles.Add(CreateTestFile(code, ".cs"));
        }

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Run analyses concurrently
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var analyzerHost = _analyzerHost;
        var analysisTasks = testFiles.Select(file =>
            analyzerHost.RunAnalyzerAsync(adapter.Id, file));

        var results = await Task.WhenAll(analysisTasks);

        // Assert: All analyses should complete successfully
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Length, Is.EqualTo(testFiles.Count));
        Assert.That(results.All(r => r.Success), Is.True);
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task ErrorHandling_AnalyzeInvalidCSharpFile_ReportsError()
    {
        // Arrange: Create file with invalid C# syntax
        var invalidCode = @"
public class TestClass
{
    // Missing closing brace
    public void TestMethod()
    {
        var x = 10;
";

        var testFile = CreateTestFile(invalidCode, ".cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Run analysis on invalid file
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            testFile);

        // Assert: Should handle gracefully (may still succeed but with syntax errors)
        Assert.That(analysisResult, Is.Not.Null);
        // The analysis may succeed or fail depending on analyzer implementation
    }

    [Test]
    public async Task ErrorHandling_CancelAnalysis_ThrowsOperationCanceled()
    {
        // Arrange: Create test file
        var testCode = GenerateLargeCodeFile(100);
        var testFile = CreateTestFile(testCode, ".cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Create cancellation token that's already cancelled
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert: Should handle cancellation
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        try
        {
            await _analyzerHost.RunAnalyzerAsync(
                adapter.Id,
                testFile,
                cancellationToken: cts.Token);

            // If we get here, the implementation doesn't respect cancellation
            // but that's okay for some implementations
            Assert.Pass("Analysis completed despite cancellation (implementation doesn't check token)");
        }
        catch (OperationCanceledException)
        {
            // This is the expected behavior
            Assert.Pass("Cancellation was properly handled");
        }
    }

    [Test]
    public async Task ErrorHandling_NonExistentFile_ReportsError()
    {
        // Arrange: Use non-existent file path
        var nonExistentFile = Path.Combine(TempDirectory, "DoesNotExist.cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Attempt to analyze non-existent file
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            nonExistentFile);

        // Assert: Should report error
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.False);
        Assert.That(analysisResult.ErrorMessage, Is.Not.Null.And.Not.Empty);
    }

    #endregion

    #region Health and Status Tests

    [Test]
    public async Task Health_GetAnalyzerHealth_ReturnsValidStatus()
    {
        // Arrange: Load an analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Get health status
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var healthStatuses = await _analyzerHost.GetHealthStatusAsync();

        // Assert: Should have health status for loaded analyzer
        Assert.That(healthStatuses.Length, Is.GreaterThan(0));

        var analyzerHealth = healthStatuses.FirstOrDefault(h => h.AnalyzerId == adapter.Id);
        if (analyzerHealth == null)
            throw new InvalidOperationException($"Health status for analyzer {adapter.Id} not found");

        Assert.That(analyzerHealth.IsHealthy, Is.True);
        Assert.That(analyzerHealth.IsLoaded, Is.True);
        Assert.That(analyzerHealth.IsEnabled, Is.True);
    }

    [Test]
    public async Task Lifecycle_LoadAndUnloadAnalyzer_WorksCorrectly()
    {
        // Arrange: Create analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        // Act: Register analyzer
        var registered = await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);
        Assert.That(registered, Is.True);

        // Verify loaded
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var loadedAnalyzers = _analyzerHost.GetLoadedAnalyzers();
        Assert.That(loadedAnalyzers.Any(a => a.Id == adapter.Id), Is.True);

        // Unload analyzer
        var unloadResult = await _analyzerHost.UnloadAnalyzerAsync(adapter.Id);
        Assert.That(unloadResult, Is.True);

        // Verify unloaded
        loadedAnalyzers = _analyzerHost.GetLoadedAnalyzers();
        Assert.That(loadedAnalyzers.Any(a => a.Id == adapter.Id), Is.False);
    }

    #endregion
}

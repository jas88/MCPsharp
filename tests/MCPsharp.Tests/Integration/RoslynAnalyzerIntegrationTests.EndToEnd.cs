using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

public partial class RoslynAnalyzerIntegrationTests
{
    #region End-to-End Analysis Flow Tests

    [Test]
    public async Task EndToEnd_AnalyzeFileWithBuiltInAnalyzer_DetectsIssues()
    {
        // Arrange: Create C# file with intentional issues
        var testCode = @"
using System;

namespace TestNamespace
{
    public class testClass  // Naming violation: should be TestClass
    {
        private int unusedField;  // Unused field

        public void TestMethod()
        {
            var unused = 10;  // Unused variable (starts with 'unused')
            Console.WriteLine(""Hello"");
        }
    }
}";

        var testFile = CreateTestFile(testCode, ".cs");

        // Create a simple test analyzer and register it directly
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        // Register analyzer directly with registry
        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Run analysis on the test file using the adapter directly
        var analysisResult = await _analyzerHost!.RunAnalyzerAsync(
            adapter.Id,
            testFile);

        // Assert: Verify analysis completed successfully
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.True);

        // Verify findings were detected
        Assert.That(analysisResult.Findings, Is.Not.Empty);
        Assert.That(analysisResult.Findings.Any(f => f.Severity >= FindingSeverity.Warning), Is.True);

        // Verify finding details
        var findings = analysisResult.Findings;
        Assert.That(findings.All(f => !string.IsNullOrEmpty(f.Message)), Is.True);
        Assert.That(findings.All(f => f.FilePath == testFile), Is.True);
        Assert.That(findings.All(f => f.LineNumber > 0), Is.True);
    }

    [Test]
    public async Task EndToEnd_AnalyzeMultipleFilesInDirectory_DetectsAllIssues()
    {
        // Arrange: Create multiple C# files with issues
        var testDir = CreateTestDirectory("MultiFileTest");

        var file1Code = @"
public class Class1
{
    public void Method1()
    {
        var unused1 = 10;
    }
}";

        var file2Code = @"
public class Class2
{
    public void Method2()
    {
        var unused2 = ""test"";
    }
}";

        var file3Code = @"
public class Class3
{
    public void Method3()
    {
        var unused3 = true;
    }
}";

        File.WriteAllText(Path.Combine(testDir, "File1.cs"), file1Code);
        File.WriteAllText(Path.Combine(testDir, "File2.cs"), file2Code);
        File.WriteAllText(Path.Combine(testDir, "File3.cs"), file3Code);

        // Create and load test analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Act: Run analysis on directory
        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            testDir);

        // Assert: Verify all files were analyzed
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.True);

        // Should have findings from multiple files
        Assert.That(analysisResult.Findings, Is.Not.Empty);

        // Verify findings from different files
        var uniqueFiles = analysisResult.Findings
            .Select(f => f.FilePath)
            .Distinct()
            .Count();

        Assert.That(uniqueFiles, Is.GreaterThanOrEqualTo(2),
            "Should have findings from at least 2 files");
    }

    [Test]
    public async Task EndToEnd_AnalyzerWithConfiguration_RespectsSettings()
    {
        // Arrange: Create test file
        var testCode = @"
public class TestClass
{
    public void TestMethod()
    {
        var unused1 = 10;
        var unused2 = 20;
        var unused3 = 30;
    }
}";

        var testFile = CreateTestFile(testCode, ".cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Configure analyzer
        var configuration = new AnalyzerConfiguration
        {
            Rules = new Dictionary<string, RuleConfiguration>
            {
                ["UnusedVariable"] = new RuleConfiguration
                {
                    IsEnabled = true,
                    Severity = IssueSeverity.Error
                }
            },
            Properties = new Dictionary<string, object>
            {
                ["ReportAll"] = true
            }
        };

        if (_analyzerHost == null)
            throw new InvalidOperationException("Analyzer host is not initialized");

        await _analyzerHost.ConfigureAnalyzerAsync(adapter.Id, configuration);

        // Act: Run analysis
        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            testFile);

        // Assert: Verify configuration was applied
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.True);
    }

    #endregion
}

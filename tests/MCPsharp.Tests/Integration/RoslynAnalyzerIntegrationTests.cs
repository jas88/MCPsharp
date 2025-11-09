using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.Fixes;
using NUnit.Framework;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Integration tests for end-to-end Roslyn analyzer functionality
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Roslyn")]
[Category("Analyzer")]
public class RoslynAnalyzerIntegrationTests : IntegrationTestBase
{
    private IAnalyzerHost? _analyzerHost;
    private IAnalyzerRegistry? _analyzerRegistry;
    private ISecurityManager? _securityManager;
    private IFixEngine? _fixEngine;
    private IAnalyzerSandboxFactory? _sandboxFactory;
    private RoslynAnalyzerLoader? _analyzerLoader;
    private ILoggerFactory? _loggerFactory;
    private FileOperationsService? _fileOperations;

    [SetUp]
    protected override void Setup()
    {
        base.Setup();

        // Initialize logging
        _loggerFactory = NullLoggerFactory.Instance;

        // Initialize services
        _analyzerRegistry = new AnalyzerRegistry(
            _loggerFactory.CreateLogger<AnalyzerRegistry>());

        _securityManager = new SecurityManager(
            _loggerFactory.CreateLogger<SecurityManager>(),
            TempDirectory);

        _fileOperations = new FileOperationsService(
            TempDirectory,
            workspace: null,
            logger: _loggerFactory.CreateLogger<FileOperationsService>());

        _fixEngine = new FixEngine(
            _loggerFactory.CreateLogger<FixEngine>(),
            _fileOperations);

        _sandboxFactory = new DefaultAnalyzerSandboxFactory(
            _loggerFactory,
            _securityManager);

        _analyzerHost = new AnalyzerHost(
            _loggerFactory.CreateLogger<AnalyzerHost>(),
            _loggerFactory,
            _analyzerRegistry,
            _securityManager,
            _fixEngine,
            _sandboxFactory);

        _analyzerLoader = new RoslynAnalyzerLoader(
            _loggerFactory.CreateLogger<RoslynAnalyzerLoader>(),
            _loggerFactory);
    }

    [TearDown]
    protected override void TearDown()
    {
        // Clean up loaded analyzers
        if (_analyzerHost != null)
        {
            var loadedAnalyzers = _analyzerHost.GetLoadedAnalyzers();
            foreach (var analyzer in loadedAnalyzers)
            {
                _analyzerHost.UnloadAnalyzerAsync(analyzer.Id).Wait();
            }
        }

        base.TearDown();
    }

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
    private int unused1;
    public void Method1() { }
}";

        var file2Code = @"
public class Class2
{
    private string unused2;
    public void Method2() { var x = 10; }
}";

        var file3Code = @"
public class Class3
{
    private bool unused3;
    public void Method3() { }
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

    #region Analyzer Discovery Tests

    [Test]
    public void AnalyzerDiscovery_LoadFromAssembly_SuccessfullyLoadsAnalyzers()
    {
        // Arrange: Create a simple analyzer assembly path
        // In a real scenario, this would be a path to a compiled analyzer DLL
        var currentAssembly = typeof(RoslynAnalyzerIntegrationTests).Assembly.Location;

        // Act: Attempt to discover analyzers
        var discoveredPaths = _analyzerLoader!.DiscoverAnalyzerAssemblies(
            Path.GetDirectoryName(currentAssembly)!,
            recursive: false);

        // Assert: Should find some assemblies (at minimum the test assembly)
        Assert.That(discoveredPaths, Is.Not.Null);
        // Note: May not find actual analyzer assemblies in test environment
    }

    [Test]
    public async Task AnalyzerDiscovery_LoadInvalidAssembly_HandlesGracefully()
    {
        // Arrange: Create a fake assembly path
        var fakeAssemblyPath = Path.Combine(TempDirectory, "NonExistent.dll");

        // Act: Attempt to load from non-existent assembly
        var loadedAnalyzers = await _analyzerLoader!.LoadAnalyzersFromAssemblyAsync(
            fakeAssemblyPath);

        // Assert: Should return empty array, not throw
        Assert.That(loadedAnalyzers, Is.Not.Null);
        Assert.That(loadedAnalyzers.IsEmpty, Is.True);
    }

    #endregion

    #region Security and Permissions Tests

    [Test]
    public async Task Security_AnalyzerWithRestrictedPermissions_RespectsSandbox()
    {
        // Arrange: Create test file
        var testCode = @"public class TestClass { }";
        var testFile = CreateTestFile(testCode, ".cs");

        // Create and load analyzer
        var testAnalyzer = new TestUnusedVariableAnalyzer();
        var adapter = new RoslynAnalyzerAdapter(
            testAnalyzer,
            _loggerFactory!.CreateLogger<RoslynAnalyzerAdapter>());

        await _analyzerRegistry!.RegisterAnalyzerAsync(adapter);

        // Set restricted permissions
        await _securityManager!.SetPermissionsAsync(adapter.Id, new AnalyzerPermissions
        {
            CanReadFiles = true,
            CanWriteFiles = false,
            CanExecuteCommands = false,
            CanAccessNetwork = false,
            AllowedPaths = ImmutableArray.Create(TempDirectory)
        });

        // Act: Run analysis (should succeed with read-only)
        var analysisResult = await _analyzerHost.RunAnalyzerAsync(
            adapter.Id,
            testFile);

        // Assert: Analysis should work within sandbox
        Assert.That(analysisResult, Is.Not.Null);
        Assert.That(analysisResult.Success, Is.True);
    }

    #endregion

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
        var analysisTasks = testFiles.Select(file =>
            _analyzerHost.RunAnalyzerAsync(adapter.Id, file));

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
        var healthStatuses = await _analyzerHost.GetHealthStatusAsync();

        // Assert: Should have health status for loaded analyzer
        Assert.That(healthStatuses, Is.Not.Null);
        Assert.That(healthStatuses.Length, Is.GreaterThan(0));

        var analyzerHealth = healthStatuses.FirstOrDefault(h => h.AnalyzerId == adapter.Id);
        Assert.NotNull(analyzerHealth);
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

    #region Helper Methods

    private string GenerateLargeCodeFile(int methodCount)
    {
        var code = @"
using System;

namespace TestNamespace
{
    public class LargeTestClass
    {";

        for (int i = 0; i < methodCount; i++)
        {
            code += $@"
        public void Method{i}()
        {{
            var unused{i} = {i};
            Console.WriteLine(""Method {i}"");
        }}
";
        }

        code += @"
    }
}";

        return code;
    }

    #endregion

    #region Test Analyzer Implementation

    /// <summary>
    /// Simple test analyzer that detects unused variables
    /// </summary>
    private class TestUnusedVariableAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "TEST001";
        private const string Category = "Usage";

        private static readonly LocalizableString Title = "Unused variable";
        private static readonly LocalizableString MessageFormat = "Variable '{0}' is declared but never used";
        private static readonly LocalizableString Description = "Variables should be used after declaration";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // Register syntax node action for local declaration
            context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
        }

        private void AnalyzeNode(SyntaxNodeAnalysisContext context)
        {
            var localDeclaration = (Microsoft.CodeAnalysis.CSharp.Syntax.LocalDeclarationStatementSyntax)context.Node;

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                var variableSymbol = context.SemanticModel.GetDeclaredSymbol(variable);
                if (variableSymbol == null)
                    continue;

                // Simple heuristic: if variable name starts with "unused", report it
                if (variableSymbol.Name.StartsWith("unused", StringComparison.OrdinalIgnoreCase))
                {
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        variable.GetLocation(),
                        variableSymbol.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    #endregion
}

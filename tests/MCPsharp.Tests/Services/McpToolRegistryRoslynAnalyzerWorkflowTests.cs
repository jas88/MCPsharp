using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Analyzers.Fixes;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.Services.Analyzers;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// The analyzer tools working together on the real analyzer stack: load_roslyn_analyzers, run_roslyn_analyzers,
/// then apply_roslyn_fixes for the rule IDs the run reported.
/// </summary>
[TestFixture]
public class McpToolRegistryRoslynAnalyzerWorkflowTests
{
    private static readonly string[] StaticMethodRuleIds = [StaticMethodAnalyzer.DiagnosticId];

    private string _projectPath = null!;
    private RoslynWorkspace _workspace = null!;
    private BulkEditService _bulkEditService = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public async Task SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_roslynworkflow_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectPath);
        await File.WriteAllTextAsync(Path.Combine(_projectPath, "Calc.cs"), RoslynCodeFixApplierTests.TwoStaticCandidatesSource);

        var loggerFactory = NullLoggerFactory.Instance;
        var analyzerRegistry = new AnalyzerRegistry(NullLogger<AnalyzerRegistry>.Instance);
        var securityManager = new SecurityManager(NullLogger<SecurityManager>.Instance, _projectPath);
        var analyzerHost = new AnalyzerHost(
            NullLogger<AnalyzerHost>.Instance,
            loggerFactory,
            analyzerRegistry,
            securityManager,
            new FixEngine(NullLogger<FixEngine>.Instance, new FileOperationsService(_projectPath)),
            new DefaultAnalyzerSandboxFactory(loggerFactory, securityManager));
        var analyzerService = new RoslynAnalyzerService(
            new RoslynAnalyzerLoader(NullLogger<RoslynAnalyzerLoader>.Instance, loggerFactory),
            analyzerHost,
            NullLogger<RoslynAnalyzerService>.Instance,
            registry: analyzerRegistry);

        _workspace = new RoslynWorkspace();
        _bulkEditService = new BulkEditService();
        _registry = McpToolRegistryConsolidatedToolsTests.CreateRegistry(
            new ProjectContextManager(), _workspace, analyzerService, Path.GetTempPath(), _bulkEditService);

        var open = await ExecuteAsync("project_open", new { path = _projectPath });
        Assert.That(open.Success, Is.True, open.Error);
    }

    [TearDown]
    public void TearDown()
    {
        _workspace.Dispose();
        _bulkEditService.Dispose();
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, true);
        }
    }

    private Task<ToolCallResult> ExecuteAsync(string toolName, object arguments) =>
        _registry.ExecuteToolAsync(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private static JsonElement ToJson(ToolCallResult result) =>
        JsonSerializer.SerializeToElement(result.Result);

    [Test]
    public async Task RunRoslynAnalyzers_ReportsRuleIdsThatApplyRoslynFixesAccepts()
    {
        var load = await ExecuteAsync("load_roslyn_analyzers", new { path = typeof(StaticMethodAnalyzer).Assembly.Location });
        Assert.That(load.Success, Is.True, load.Error);

        var run = await ExecuteAsync("run_roslyn_analyzers", new { target_path = "Calc.cs" });
        Assert.That(run.Success, Is.True, run.Error);
        var ruleIds = ToJson(run).GetProperty("issues").EnumerateArray()
            .Select(issue => issue.GetProperty("ruleId").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.That(ruleIds, Is.EqualTo(StaticMethodRuleIds), "Add and Mul could be static");

        var fix = await ExecuteAsync("apply_roslyn_fixes", new { diagnostic_ids = ruleIds, target_path = "Calc.cs" });

        Assert.That(fix.Success, Is.True, fix.Error);
        var json = ToJson(fix);
        Assert.That(json.GetProperty("fixesApplied").GetInt32(), Is.EqualTo(2));
        Assert.That(json.GetProperty("changes")[0].GetProperty("newContent").GetString(),
            Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesFixed));
    }
}

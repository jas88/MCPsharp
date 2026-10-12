using System.Collections.Immutable;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.Services.Analyzers;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// apply_roslyn_fixes: fixes are written with a rollback session, previews do not write.
/// </summary>
[TestFixture]
public class McpToolRegistryRoslynFixTests
{
    private static readonly string[] StaticMethodIds = { StaticMethodAnalyzer.DiagnosticId };
    private static readonly string[] MultiDeclaratorIds = { MultiDeclaratorTestAnalyzer.DiagnosticId };
    private static readonly string[] UnknownIds = { "XYZ999" };
    private static readonly string[] AppliedThenSkipped = { "applied", "skipped" };

    private string _projectPath = null!;
    private string _calcPath = null!;
    private string _declPath = null!;
    private ProjectContextManager _projectContext = null!;
    private RoslynWorkspace _workspace = null!;
    private BulkEditService _bulkEditService = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public async Task SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_roslynfix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectPath);
        _calcPath = Path.Combine(_projectPath, "Calc.cs");
        _declPath = Path.Combine(_projectPath, "Decl.cs");
        await File.WriteAllTextAsync(_calcPath, RoslynCodeFixApplierTests.TwoStaticCandidatesSource);
        await File.WriteAllTextAsync(_declPath, RoslynCodeFixApplierTests.MultiDeclaratorSource);

        var analyzerService = Substitute.For<IRoslynAnalyzerService>();
        analyzerService.GetLoadedAnalyzers().Returns(ImmutableArray.Create<IAnalyzer>(
            Adapter(new StaticMethodAnalyzer()),
            Adapter(new MultiDeclaratorTestAnalyzer())));

        _projectContext = new ProjectContextManager();
        _workspace = new RoslynWorkspace();
        _bulkEditService = new BulkEditService();
        _registry = McpToolRegistryConsolidatedToolsTests.CreateRegistry(
            _projectContext, _workspace, analyzerService, Path.GetTempPath(), _bulkEditService);

        var open = await Execute("project_open", new { path = _projectPath });
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

    private static RoslynAnalyzerAdapter Adapter(DiagnosticAnalyzer analyzer) =>
        new(analyzer, NullLogger<RoslynAnalyzerAdapter>.Instance);

    private Task<ToolCallResult> Execute(string toolName, object arguments) =>
        _registry.ExecuteTool(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private static JsonElement ToJson(ToolCallResult result) =>
        JsonSerializer.SerializeToElement(result.Result);

    [Test]
    public async Task ApplyRoslynFixes_WritesBothFixes_AndRollbackRestoresOriginal()
    {
        var result = await Execute("apply_roslyn_fixes",
            new { diagnostic_ids = StaticMethodIds, target_path = "Calc.cs", preview = false });

        Assert.That(result.Success, Is.True, result.Error);
        var json = ToJson(result);
        Assert.That(json.GetProperty("fixesApplied").GetInt32(), Is.EqualTo(2));
        Assert.That(json.GetProperty("fixesFailed").GetInt32(), Is.Zero);
        Assert.That(await File.ReadAllTextAsync(_calcPath), Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesFixed));

        // The workspace sees the written content
        var workspaceDocument = _workspace.GetDocument(_calcPath);
        Assert.That(workspaceDocument, Is.Not.Null);
        Assert.That((await workspaceDocument!.GetTextAsync()).ToString(), Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesFixed));

        var rollbackId = json.GetProperty("rollbackId").GetString();
        Assert.That(rollbackId, Is.Not.Null.And.Not.Empty);

        var rollback = await Execute("rollback_bulk_edit", new { rollbackId });
        Assert.That(rollback.Success, Is.True, rollback.Error);
        Assert.That(await File.ReadAllTextAsync(_calcPath), Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesSource));
    }

    [Test]
    public async Task ApplyRoslynFixes_Preview_DoesNotWrite()
    {
        var result = await Execute("apply_roslyn_fixes",
            new { diagnostic_ids = StaticMethodIds, target_path = "Calc.cs" });

        Assert.That(result.Success, Is.True, result.Error);
        var json = ToJson(result);
        Assert.That(json.GetProperty("preview").GetBoolean(), Is.True);
        Assert.That(json.GetProperty("rollbackId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var change = json.GetProperty("changes")[0];
        Assert.That(change.GetProperty("newContent").GetString(), Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesFixed));
        Assert.That(change.GetProperty("diff").GetString(), Does.Contain("+    private static int Add(int a, int b)"));
        Assert.That(await File.ReadAllTextAsync(_calcPath), Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesSource));

        var rollbacks = await _bulkEditService.GetAvailableRollbacksAsync();
        Assert.That(rollbacks, Is.Empty);
    }

    [Test]
    public async Task ApplyRoslynFixes_DiagnosticResolvedByEarlierFix_IsReportedSkipped()
    {
        var result = await Execute("apply_roslyn_fixes",
            new { diagnostic_ids = MultiDeclaratorIds, target_path = "Decl.cs", preview = false });

        Assert.That(result.Success, Is.True, result.Error);
        var json = ToJson(result);
        Assert.That(json.GetProperty("fixesApplied").GetInt32(), Is.EqualTo(1));
        Assert.That(json.GetProperty("fixesSkipped").GetInt32(), Is.EqualTo(1));
        var statuses = json.GetProperty("fixes").EnumerateArray().Select(f => f.GetProperty("status").GetString()).ToArray();
        Assert.That(statuses, Is.EqualTo(AppliedThenSkipped));
        Assert.That(await File.ReadAllTextAsync(_declPath), Is.EqualTo(RoslynCodeFixApplierTests.MultiDeclaratorFixed));
    }

    [Test]
    public async Task ApplyRoslynFixes_UnknownDiagnostic_ReportsError()
    {
        var result = await Execute("apply_roslyn_fixes",
            new { diagnostic_ids = UnknownIds, target_path = "Calc.cs", preview = false });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("No loaded analyzer reports"));
        Assert.That(await File.ReadAllTextAsync(_calcPath), Is.EqualTo(RoslynCodeFixApplierTests.TwoStaticCandidatesSource));
    }
}

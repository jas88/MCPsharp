using System.Collections.Immutable;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.Services.Analyzers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// apply_roslyn_fixes fixes a file with the code fix providers of the analyzers that apply to it, as an IDE does:
/// here two projects' files are analyzed by two builds of one analyzer package, whose fixes differ
/// </summary>
[TestFixture]
public class McpToolRegistryRoslynFixScopeTests : FileServiceTestBase
{
    private const string DiagnosticId = "PROBEFIX";
    private static readonly string[] DiagnosticIds = [DiagnosticId];

    private RoslynWorkspace _workspace = null!;
    private BulkEditService _bulkEditService = null!;
    private McpToolRegistry _registry = null!;
    private string _firstFile = null!;
    private string _secondFile = null!;

    [SetUp]
    public async Task SetUpProjectAsync()
    {
        _firstFile = Path.Combine(TempDirectory, "First.cs");
        _secondFile = Path.Combine(TempDirectory, "Second.cs");
        await File.WriteAllTextAsync(_firstFile, "public class First { }\n");
        await File.WriteAllTextAsync(_secondFile, "public class Second { }\n");

        // Two versions of one package, in directories of their own as a restore puts them
        var firstBuild = await LoadBuildAsync("1.0.0.0", "first build");
        var secondBuild = await LoadBuildAsync("2.0.0.0", "second build");
        var analyzerService = Substitute.For<IRoslynAnalyzerService>();
        analyzerService.GetAnalyzersForFile(Arg.Is<string>(p => p.EndsWith("First.cs", StringComparison.Ordinal))).Returns(firstBuild);
        analyzerService.GetAnalyzersForFile(Arg.Is<string>(p => p.EndsWith("Second.cs", StringComparison.Ordinal))).Returns(secondBuild);

        _workspace = new RoslynWorkspace();
        _bulkEditService = new BulkEditService();
        _registry = McpToolRegistryConsolidatedToolsTests.CreateRegistry(
            new ProjectContextManager(), _workspace, analyzerService, Path.GetTempPath(), _bulkEditService);
        var open = await Execute("project_open", new { path = TempDirectory });
        Assert.That(open.Success, Is.True, open.Error);
    }

    [TearDown]
    public void TearDownProject()
    {
        _workspace.Dispose();
        _bulkEditService.Dispose();
    }

    private async Task<ImmutableArray<IAnalyzer>> LoadBuildAsync(string version, string marker)
    {
        var path = Path.Combine(CreateTestDirectory($"probe-{version}"), "ProbeFixes.dll");
        AnalyzerAssemblyEmitter.Emit("ProbeFixes", path, AnalyzerAssemblyEmitter.AnalyzerWithCodeFixSource(DiagnosticId, marker), version);
        var loader = new RoslynAnalyzerLoader(NullLogger<RoslynAnalyzerLoader>.Instance, NullLoggerFactory.Instance);
        var analyzers = await loader.LoadAnalyzersFromAssemblyAsync(path);
        Assert.That(analyzers, Has.Length.EqualTo(1));
        return analyzers;
    }

    private Task<ToolCallResult> Execute(string toolName, object arguments) =>
        _registry.ExecuteToolAsync(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    [Test]
    public async Task ApplyRoslynFixes_FixesEachFileWithTheProvidersOfItsOwnAnalyzers()
    {
        // Fixing the second file first loads the second build's provider, which must not fix the first file
        var second = await Execute("apply_roslyn_fixes", new { diagnostic_ids = DiagnosticIds, target_path = "Second.cs", preview = false });
        var first = await Execute("apply_roslyn_fixes", new { diagnostic_ids = DiagnosticIds, target_path = "First.cs", preview = false });

        Assert.That(second.Success, Is.True, second.Error);
        Assert.That(first.Success, Is.True, first.Error);
        Assert.That(await File.ReadAllTextAsync(_secondFile), Is.EqualTo("// second build\npublic class Second { }\n"));
        Assert.That(await File.ReadAllTextAsync(_firstFile), Is.EqualTo("// first build\npublic class First { }\n"));
    }
}

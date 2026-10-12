using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Analyzers;
using MCPsharp.Services.Consolidated;
using MCPsharp.Services.Roslyn;
using NSubstitute;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Verifies that the consolidated service tools, the Roslyn analyzer tools and extract_method
/// are listed by the registry and dispatch to their backing services.
/// </summary>
[TestFixture]
public class McpToolRegistryConsolidatedToolsTests
{
    internal static readonly string[] ConsolidatedToolNames =
    {
        "get_file_info", "get_file_content", "execute_file_operation", "execute_batch",
        "analyze_symbol", "analyze_type", "analyze_file", "analyze_project",
        "analyze_architecture", "analyze_dependencies", "analyze_quality",
        "execute_bulk_operation", "preview_bulk_operation", "get_bulk_progress", "manage_bulk_operation",
        "process_stream", "monitor_stream", "manage_stream"
    };

    internal static readonly string[] RoslynAnalyzerToolNames =
        { "load_roslyn_analyzers", "run_roslyn_analyzers", "list_roslyn_analyzers" };

    private const string CalculatorSource = @"using System;

public class Calculator
{
    public int Add(int a, int b)
    {
        var sum = a + b;
        Console.WriteLine(sum);
        return sum;
    }
}
";

    private static readonly string[] MetadataInclude = { "metadata" };
    private static readonly string[] StructureAnalysis = { "structure" };
    private static readonly string[] ComplexityMetric = { "complexity" };

    private string _projectPath = null!;
    private ProjectContextManager _projectContext = null!;
    private RoslynWorkspace _workspace = null!;
    private IRoslynAnalyzerService _analyzerService = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _projectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_consolidated_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_projectPath);
        File.WriteAllText(Path.Combine(_projectPath, "Calculator.cs"), CalculatorSource);
        File.WriteAllText(Path.Combine(_projectPath, "data.txt"), "alpha\nbeta\ngamma\n");

        _projectContext = new ProjectContextManager();
        _workspace = new RoslynWorkspace();
        _analyzerService = Substitute.For<IRoslynAnalyzerService>();
        _registry = CreateRegistry(_projectContext, _workspace, _analyzerService, Path.GetTempPath());
    }

    [TearDown]
    public void TearDown()
    {
        _workspace.Dispose();
        if (Directory.Exists(_projectPath))
        {
            Directory.Delete(_projectPath, true);
        }
    }

    /// <summary>
    /// Build a registry wired like Program.cs: consolidated services rooted at a startup directory.
    /// </summary>
    internal static McpToolRegistry CreateRegistry(
        ProjectContextManager projectContext, RoslynWorkspace workspace,
        IRoslynAnalyzerService analyzerService, string startupRoot)
    {
        var fileOperations = new FileOperationsService(startupRoot);
        var universalFileOps = new UniversalFileOperations(startupRoot, fileOperations);
        return new McpToolRegistry(
            projectContext,
            workspace,
            fileOperationsService: fileOperations,
            universalFileOps: universalFileOps,
            unifiedAnalysis: new UnifiedAnalysisService(workspace: workspace),
            bulkOperationsHub: new BulkOperationsHub(fileOps: universalFileOps),
            streamController: new StreamProcessingController(),
            roslynAnalyzerService: analyzerService);
    }

    private async Task OpenProjectAsync()
    {
        var result = await Execute("project_open", new { path = _projectPath });
        Assert.That(result.Success, Is.True, result.Error);
    }

    private Task<ToolCallResult> Execute(string toolName, object arguments) =>
        _registry.ExecuteTool(new ToolCallRequest
        {
            Name = toolName,
            Arguments = JsonDocument.Parse(JsonSerializer.Serialize(arguments))
        });

    private async Task AssertSucceeds(string toolName, object arguments)
    {
        var result = await Execute(toolName, arguments);
        Assert.That(result.Success, Is.True, $"{toolName} failed: {result.Error}");
        Assert.That(result.Result, Is.Not.Null);
    }

    #region Tool listing

    [Test]
    public void GetTools_WithServices_ListsConsolidatedAndAnalyzerTools()
    {
        var toolNames = _registry.GetTools().Select(t => t.Name).ToList();

        Assert.That(toolNames, Is.SupersetOf(ConsolidatedToolNames));
        Assert.That(toolNames, Is.SupersetOf(RoslynAnalyzerToolNames));
        Assert.That(toolNames, Does.Contain("extract_method"));
        Assert.That(toolNames, Is.Unique);
    }

    [Test]
    public void GetTools_WithServices_NewToolsHaveObjectSchemasWithDeclaredRequiredProperties()
    {
        var newTools = _registry.GetTools()
            .Where(t => ConsolidatedToolNames.Contains(t.Name) || RoslynAnalyzerToolNames.Contains(t.Name));

        foreach (var tool in newTools)
        {
            Assert.That(tool.Description, Is.Not.Empty, tool.Name);
            var schema = tool.InputSchema.RootElement;
            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"), tool.Name);
            var properties = schema.GetProperty("properties");
            if (schema.TryGetProperty("required", out var required))
            {
                foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
                {
                    Assert.That(properties.TryGetProperty(name, out _), Is.True, $"{tool.Name}: required '{name}' not declared");
                }
            }
        }
    }

    [Test]
    public void GetTools_WithoutServices_OmitsConsolidatedAndAnalyzerTools()
    {
        var toolNames = new McpToolRegistry(new ProjectContextManager()).GetTools().Select(t => t.Name).ToList();

        Assert.That(toolNames, Has.None.AnyOf(ConsolidatedToolNames.Concat(RoslynAnalyzerToolNames).ToArray()));
    }

    #endregion

    #region Preconditions

    [Test]
    public async Task ProjectScopedTools_WithoutOpenProject_ReturnNoProjectError()
    {
        foreach (var (tool, args) in new (string, object)[]
                 {
                     ("get_file_info", new { path = "data.txt" }),
                     ("analyze_project", new { }),
                     ("execute_bulk_operation", new { operation_type = "replace", files = new[] { "data.txt" } }),
                     ("process_stream", new { processor_type = "line", input_path = "data.txt" }),
                     ("extract_method", new { file_path = "Calculator.cs", start_line = 7, end_line = 8 })
                 })
        {
            var result = await Execute(tool, args);
            Assert.That(result.Success, Is.False, tool);
            Assert.That(result.Error, Is.EqualTo("No project is open"), tool);
        }
    }

    [Test]
    public async Task PathArguments_OutsideProjectRoot_AreRejected()
    {
        await OpenProjectAsync();

        var result = await Execute("get_file_content", new { path = "../outside.txt" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("outside the project root"));
    }

    [Test]
    public async Task EnumArguments_WithInvalidValue_ReturnClearError()
    {
        await OpenProjectAsync();

        var result = await Execute("execute_file_operation", new { path = "data.txt", operation = "explode" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("operation"));
    }

    #endregion

    #region Universal file operations

    [Test]
    public async Task FileTools_DispatchToUniversalFileOperations()
    {
        await OpenProjectAsync();

        await AssertSucceeds("get_file_info", new { path = "data.txt", detail = "detailed", include = MetadataInclude });
        await AssertSucceeds("get_file_content", new { path = "data.txt" });
        await AssertSucceeds("get_file_content", new { path = "data.txt", operation = "partial_read", start_line = 1, end_line = 2 });
        await AssertSucceeds("execute_file_operation", new { path = "data.txt", operation = "validate" });
        await AssertSucceeds("execute_batch", new { operations = new[] { new { path = "data.txt", operation = "validate" } } });
    }

    [Test]
    public async Task GetFileContent_Write_CreatesFileInsideProject()
    {
        await OpenProjectAsync();

        await AssertSucceeds("get_file_content", new { path = "notes/new.txt", operation = "write", content = "hello", overwrite = true });

        Assert.That(File.ReadAllText(Path.Combine(_projectPath, "notes", "new.txt")), Is.EqualTo("hello"));
    }

    #endregion

    #region Unified analysis

    [Test]
    public async Task AnalysisTools_DispatchToUnifiedAnalysisService()
    {
        await OpenProjectAsync();

        await AssertSucceeds("analyze_symbol", new { symbol_name = "Add", scope = "definition" });
        await AssertSucceeds("analyze_type", new { type_name = "Calculator" });
        await AssertSucceeds("analyze_file", new { file_path = "Calculator.cs", analysis_types = StructureAnalysis });
        await AssertSucceeds("analyze_project", new { });
        await AssertSucceeds("analyze_architecture", new { });
        await AssertSucceeds("analyze_dependencies", new { depth = 1 });
        await AssertSucceeds("analyze_quality", new { metrics = ComplexityMetric });
    }

    [Test]
    public async Task AnalyzeQuality_UsesProjectRootRatherThanScopeName()
    {
        await OpenProjectAsync();

        var result = await Execute("analyze_quality", new { scope = "all", metrics = ComplexityMetric });

        Assert.That(result.Success, Is.True, result.Error);
        var response = (MCPsharp.Models.Consolidated.QualityAnalysisResponse)result.Result!;
        Assert.That(response.Scope, Is.EqualTo("All"));
        Assert.That(response.Complexity, Is.Not.Null);
        Assert.That(response.Complexity!.Scope, Is.EqualTo(_projectPath));
        Assert.That(response.Complexity.FileCount, Is.EqualTo(1));
    }

    [Test]
    public async Task AnalyzeProject_InventoriesOnlyTheProjectDirectory()
    {
        await OpenProjectAsync();

        var result = await Execute("analyze_project", new { detail = "standard" });

        Assert.That(result.Success, Is.True, result.Error);
        var response = (MCPsharp.Models.Consolidated.ProjectAnalysisResponse)result.Result!;
        Assert.That(response.Files!.Select(f => f.Path), Is.EqualTo(new[] { Path.Combine(_projectPath, "Calculator.cs") }));
    }

    #endregion
}

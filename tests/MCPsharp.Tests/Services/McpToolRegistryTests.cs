using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpToolRegistryTests
{
    private string _testProjectPath = null!;
    private ProjectContextManager _projectContext = null!;
    private McpToolRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        // Create a temporary test directory
        _testProjectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testProjectPath);

        _projectContext = new ProjectContextManager();
        _registry = new McpToolRegistry(_projectContext);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
        if (Directory.Exists(_testProjectPath))
        {
            Directory.Delete(_testProjectPath, true);
        }
    }

    [Test]
    public void GetTools_WithoutOptionalServices_ReturnsTheFortyCoreTools()
    {
        // Act
        var tools = _registry.GetTools();

        // Assert
        Assert.That(tools, Is.Not.Null);
        Assert.That(tools.Count, Is.EqualTo(40));

        var toolNames = tools.Select(t => t.Name).ToList();

        // Phase 0 tools (7)
        Assert.That(toolNames, Does.Contain("project_open"));
        Assert.That(toolNames, Does.Contain("project_info"));
        Assert.That(toolNames, Does.Contain("file_list"));
        Assert.That(toolNames, Does.Contain("file_read"));
        Assert.That(toolNames, Does.Contain("file_write"));
        Assert.That(toolNames, Does.Contain("file_edit"));
        Assert.That(toolNames, Does.Contain("search_text"));

        // Phase 1 tools (8)
        Assert.That(toolNames, Does.Contain("find_symbol"));
        Assert.That(toolNames, Does.Contain("get_symbol_info"));
        Assert.That(toolNames, Does.Contain("get_class_structure"));
        Assert.That(toolNames, Does.Contain("add_class_property"));
        Assert.That(toolNames, Does.Contain("add_class_method"));
        Assert.That(toolNames, Does.Contain("find_references"));
        Assert.That(toolNames, Does.Contain("find_implementations"));
        Assert.That(toolNames, Does.Contain("parse_project"));

        // Phase 2 tools (7)
        Assert.That(toolNames, Does.Contain("get_workflows"));
        Assert.That(toolNames, Does.Contain("parse_workflow"));
        Assert.That(toolNames, Does.Contain("validate_workflow_consistency"));
        Assert.That(toolNames, Does.Contain("get_config_schema"));
        Assert.That(toolNames, Does.Contain("merge_configs"));
        Assert.That(toolNames, Does.Contain("analyze_impact"));
        Assert.That(toolNames, Does.Contain("trace_feature"));

        // Advanced analysis tools (10)
        Assert.That(toolNames, Does.Contain("find_callers"));
        Assert.That(toolNames, Does.Contain("find_call_chains"));
        Assert.That(toolNames, Does.Contain("find_type_usages"));
        Assert.That(toolNames, Does.Contain("analyze_call_patterns"));
        Assert.That(toolNames, Does.Contain("analyze_inheritance"));
        Assert.That(toolNames, Does.Contain("find_circular_dependencies"));
        Assert.That(toolNames, Does.Contain("find_unused_methods"));
        Assert.That(toolNames, Does.Contain("analyze_call_graph"));
        Assert.That(toolNames, Does.Contain("find_recursive_calls"));
        Assert.That(toolNames, Does.Contain("analyze_type_dependencies"));
        Assert.That(toolNames, Does.Contain("rename_symbol"));

        // Code quality tools (3)
        Assert.That(toolNames, Does.Contain("code_quality_analyze"));
        Assert.That(toolNames, Does.Contain("code_quality_fix"));
        Assert.That(toolNames, Does.Contain("code_quality_profiles"));

        // Refactoring tools (1)
        Assert.That(toolNames, Does.Contain("extract_method"));

        // Analyzer configuration tools (3), which need no optional service
        Assert.That(toolNames, Does.Contain("configure_analyzer"));
        Assert.That(toolNames, Does.Contain("get_analyzer_config"));
        Assert.That(toolNames, Does.Contain("reset_analyzer_config"));
    }

    [Test]
    public void GetTools_EachToolHasValidNameDescriptionAndSchema()
    {
        // Act
        var tools = _registry.GetTools();

        // Assert
        foreach (var tool in tools)
        {
            Assert.That(tool.Name, Is.Not.Null);
            Assert.That(tool.Name, Is.Not.Empty);
            Assert.That(tool.Description, Is.Not.Null);
            Assert.That(tool.Description, Is.Not.Empty);
            Assert.That(tool.InputSchema, Is.Not.Null);

            // Verify schema is valid JSON
            var schemaRoot = tool.InputSchema.RootElement;
            Assert.That(schemaRoot.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(schemaRoot.TryGetProperty("type", out var typeProperty), Is.True);
            Assert.That(typeProperty.GetString(), Is.EqualTo("object"));
        }
    }

    [Test]
    public async Task ExecuteTool_ProjectOpen_WithValidPath_Succeeds()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_ProjectOpen_WithInvalidPath_Fails()
    {
        // Arrange
        var invalidPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}");
        var request = new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{invalidPath.Replace("\\", "\\\\")}\"}}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("not exist").IgnoreCase);
    }

    [Test]
    public async Task ExecuteTool_ProjectInfo_WithoutOpenProject_Fails()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "project_info",
            Arguments = JsonDocument.Parse("{}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("No project").IgnoreCase);
    }

    [Test]
    public async Task ExecuteTool_ProjectInfo_WithOpenProject_Succeeds()
    {
        // Arrange
        _projectContext.OpenProject(_testProjectPath);
        var request = new ToolCallRequest
        {
            Name = "project_info",
            Arguments = JsonDocument.Parse("{}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Result, Is.Not.Null);
    }
}

using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public class McpToolRegistryTests
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
    public void GetTools_ReturnsAllThirtySevenTools()
    {
        // Act
        var tools = _registry.GetTools();

        // Assert
        Assert.That(tools, Is.Not.Null);
        Assert.That(tools.Count, Is.EqualTo(37));

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

    [Test]
    public async Task ExecuteTool_FileList_ReturnsFiles()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        // Create some test files
        var testFile1 = Path.Combine(_testProjectPath, "test1.txt");
        var testFile2 = Path.Combine(_testProjectPath, "test2.cs");
        await File.WriteAllTextAsync(testFile1, "Test content 1");
        await File.WriteAllTextAsync(testFile2, "Test content 2");

        var request = new ToolCallRequest
        {
            Name = "file_list",
            Arguments = JsonDocument.Parse("{}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileList_WithPattern_FiltersFiles()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        // Create test files
        await File.WriteAllTextAsync(Path.Combine(_testProjectPath, "test.cs"), "C# file");
        await File.WriteAllTextAsync(Path.Combine(_testProjectPath, "test.txt"), "Text file");

        var request = new ToolCallRequest
        {
            Name = "file_list",
            Arguments = JsonDocument.Parse("{\"pattern\": \"**/*.cs\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileRead_ReturnsContent()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var testFile = Path.Combine(_testProjectPath, "test.txt");
        var expectedContent = "Hello, MCP!";
        await File.WriteAllTextAsync(testFile, expectedContent);

        var request = new ToolCallRequest
        {
            Name = "file_read",
            Arguments = JsonDocument.Parse("{\"path\": \"test.txt\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileRead_NonExistentFile_Fails()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var request = new ToolCallRequest
        {
            Name = "file_read",
            Arguments = JsonDocument.Parse("{\"path\": \"nonexistent.txt\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_FileWrite_CreatesFile()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var content = "New file content";
        var request = new ToolCallRequest
        {
            Name = "file_write",
            Arguments = JsonDocument.Parse($"{{\"path\": \"newfile.txt\", \"content\": \"{content}\"}}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Error, Is.Null);

        var filePath = Path.Combine(_testProjectPath, "newfile.txt");
        Assert.That(File.Exists(filePath), Is.True);
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.That(actualContent, Is.EqualTo(content));
    }

    [Test]
    public async Task ExecuteTool_FileWrite_OverwritesExistingFile()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var filePath = Path.Combine(_testProjectPath, "existing.txt");
        await File.WriteAllTextAsync(filePath, "Original content");

        var newContent = "Updated content";
        var request = new ToolCallRequest
        {
            Name = "file_write",
            Arguments = JsonDocument.Parse($"{{\"path\": \"existing.txt\", \"content\": \"{newContent}\"}}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.That(actualContent, Is.EqualTo(newContent));
    }

    [Test]
    public async Task ExecuteTool_FileEdit_AppliesEdits()
    {
        // Arrange
        // Open project via tool
        await _registry.ExecuteTool(new ToolCallRequest
        {
            Name = "project_open",
            Arguments = JsonDocument.Parse($"{{\"path\": \"{_testProjectPath.Replace("\\", "\\\\")}\"}}")
        });

        var filePath = Path.Combine(_testProjectPath, "edit.txt");
        await File.WriteAllTextAsync(filePath, "Line 1\nLine 2\nLine 3");

        var editsJson = @"{
            ""path"": ""edit.txt"",
            ""edits"": [
                {
                    ""type"": ""replace"",
                    ""start_line"": 1,
                    ""start_column"": 0,
                    ""end_line"": 1,
                    ""end_column"": 6,
                    ""new_text"": ""Modified""
                }
            ]
        }";

        var request = new ToolCallRequest
        {
            Name = "file_edit",
            Arguments = JsonDocument.Parse(editsJson)
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteTool_UnknownTool_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "unknown_tool",
            Arguments = JsonDocument.Parse("{}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Unknown tool"));
    }

    [Test]
    public async Task ExecuteTool_InvalidArguments_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "file_read",
            Arguments = JsonDocument.Parse("{\"invalid_arg\": \"value\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    // ===== Phase 1 Tool Tests =====

    [Test]
    public async Task ExecuteTool_FindSymbol_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "find_symbol",
            Arguments = JsonDocument.Parse("{\"name\": \"TestClass\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_GetSymbolInfo_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "get_symbol_info",
            Arguments = JsonDocument.Parse("{\"filePath\": \"test.cs\", \"line\": 0, \"column\": 0}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_GetClassStructure_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "get_class_structure",
            Arguments = JsonDocument.Parse("{\"className\": \"TestClass\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_AddClassProperty_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "add_class_property",
            Arguments = JsonDocument.Parse("{\"className\": \"TestClass\", \"propertyName\": \"TestProp\", \"propertyType\": \"string\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_AddClassMethod_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "add_class_method",
            Arguments = JsonDocument.Parse("{\"className\": \"TestClass\", \"methodName\": \"TestMethod\", \"returnType\": \"void\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_FindReferences_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "find_references",
            Arguments = JsonDocument.Parse("{\"symbolName\": \"TestClass\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_FindImplementations_WithoutWorkspace_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "find_implementations",
            Arguments = JsonDocument.Parse("{\"symbolName\": \"ITestInterface\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Workspace not initialized"));
    }

    [Test]
    public async Task ExecuteTool_ParseProject_WithValidProject_Succeeds()
    {
        // Arrange
        // Create a test .csproj file
        var csprojPath = Path.Combine(_testProjectPath, "test.csproj");
        var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>";
        await File.WriteAllTextAsync(csprojPath, csprojContent);

        var request = new ToolCallRequest
        {
            Name = "parse_project",
            Arguments = JsonDocument.Parse($"{{\"projectPath\": \"{csprojPath.Replace("\\", "\\\\")}\" }}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    // ===== Phase 2 Tool Tests =====

    [Test]
    public async Task ExecuteTool_GetWorkflows_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "get_workflows",
            Arguments = JsonDocument.Parse("{\"projectRoot\": \"/test/path\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_ParseWorkflow_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "parse_workflow",
            Arguments = JsonDocument.Parse("{\"workflowPath\": \"/test/workflow.yml\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_ValidateWorkflowConsistency_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "validate_workflow_consistency",
            Arguments = JsonDocument.Parse("{\"workflowPath\": \"/test/workflow.yml\", \"projectPath\": \"/test\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_GetConfigSchema_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "get_config_schema",
            Arguments = JsonDocument.Parse("{\"configPath\": \"/test/config.json\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_MergeConfigs_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "merge_configs",
            Arguments = JsonDocument.Parse("{\"configPaths\": [\"/test/config1.json\", \"/test/config2.json\"]}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_AnalyzeImpact_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "analyze_impact",
            Arguments = JsonDocument.Parse("{\"filePath\": \"/test/file.cs\", \"changeType\": \"modify\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_TraceFeature_WithFeatureName_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "trace_feature",
            Arguments = JsonDocument.Parse("{\"featureName\": \"Authentication\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }

    [Test]
    public async Task ExecuteTool_TraceFeature_WithEntryPoint_WithoutService_ReturnsError()
    {
        // Arrange
        var request = new ToolCallRequest
        {
            Name = "trace_feature",
            Arguments = JsonDocument.Parse("{\"entryPoint\": \"UserController.Login\"}")
        };

        // Act
        var result = await _registry.ExecuteTool(request);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Phase 2 features"));
    }
}

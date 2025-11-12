using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using Xunit;

namespace MCPsharp.Tests.Services;

public class McpToolRegistryTests : IDisposable
{
    private readonly string _testProjectPath;
    private readonly ProjectContextManager _projectContext;
    private readonly McpToolRegistry _registry;

    public McpToolRegistryTests()
    {
        // Create a temporary test directory
        _testProjectPath = Path.Combine(Path.GetTempPath(), $"mcpsharp_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testProjectPath);

        _projectContext = new ProjectContextManager();
        _registry = new McpToolRegistry(_projectContext);
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testProjectPath))
        {
            Directory.Delete(_testProjectPath, true);
        }
    }

    [Fact]
    public void GetTools_ReturnsAllThirtySevenTools()
    {
        // Act
        var tools = _registry.GetTools();

        // Assert
        Assert.NotNull(tools);
        Assert.Equal(37, tools.Count);

        var toolNames = tools.Select(t => t.Name).ToList();

        // Phase 0 tools (7)
        Assert.Contains("project_open", toolNames);
        Assert.Contains("project_info", toolNames);
        Assert.Contains("file_list", toolNames);
        Assert.Contains("file_read", toolNames);
        Assert.Contains("file_write", toolNames);
        Assert.Contains("file_edit", toolNames);
        Assert.Contains("search_text", toolNames);

        // Phase 1 tools (8)
        Assert.Contains("find_symbol", toolNames);
        Assert.Contains("get_symbol_info", toolNames);
        Assert.Contains("get_class_structure", toolNames);
        Assert.Contains("add_class_property", toolNames);
        Assert.Contains("add_class_method", toolNames);
        Assert.Contains("find_references", toolNames);
        Assert.Contains("find_implementations", toolNames);
        Assert.Contains("parse_project", toolNames);

        // Phase 2 tools (7)
        Assert.Contains("get_workflows", toolNames);
        Assert.Contains("parse_workflow", toolNames);
        Assert.Contains("validate_workflow_consistency", toolNames);
        Assert.Contains("get_config_schema", toolNames);
        Assert.Contains("merge_configs", toolNames);
        Assert.Contains("analyze_impact", toolNames);
        Assert.Contains("trace_feature", toolNames);

        // Advanced analysis tools (10)
        Assert.Contains("find_callers", toolNames);
        Assert.Contains("find_call_chains", toolNames);
        Assert.Contains("find_type_usages", toolNames);
        Assert.Contains("analyze_call_patterns", toolNames);
        Assert.Contains("analyze_inheritance", toolNames);
        Assert.Contains("find_circular_dependencies", toolNames);
        Assert.Contains("find_unused_methods", toolNames);
        Assert.Contains("analyze_call_graph", toolNames);
        Assert.Contains("find_recursive_calls", toolNames);
        Assert.Contains("analyze_type_dependencies", toolNames);
        Assert.Contains("rename_symbol", toolNames);

        // Code quality tools (3)
        Assert.Contains("code_quality_analyze", toolNames);
        Assert.Contains("code_quality_fix", toolNames);
        Assert.Contains("code_quality_profiles", toolNames);

        // Refactoring tools (1)
        Assert.Contains("extract_method", toolNames);
    }

    [Fact]
    public void GetTools_EachToolHasValidNameDescriptionAndSchema()
    {
        // Act
        var tools = _registry.GetTools();

        // Assert
        foreach (var tool in tools)
        {
            Assert.NotNull(tool.Name);
            Assert.NotEmpty(tool.Name);
            Assert.NotNull(tool.Description);
            Assert.NotEmpty(tool.Description);
            Assert.NotNull(tool.InputSchema);

            // Verify schema is valid JSON
            var schemaRoot = tool.InputSchema.RootElement;
            Assert.Equal(JsonValueKind.Object, schemaRoot.ValueKind);
            Assert.True(schemaRoot.TryGetProperty("type", out var typeProperty));
            Assert.Equal("object", typeProperty.GetString());
        }
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Result);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("not exist", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("No project", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Result);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.Null(result.Error);

        var filePath = Path.Combine(_testProjectPath, "newfile.txt");
        Assert.True(File.Exists(filePath));
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.Equal(content, actualContent);
    }

    [Fact]
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
        Assert.True(result.Success);
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.Equal(newContent, actualContent);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Unknown tool", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    // ===== Phase 1 Tool Tests =====

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Workspace not initialized", result.Error);
    }

    [Fact]
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
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
    }

    // ===== Phase 2 Tool Tests =====

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }

    [Fact]
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
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Phase 2 features", result.Error);
    }
}

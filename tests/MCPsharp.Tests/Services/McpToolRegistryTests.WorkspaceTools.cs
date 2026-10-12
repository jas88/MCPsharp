using System.Text.Json;
using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpToolRegistryTests
{
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

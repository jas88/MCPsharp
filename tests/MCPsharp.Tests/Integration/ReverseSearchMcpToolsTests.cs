using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;
using Xunit;

namespace MCPsharp.Tests.Integration;

/// <summary>
/// Integration tests for reverse search MCP tools
/// </summary>
public class ReverseSearchMcpToolsTests : IDisposable
{
    private readonly ProjectContextManager _projectContext;
    private readonly RoslynWorkspace _workspace;
    private readonly McpToolRegistry _toolRegistry;

    public ReverseSearchMcpToolsTests()
    {
        _projectContext = new ProjectContextManager();
        _workspace = new RoslynWorkspace();
        _toolRegistry = new McpToolRegistry(_projectContext, _workspace);

        // Initialize project context and workspace
        InitializeProjectContext();
    }

    private void InitializeProjectContext()
    {
        // Create a temporary test directory
        var testDir = Path.Combine(Path.GetTempPath(), "MCPsharpTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(testDir);

        // Add test fixture files
        var testFiles = new[]
        {
            ("IService.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public interface IService
{
    void Execute();
    string GetData();
}"),
            ("Service.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Service : IService
{
    public void Execute()
    {
        System.Console.WriteLine(""Executing"");
    }

    public string GetData()
    {
        return ""data"";
    }
}"),
            ("Consumer.cs", @"
namespace MCPsharp.Tests.TestFixtures;

public class Consumer
{
    private readonly IService _service;

    public Consumer(IService service)
    {
        _service = service;
    }

    public void Run()
    {
        _service.Execute();
        var data = _service.GetData();
    }
}")
        };

        foreach (var (fileName, content) in testFiles)
        {
            var filePath = Path.Combine(testDir, fileName);
            File.WriteAllText(filePath, content);
        }

        // Open the project
        _projectContext.OpenProject(testDir);
    }

    [Fact]
    public async Task ToolRegistry_ShouldContainReverseSearchTools()
    {
        // Act
        var tools = _toolRegistry.GetTools();

        // Assert
        Assert.Contains(tools, t => t.Name == "find_callers");
        Assert.Contains(tools, t => t.Name == "find_call_chains");
        Assert.Contains(tools, t => t.Name == "find_type_usages");
        Assert.Contains(tools, t => t.Name == "analyze_call_patterns");
        Assert.Contains(tools, t => t.Name == "analyze_inheritance");
        Assert.Contains(tools, t => t.Name == "find_circular_dependencies");
        Assert.Contains(tools, t => t.Name == "find_unused_methods");
        Assert.Contains(tools, t => t.Name == "analyze_call_graph");
        Assert.Contains(tools, t => t.Name == "find_recursive_calls");
        Assert.Contains(tools, t => t.Name == "analyze_type_dependencies");
    }

    [Fact]
    public async Task ExecuteFindCallers_ShouldReturnCallers()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service"",
            ""includeIndirect"": true
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_callers", Arguments = arguments });

        // Debug - capture actual values
        Console.WriteLine($"Success: {result.Success}");
        Console.WriteLine($"Error: '{result.Error}'");
        Console.WriteLine($"Result: {result.Result?.GetType().Name ?? "null"}");

        // Assert
        Assert.True(result.Success, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Result);
        Assert.True(result.Error == null, $"Expected null error but got: '{result.Error}'");
    }

    [Fact]
    public async Task ExecuteFindCallChains_ShouldReturnCallChains()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service"",
            ""direction"": ""backward"",
            ""maxDepth"": 5
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_call_chains", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteFindTypeUsages_ShouldReturnTypeUsages()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""typeName"": ""Service""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_type_usages", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAnalyzeCallPatterns_ShouldReturnPatternAnalysis()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_call_patterns", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAnalyzeInheritance_ShouldReturnInheritanceAnalysis()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""typeName"": ""Service""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_inheritance", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteFindCircularDependencies_ShouldReturnDependencies()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""namespaceFilter"": ""MCPsharp.Tests.TestFixtures""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_circular_dependencies", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteFindUnusedMethods_ShouldReturnUnusedMethods()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""namespaceFilter"": ""MCPsharp.Tests.TestFixtures""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_unused_methods", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAnalyzeCallGraph_ShouldReturnCallGraph()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""typeName"": ""Service""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_call_graph", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteFindRecursiveCalls_ShouldReturnRecursiveCalls()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service"",
            ""maxDepth"": 10
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_recursive_calls", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteAnalyzeTypeDependencies_ShouldReturnDependencies()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""typeName"": ""Consumer""
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_type_dependencies", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ExecuteFindCallers_WithInvalidArguments_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_callers", Arguments = arguments });

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("MethodName is required", result.Error);
    }

    [Fact]
    public async Task ExecuteFindTypeUsages_WithInvalidArguments_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_type_usages", Arguments = arguments });

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("TypeName is required", result.Error);
    }

    [Fact]
    public async Task ExecuteAnalyzeInheritance_WithInvalidArguments_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_inheritance", Arguments = arguments });

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("TypeName is required", result.Error);
    }

    [Fact]
    public async Task ExecuteUnknownTool_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "unknown_tool", Arguments = arguments });

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("Unknown tool", result.Error);
    }

    [Fact]
    public void ReverseSearchTools_ShouldHaveCorrectInputSchemas()
    {
        // Act
        var tools = _toolRegistry.GetTools();
        var findCallersTool = tools.FirstOrDefault(t => t.Name == "find_callers");

        // Assert
        Assert.NotNull(findCallersTool);
        Assert.NotNull(findCallersTool.InputSchema);
        Assert.Equal("Find all callers of a specific method (who calls this method)", findCallersTool.Description);
    }

    [Fact]
    public void ReverseSearchTools_ShouldHaveValidSchemas()
    {
        // Act
        var tools = _toolRegistry.GetTools();
        var reverseSearchTools = tools.Where(t =>
            t.Name.StartsWith("find_") ||
            t.Name.StartsWith("analyze_") ||
            t.Name == "find_circular_dependencies" ||
            t.Name == "find_unused_methods" ||
            t.Name == "find_recursive_calls").ToList();

        // Assert
        Assert.True(reverseSearchTools.Count >= 10);
        Assert.All(reverseSearchTools, tool =>
        {
            Assert.NotNull(tool.Name);
            Assert.NotNull(tool.Description);
            Assert.NotNull(tool.InputSchema);
        });
    }

    [Fact]
    public async Task ExecuteFindCallChains_WithDirectionParameter_ShouldAcceptValidValues()
    {
        // Test backward direction
        var backwardArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""direction"": ""backward""
        }");

        var backwardResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_call_chains", Arguments = backwardArgs });
        Assert.True(backwardResult.Success);

        // Test forward direction
        var forwardArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""direction"": ""forward""
        }");

        var forwardResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_call_chains", Arguments = forwardArgs });
        Assert.True(forwardResult.Success);
    }

    [Fact]
    public async Task ExecuteFindCallChains_WithMaxDepthParameter_ShouldAcceptValidValues()
    {
        // Arrange
        var arguments = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""maxDepth"": 15
        }");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_call_chains", Arguments = arguments });

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task ExecuteMultipleTools_Sequentially_ShouldWorkCorrectly()
    {
        // Act - Execute multiple tools in sequence
        var callersArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service""
        }");

        var callersResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_callers", Arguments = callersArgs });
        Assert.True(callersResult.Success);

        var typeUsagesArgs = JsonDocument.Parse(@"
        {
            ""typeName"": ""Service""
        }");

        var typeUsagesResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_type_usages", Arguments = typeUsagesArgs });
        Assert.True(typeUsagesResult.Success);

        var patternsArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service""
        }");

        var patternsResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_call_patterns", Arguments = patternsArgs });
        Assert.True(patternsResult.Success);
    }

    public void Dispose()
    {
        var context = _projectContext.GetProjectContext();
        if (context != null && Directory.Exists(context.RootPath))
        {
            try
            {
                Directory.Delete(context.RootPath, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        // RoslynWorkspace and ProjectContext no longer implement IDisposable
    }
}
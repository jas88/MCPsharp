using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services;
using MCPsharp.Services.Roslyn;
using MCPsharp.Tests.TestFixtures;
using NUnit.Framework;

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

    [Test]
    public void ToolRegistry_ShouldContainReverseSearchTools()
    {
        // Act
        var tools = _toolRegistry.GetTools();

        // Assert
        Assert.That(t => t.Name == "find_callers", Does.Contain(tools));
        Assert.That(t => t.Name == "find_call_chains", Does.Contain(tools));
        Assert.That(t => t.Name == "find_type_usages", Does.Contain(tools));
        Assert.That(t => t.Name == "analyze_call_patterns", Does.Contain(tools));
        Assert.That(t => t.Name == "analyze_inheritance", Does.Contain(tools));
        Assert.That(t => t.Name == "find_circular_dependencies", Does.Contain(tools));
        Assert.That(t => t.Name == "find_unused_methods", Does.Contain(tools));
        Assert.That(t => t.Name == "analyze_call_graph", Does.Contain(tools));
        Assert.That(t => t.Name == "find_recursive_calls", Does.Contain(tools));
        Assert.That(t => t.Name == "analyze_type_dependencies", Does.Contain(tools));
    }

    [Test]
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
        Assert.That(result.Success, $"Expected success but got: {result.Error}", Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error == null, $"Expected null error but got: '{result.Error}'", Is.True);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ExecuteFindCallers_WithInvalidArguments_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_callers", Arguments = arguments });

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("MethodName is required"));
    }

    [Test]
    public async Task ExecuteFindTypeUsages_WithInvalidArguments_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_type_usages", Arguments = arguments });

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("TypeName is required"));
    }

    [Test]
    public async Task ExecuteAnalyzeInheritance_WithInvalidArguments_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_inheritance", Arguments = arguments });

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("TypeName is required"));
    }

    [Test]
    public async Task ExecuteUnknownTool_ShouldReturnError()
    {
        // Arrange
        var arguments = JsonDocument.Parse("{}");

        // Act
        var result = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "unknown_tool", Arguments = arguments });

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
        Assert.That(result.Error, Does.Contain("Unknown tool"));
    }

    [Test]
    public void ReverseSearchTools_ShouldHaveCorrectInputSchemas()
    {
        // Act
        var tools = _toolRegistry.GetTools();
        var findCallersTool = tools.FirstOrDefault(t => t.Name == "find_callers");

        // Assert
        Assert.That(findCallersTool, Is.Not.Null);
        Assert.That(findCallersTool.InputSchema, Is.Not.Null);
        Assert.That(findCallersTool.Description, Is.EqualTo("Find all methods that call a specific method using call graph analysis. PREFERRED over grep/search for understanding code flow and dependencies. Returns structured caller information with file and line locations. Use to analyze method dependencies before refactoring or to trace execution paths."));
    }

    [Test]
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
        Assert.That(reverseSearchTools.Count >= 10, Is.True);
        Assert.All(reverseSearchTools, tool =>
        {
            Assert.That(tool.Name, Is.Not.Null);
            Assert.That(tool.Description, Is.Not.Null);
            Assert.That(tool.InputSchema, Is.Not.Null);
        });
    }

    [Test]
    public async Task ExecuteFindCallChains_WithDirectionParameter_ShouldAcceptValidValues()
    {
        // Test backward direction
        var backwardArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""direction"": ""backward""
        }");

        var backwardResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_call_chains", Arguments = backwardArgs });
        Assert.That(backwardResult.Success, Is.True);

        // Test forward direction
        var forwardArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""direction"": ""forward""
        }");

        var forwardResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_call_chains", Arguments = forwardArgs });
        Assert.That(forwardResult.Success, Is.True);
    }

    [Test]
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
        Assert.That(result.Success, Is.True);
        Assert.That(result.Result, Is.Not.Null);
    }

    [Test]
    public async Task ExecuteMultipleTools_Sequentially_ShouldWorkCorrectly()
    {
        // Act - Execute multiple tools in sequence
        var callersArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service""
        }");

        var callersResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_callers", Arguments = callersArgs });
        Assert.That(callersResult.Success, Is.True);

        var typeUsagesArgs = JsonDocument.Parse(@"
        {
            ""typeName"": ""Service""
        }");

        var typeUsagesResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "find_type_usages", Arguments = typeUsagesArgs });
        Assert.That(typeUsagesResult.Success, Is.True);

        var patternsArgs = JsonDocument.Parse(@"
        {
            ""methodName"": ""Execute"",
            ""containingType"": ""Service""
        }");

        var patternsResult = await _toolRegistry.ExecuteTool(new ToolCallRequest { Name = "analyze_call_patterns", Arguments = patternsArgs });
        Assert.That(patternsResult.Success, Is.True);
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
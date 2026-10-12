using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Reverse search (caller, call chain and type usage) tool definitions
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Reverse search tool definitions
    /// </summary>
    private static List<McpTool> ReverseSearchToolDefinitions() => new()
    {
        // Reverse search tools
        new McpTool
        {
            Name = "find_callers",
            Description = "Find all methods that call a specific method using call graph analysis. PREFERRED over grep/search for understanding code flow and dependencies. Returns structured caller information with file and line locations. Use to analyze method dependencies before refactoring or to trace execution paths.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "methodName",
                    Type = "string",
                    Description = "Name of the method to analyze",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "containingType",
                    Type = "string",
                    Description = "Optional containing type to disambiguate overloaded methods",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "includeIndirect",
                    Type = "boolean",
                    Description = "Include indirect calls through interfaces/base classes",
                    Required = false,
                    Default = true
                }
            )
        },
        new McpTool
        {
            Name = "find_call_chains",
            Description = "Find call chains leading to or from a specific method",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "methodName",
                    Type = "string",
                    Description = "Name of the method to analyze",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "containingType",
                    Type = "string",
                    Description = "Optional containing type to disambiguate methods",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "direction",
                    Type = "string",
                    Description = "Direction of analysis (backward=who calls this, forward=what this calls)",
                    Required = false,
                    Default = "backward"
                },
                new PropertyDefinition
                {
                    Name = "maxDepth",
                    Type = "integer",
                    Description = "Maximum depth of call chain analysis",
                    Required = false,
                    Default = 10
                }
            )
        },
        new McpTool
        {
            Name = "find_type_usages",
            Description = "Find all usages of a specific type across the codebase",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "typeName",
                    Type = "string",
                    Description = "Name of the type to analyze",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "analyze_call_patterns",
            Description = "Analyze call patterns for a specific method",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "methodName",
                    Type = "string",
                    Description = "Name of the method to analyze",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "containingType",
                    Type = "string",
                    Description = "Optional containing type to disambiguate methods",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "analyze_inheritance",
            Description = "Analyze inheritance relationships for a specific type",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "typeName",
                    Type = "string",
                    Description = "Name of the type to analyze",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "find_circular_dependencies",
            Description = "Find circular dependencies in call chains",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "namespaceFilter",
                    Type = "string",
                    Description = "Optional namespace filter to limit analysis scope",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "find_unused_methods",
            Description = "Find methods that are never called (potential dead code)",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "namespaceFilter",
                    Type = "string",
                    Description = "Optional namespace filter to limit analysis scope",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "analyze_call_graph",
            Description = "Analyze the call graph for a specific type or namespace",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "typeName",
                    Type = "string",
                    Description = "Optional type name to limit analysis to a specific type",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "namespaceName",
                    Type = "string",
                    Description = "Optional namespace name to limit analysis scope",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "find_recursive_calls",
            Description = "Find recursive call chains for a specific method",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "methodName",
                    Type = "string",
                    Description = "Name of the method to analyze for recursion",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "containingType",
                    Type = "string",
                    Description = "Optional containing type to disambiguate methods",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "maxDepth",
                    Type = "integer",
                    Description = "Maximum depth to search for recursive calls",
                    Required = false,
                    Default = 20
                }
            )
        },
        new McpTool
        {
            Name = "analyze_type_dependencies",
            Description = "Analyze type dependencies for a specific type",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "typeName",
                    Type = "string",
                    Description = "Name of the type to analyze dependencies for",
                    Required = true
                }
            )
        },
    };
}

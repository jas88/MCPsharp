using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Statically registered tool definitions: the registration order and the project and file tools
/// </summary>
public partial class McpToolRegistry
{
    private static List<McpTool> RegisterTools()
    {
        var tools = new List<McpTool>();
        tools.AddRange(ProjectAndFileToolDefinitions());
        tools.AddRange(SymbolToolDefinitions());
        tools.AddRange(Phase2ToolDefinitions());
        tools.AddRange(RenameToolDefinitions());
        tools.AddRange(ReverseSearchToolDefinitions());
        tools.AddRange(CodeQualityToolDefinitions());
        tools.AddRange(RefactoringToolDefinitions());
        return tools;
    }

    /// <summary>
    /// Project and file tool definitions
    /// </summary>
    private static List<McpTool> ProjectAndFileToolDefinitions() => new()
    {
        new McpTool
        {
            Name = "project_open",
            Description = "Open a project directory for file operations",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "path",
                    Type = "string",
                    Description = "Absolute path to the project directory",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "project_info",
            Description = "Get information about the currently open project",
            InputSchema = JsonSchemaHelper.CreateSchema()
        },
        new McpTool
        {
            Name = "file_list",
            Description = "List files in the project, optionally filtered by glob pattern",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "pattern",
                    Type = "string",
                    Description = "Optional glob pattern (e.g., '**/*.cs', 'src/**/*.json')",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "include_hidden",
                    Type = "boolean",
                    Description = "Whether to include hidden files",
                    Required = false,
                    Default = false
                }
            )
        },
        new McpTool
        {
            Name = "file_read",
            Description = "Read the contents of a file. Consider using semantic tools (find_symbol, get_class_structure, find_references) first to understand code structure and relationships, then use file_read for full context when needed. Semantic tools provide structured data that's easier to process than raw file content.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "path",
                    Type = "string",
                    Description = "Path to the file (relative to project root or absolute)",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "file_write",
            Description = "Write content to a file (creates if doesn't exist, overwrites if exists)",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "path",
                    Type = "string",
                    Description = "Path to the file (relative to project root or absolute)",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "content",
                    Type = "string",
                    Description = "Content to write to the file",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "create_directories",
                    Type = "boolean",
                    Description = "Whether to create parent directories if they don't exist",
                    Required = false,
                    Default = true
                }
            )
        },
        new McpTool
        {
            Name = "file_edit",
            Description = @"Apply text edits to a file using line/column positions.

**DEPRECATED: For C# code modifications, use AI-powered tools instead:**
- Use `ai_suggest_fix` for bug fixes
- Use `ai_refactor` for refactoring
- Use `ai_implement_feature` for new features

These tools use Roslyn AST and guarantee syntactically valid code.

Edit types:
- replace: Requires start_line, start_column, end_line, end_column, new_text
- insert: Requires line, column, text
- delete: Requires start_line, start_column, end_line, end_column",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "path",
                    Type = "string",
                    Description = "Path to the file (relative to project root or absolute)",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "edits",
                    Type = "array",
                    Description = "Array of edit operations with line/column positions",
                    Required = true,
                    Items = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["type"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["enum"] = EditOperationTypes,
                                ["description"] = "Type of edit operation"
                            },
                            ["start_line"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "Starting line number (0-indexed, required for replace/delete)"
                            },
                            ["start_column"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "Starting column number (0-indexed, required for replace/delete)"
                            },
                            ["end_line"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "Ending line number (0-indexed, required for replace/delete)"
                            },
                            ["end_column"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "Ending column number (0-indexed, required for replace/delete)"
                            },
                            ["new_text"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "New text for replace operations"
                            },
                            ["line"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "Line number for insert operations (0-indexed)"
                            },
                            ["column"] = new Dictionary<string, object>
                            {
                                ["type"] = "integer",
                                ["description"] = "Column number for insert operations (0-indexed)"
                            },
                            ["text"] = new Dictionary<string, object>
                            {
                                ["type"] = "string",
                                ["description"] = "Text to insert"
                            }
                        },
                        ["required"] = EditRequiredProperties
                    }
                }
            )
        },
        new McpTool
        {
            Name = "search_text",
            Description = "Search for text or regex patterns across project files. Use find_symbol for code elements (classes, methods, etc.) when possible as it provides semantic understanding. Text search is useful for comments, strings, documentation, and non-code content where semantic analysis isn't applicable.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "pattern",
                    Type = "string",
                    Description = "Text or regex pattern to search for",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "target_path",
                    Type = "string",
                    Description = "File or directory to search (default: current project)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "regex",
                    Type = "boolean",
                    Description = "Treat pattern as regex (default: false)",
                    Required = false,
                    Default = false
                },
                new PropertyDefinition
                {
                    Name = "case_sensitive",
                    Type = "boolean",
                    Description = "Case-sensitive search (default: true)",
                    Required = false,
                    Default = true
                },
                new PropertyDefinition
                {
                    Name = "include_pattern",
                    Type = "string",
                    Description = "Glob pattern for files to include (e.g., \"*.cs\")",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "exclude_pattern",
                    Type = "string",
                    Description = "Glob pattern for files to exclude",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "context_lines",
                    Type = "integer",
                    Description = "Lines of context before/after match (default: 2)",
                    Required = false,
                    Default = 2
                },
                new PropertyDefinition
                {
                    Name = "max_results",
                    Type = "integer",
                    Description = "Maximum results to return (default: 100)",
                    Required = false,
                    Default = 100
                },
                new PropertyDefinition
                {
                    Name = "offset",
                    Type = "integer",
                    Description = "Pagination offset (default: 0)",
                    Required = false,
                    Default = 0
                }
            )
        },
    };
}

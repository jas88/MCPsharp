using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Code quality and refactoring tool definitions
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Automated code quality tool definitions
    /// </summary>
    private static List<McpTool> CodeQualityToolDefinitions() => new()
    {
        // Automated Code Fix Tools
        new McpTool
        {
            Name = "code_quality_analyze",
            Description = "Analyze code for quality issues using built-in diagnostic analyzers",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "target_path",
                    Type = "string",
                    Description = "File or directory to analyze",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "diagnostic_ids",
                    Type = "array",
                    Description = "Specific diagnostic IDs to check (e.g., [\"MCP001\", \"MCP002\"]). Optional - if not specified, all diagnostics for the profile are checked.",
                    Required = false,
                    Items = StringItems
                },
                new PropertyDefinition
                {
                    Name = "profile",
                    Type = "string",
                    Description = "Fix profile to use: Conservative, Balanced, or Aggressive (default: Balanced)",
                    Required = false,
                    Default = "Balanced"
                }
            )
        },
        new McpTool
        {
            Name = "code_quality_fix",
            Description = "Apply automated code quality fixes to improve code",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "target_path",
                    Type = "string",
                    Description = "File or directory to fix",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "diagnostic_ids",
                    Type = "array",
                    Description = "Specific diagnostic IDs to fix. Optional - if not specified, all safe fixes for the profile are applied.",
                    Required = false,
                    Items = StringItems
                },
                new PropertyDefinition
                {
                    Name = "profile",
                    Type = "string",
                    Description = "Fix profile to use: Conservative, Balanced, or Aggressive (default: Balanced)",
                    Required = false,
                    Default = "Balanced"
                },
                new PropertyDefinition
                {
                    Name = "preview",
                    Type = "boolean",
                    Description = "Show preview without applying changes (default: true for safety)",
                    Required = false,
                    Default = true
                },
                new PropertyDefinition
                {
                    Name = "create_backup",
                    Type = "boolean",
                    Description = "Create backup before fixing (default: true)",
                    Required = false,
                    Default = true
                }
            )
        },
        new McpTool
        {
            Name = "code_quality_profiles",
            Description = "List available fix profiles and their capabilities",
            InputSchema = JsonSchemaHelper.CreateSchema()
        },
    };

    /// <summary>
    /// Refactoring tool definitions
    /// </summary>
    private static List<McpTool> RefactoringToolDefinitions() => new()
    {
        // Refactoring Tools
        new McpTool
        {
            Name = "extract_method",
            Description = "Extract the selected statements (or, with start/end columns, a single expression) into a new method of the containing type, inferring parameters (by value or ref), returned variables (value or tuple), async, static and type parameters. Requires an open project. Unless preview is true the file is rewritten on disk and in the workspace after recording a rollback session (undo with rollback_bulk_edit using the returned rollbackId); the change is refused, without writing, if it would introduce compilation errors.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "file_path",
                    Type = "string",
                    Description = "Path to the file containing code to extract",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "start_line",
                    Type = "integer",
                    Description = "Starting line number (1-based)",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "end_line",
                    Type = "integer",
                    Description = "Ending line number (1-based)",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "start_column",
                    Type = "integer",
                    Description = "Starting column on start_line (1-based, optional; default: start of line)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "end_column",
                    Type = "integer",
                    Description = "Ending column on end_line (1-based, inclusive, optional; default: end of line)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "method_name",
                    Type = "string",
                    Description = "Name for the extracted method (optional, auto-generated if not provided)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "accessibility",
                    Type = "string",
                    Description = "Method accessibility: private, public, protected, internal (default: private)",
                    Required = false,
                    Default = "private"
                },
                new PropertyDefinition
                {
                    Name = "make_static",
                    Type = "boolean",
                    Description = "Force the extracted method to be static (true) or an instance method (false); default: static when the selection uses no instance members",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "preview",
                    Type = "boolean",
                    Description = "Only compute the extraction and return a before/after preview without modifying the file (default: false, which rewrites the file)",
                    Required = false,
                    Default = false
                }
            )
        }
    };
}

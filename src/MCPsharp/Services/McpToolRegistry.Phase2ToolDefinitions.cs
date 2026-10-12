using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Workflow, configuration, impact and feature tracing tool definitions
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Workflow, configuration, impact analysis and feature tracing tool definitions
    /// </summary>
    private static List<McpTool> Phase2ToolDefinitions() => new()
    {
        // ===== Phase 2 Tools =====
        new McpTool
        {
            Name = "get_workflows",
            Description = "Get all GitHub Actions workflows in a project",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "projectRoot",
                    Type = "string",
                    Description = "Root directory of the project",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "parse_workflow",
            Description = "Parse a GitHub Actions workflow file",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "workflowPath",
                    Type = "string",
                    Description = "Path to the workflow YAML file",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "validate_workflow_consistency",
            Description = "Validate GitHub Actions workflow against project configuration",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "workflowPath",
                    Type = "string",
                    Description = "Path to the workflow YAML file",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "projectPath",
                    Type = "string",
                    Description = "Path to the project directory",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "get_config_schema",
            Description = "Get the schema of a configuration file (JSON/YAML)",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "configPath",
                    Type = "string",
                    Description = "Path to the configuration file",
                    Required = true
                }
            )
        },
        new McpTool
        {
            Name = "merge_configs",
            Description = "Merge multiple configuration files",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "configPaths",
                    Type = "array",
                    Description = "Array of configuration file paths to merge",
                    Required = true,
                    Items = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    }
                }
            )
        },
        new McpTool
        {
            Name = "analyze_impact",
            Description = "Analyze the impact of a code change across the project",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "filePath",
                    Type = "string",
                    Description = "Path to the file being changed",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "changeType",
                    Type = "string",
                    Description = "Type of change (add, modify, delete, rename)",
                    Required = true
                },
                new PropertyDefinition
                {
                    Name = "symbolName",
                    Type = "string",
                    Description = "Optional symbol name being changed",
                    Required = false
                }
            )
        },
        new McpTool
        {
            Name = "trace_feature",
            Description = "Trace a feature across multiple files in the project",
            InputSchema = JsonSchemaHelper.CreateSchema(
                new PropertyDefinition
                {
                    Name = "featureName",
                    Type = "string",
                    Description = "Name of the feature to trace (alternative to entryPoint)",
                    Required = false
                },
                new PropertyDefinition
                {
                    Name = "entryPoint",
                    Type = "string",
                    Description = "Entry point to start feature discovery (alternative to featureName)",
                    Required = false
                }
            )
        },
    };
}

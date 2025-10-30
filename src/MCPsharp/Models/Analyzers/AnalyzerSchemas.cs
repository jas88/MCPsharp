using System.Text.Json.Serialization;

namespace MCPsharp.Models.Analyzers;

/// <summary>
/// JSON schemas for analyzer MCP tools
/// </summary>
public static class AnalyzerSchemas
{
    /// <summary>
    /// Schema for listing analyzers
    /// </summary>
    public static readonly object ListAnalyzersSchema = new
    {
        type = "object",
        properties = new
        {
            include_builtin = new
            {
                type = "boolean",
                description = "Include built-in analyzers in the results",
  @default = true
            },
            include_external = new
            {
                type = "boolean",
                description = "Include external analyzers in the results",
  @default = true
            },
            category = new
            {
                type = "string",
                description = "Filter by rule category",
                @enum = new[] { "CodeQuality", "Performance", "Security", "Maintainability", "Reliability", "Style", "Migration", "Design" }
            },
            extension = new
            {
                type = "string",
                description = "Filter by supported file extension (e.g., '.cs')"
            }
        }
    };

    /// <summary>
    /// Schema for running analysis
    /// </summary>
    public static readonly object RunAnalysisSchema = new
    {
        type = "object",
        required = new[] { "analyzer_id", "files" },
        properties = new
        {
            analyzer_id = new
            {
                type = "string",
                description = "ID of the analyzer to run"
            },
            files = new
            {
                type = "array",
                items = new
                {
                    type = "string"
                },
                description = "List of file paths to analyze"
            },
            rules = new
            {
                type = "array",
                items = new
                {
                    type = "string"
                },
                description = "Specific rule IDs to run (empty for all rules)"
            },
            configuration = new
            {
                type = "object",
                description = "Analyzer configuration overrides",
                properties = new
                {
                    is_enabled = new { type = "boolean", @default = true },
                    include_files = new
                    {
                        type = "array",
                        items = new { type = "string" }
                    },
                    exclude_files = new
                    {
                        type = "array",
                        items = new { type = "string" }
                    },
                    rules = new
                    {
                        type = "object",
                        additionalProperties = new
                        {
                            type = "object",
                            properties = new
                            {
                                is_enabled = new { type = "boolean" },
                                severity = new
                                {
                                    type = "string",
                                    @enum = new[] { "Info", "Warning", "Error", "Critical" }
                                }
                            }
                        }
                    }
                }
            },
            include_disabled_rules = new
            {
                type = "boolean",
                description = "Include disabled rules in analysis",
                @default = false
            },
            generate_fixes = new
            {
                type = "boolean",
                description = "Generate fixes for found issues",
  @default = true
            }
        }
    };

    /// <summary>
    /// Schema for getting fixes
    /// </summary>
    public static readonly object GetFixesSchema = new
    {
        type = "object",
        required = new[] { "analyzer_id", "issue_ids" },
        properties = new
        {
            analyzer_id = new
            {
                type = "string",
                description = "ID of the analyzer"
            },
            issue_ids = new
            {
                type = "array",
                items = new
                {
                    type = "string"
                },
                description = "List of issue IDs to get fixes for"
            }
        }
    };

    /// <summary>
    /// Schema for applying fixes
    /// </summary>
    public static readonly object ApplyFixesSchema = new
    {
        type = "object",
        required = new[] { "analyzer_id", "issue_ids" },
        properties = new
        {
            analyzer_id = new
            {
                type = "string",
                description = "ID of the analyzer"
            },
            issue_ids = new
            {
                type = "array",
                items = new
                {
                    type = "string"
                },
                description = "List of issue IDs to apply fixes for"
            },
            fix_ids = new
            {
                type = "array",
                items = new
                {
                    type = "string"
                },
                description = "Specific fix IDs to apply (empty for all available fixes)"
            },
            preview_only = new
            {
                type = "boolean",
                description = "Only preview fixes without applying them",
                @default = false
            },
            resolve_conflicts = new
            {
                type = "boolean",
                description = "Automatically resolve conflicts between fixes",
  @default = true
            },
            conflict_strategy = new
            {
                type = "string",
                description = "Strategy for resolving conflicts",
                @enum = new[] { "PreferOlder", "PreferNewer", "PreferConfidence", "PreferSeverity", "Manual", "SkipAll", "Abort" },
                @default = "PreferNewer"
            },
            create_backup = new
            {
                type = "boolean",
                description = "Create backup files before applying fixes",
  @default = true
            },
            inputs = new
            {
                type = "object",
                description = "Additional inputs required for interactive fixes",
                additionalProperties = true
            }
        }
    };

    /// <summary>
    /// Schema for loading analyzers
    /// </summary>
    public static readonly object LoadAnalyzerSchema = new
    {
        type = "object",
        required = new[] { "assembly_path" },
        properties = new
        {
            assembly_path = new
            {
                type = "string",
                description = "Path to the analyzer assembly (.dll file)"
            },
            auto_enable = new
            {
                type = "boolean",
                description = "Automatically enable the analyzer after loading",
  @default = true
            },
            permissions = new
            {
                type = "object",
                description = "Permissions to grant to the analyzer",
                properties = new
                {
                    can_read_files = new { type = "boolean", @default = true },
                    can_write_files = new { type = "boolean", @default = false },
                    can_execute_commands = new { type = "boolean", @default = false },
                    can_access_network = new { type = "boolean", @default = false },
                    allowed_paths = new
                    {
                        type = "array",
                        items = new { type = "string" }
                    },
                    denied_paths = new
                    {
                        type = "array",
                        items = new { type = "string" }
                    }
                }
            }
        }
    };

    /// <summary>
    /// Schema for unloading analyzers
    /// </summary>
    public static readonly object UnloadAnalyzerSchema = new
    {
        type = "object",
        required = new[] { "analyzer_id" },
        properties = new
        {
            analyzer_id = new
            {
                type = "string",
                description = "ID of the analyzer to unload"
            },
            force = new
            {
                type = "boolean",
                description = "Force unload even if analyzer is in use",
                @default = false
            }
        }
    };

    /// <summary>
    /// Schema for configuring analyzers
    /// </summary>
    public static readonly object ConfigureAnalyzerSchema = new
    {
        type = "object",
        required = new[] { "analyzer_id" },
        properties = new
        {
            analyzer_id = new
            {
                type = "string",
                description = "ID of the analyzer to configure"
            },
            is_enabled = new
            {
                type = "boolean",
                description = "Enable or disable the analyzer"
            },
            configuration = new
            {
                type = "object",
                description = "Analyzer configuration",
                properties = new
                {
                    properties = new
                    {
                        type = "object",
                        description = "Custom properties for the analyzer",
                        additionalProperties = true
                    },
                    rules = new
                    {
                        type = "object",
                        description = "Rule-specific configuration",
                        additionalProperties = new
                        {
                            type = "object",
                            properties = new
                            {
                                is_enabled = new { type = "boolean" },
                                severity = new
                                {
                                    type = "string",
                                    @enum = new[] { "Info", "Warning", "Error", "Critical" }
                                },
                                parameters = new
                                {
                                    type = "object",
                                    description = "Rule-specific parameters",
                                    additionalProperties = true
                                }
                            }
                        }
                    },
                    include_files = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "File patterns to include"
                    },
                    exclude_files = new
                    {
                        type = "array",
                        items = new { type = "string" },
                        description = "File patterns to exclude"
                    }
                }
            }
        }
    };

    /// <summary>
    /// Schema for getting analyzer health
    /// </summary>
    public static readonly object GetAnalyzerHealthSchema = new
    {
        type = "object",
        properties = new
        {
            analyzer_id = new
            {
                type = "string",
                description = "ID of specific analyzer to check (empty for all analyzers)"
            }
        }
    };

    /// <summary>
    /// Schema for getting fix history
    /// </summary>
    public static readonly object GetFixHistorySchema = new
    {
        type = "object",
        properties = new
        {
            max_sessions = new
            {
                type = "integer",
                description = "Maximum number of sessions to return",
                @default = 50,
                minimum = 1,
                maximum = 1000
            },
            analyzer_id = new
            {
                type = "string",
                description = "Filter by specific analyzer ID"
            },
            start_date = new
            {
                type = "string",
                format = "date-time",
                description = "Start date for filtering sessions"
            },
            end_date = new
            {
                type = "string",
                format = "date-time",
                description = "End date for filtering sessions"
            }
        }
    };

    /// <summary>
    /// Schema for rolling back fixes
    /// </summary>
    public static readonly object RollbackFixesSchema = new
    {
        type = "object",
        required = new[] { "session_id" },
        properties = new
        {
            session_id = new
            {
                type = "string",
                description = "ID of the fix session to rollback"
            },
            force = new
            {
                type = "boolean",
                description = "Force rollback even if files have been modified",
                @default = false
            }
        }
    };
}
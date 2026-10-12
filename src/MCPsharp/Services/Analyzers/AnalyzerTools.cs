using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// MCP tool implementations for analyzer functionality
/// </summary>
public static partial class AnalyzerTools
{
    /// <summary>
    /// Get all analyzer MCP tools
    /// </summary>
    public static List<McpTool> GetAnalyzerTools()
    {
        return new List<McpTool>
        {
            new McpTool
            {
                Name = "analyzer_list",
                Description = "List all available analyzers, with optional filtering",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.ListAnalyzersSchema)
            },
            new McpTool
            {
                Name = "analyzer_run",
                Description = "Run analysis on specified files using an analyzer",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.RunAnalysisSchema)
            },
            new McpTool
            {
                Name = "analyzer_get_fixes",
                Description = "Get available fixes for specific analyzer issues",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.GetFixesSchema)
            },
            new McpTool
            {
                Name = "analyzer_apply_fixes",
                Description = "Apply fixes for analyzer issues with conflict resolution and rollback",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.ApplyFixesSchema)
            },
            new McpTool
            {
                Name = "analyzer_load",
                Description = "Load an analyzer from an assembly file with security validation",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.LoadAnalyzerSchema)
            },
            new McpTool
            {
                Name = "analyzer_unload",
                Description = "Unload an analyzer and clean up resources",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.UnloadAnalyzerSchema)
            },
            new McpTool
            {
                Name = "analyzer_configure",
                Description = "Configure analyzer settings and rule preferences",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.ConfigureAnalyzerSchema)
            },
            new McpTool
            {
                Name = "analyzer_get_health",
                Description = "Get health status and metrics for analyzers",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.GetAnalyzerHealthSchema)
            },
            new McpTool
            {
                Name = "analyzer_get_fix_history",
                Description = "Get history of applied fixes with rollback information",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.GetFixHistorySchema)
            },
            new McpTool
            {
                Name = "analyzer_rollback_fixes",
                Description = "Rollback previously applied fixes using backups",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.RollbackFixesSchema)
            }
        };
    }

    /// <summary>
    /// Execute analyzer tool
    /// </summary>
    public static async Task<ToolCallResult> ExecuteAnalyzerTool(
        string toolName,
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        IFixEngine fixEngine,
        CancellationToken cancellationToken = default)
    {
        return toolName switch
        {
            "analyzer_list" => await ExecuteListAnalyzers(arguments, analyzerHost),
            "analyzer_run" => await ExecuteRunAnalysis(arguments, analyzerHost, cancellationToken),
            "analyzer_get_fixes" => await ExecuteGetFixes(arguments, analyzerHost, cancellationToken),
            "analyzer_apply_fixes" => await ExecuteApplyFixes(arguments, analyzerHost, cancellationToken),
            "analyzer_load" => await ExecuteLoadAnalyzer(arguments, analyzerHost, cancellationToken),
            "analyzer_unload" => await ExecuteUnloadAnalyzer(arguments, analyzerHost, cancellationToken),
            "analyzer_configure" => await ExecuteConfigureAnalyzer(arguments, analyzerHost, cancellationToken),
            "analyzer_get_health" => await ExecuteGetAnalyzerHealth(arguments, analyzerHost, cancellationToken),
            "analyzer_get_fix_history" => await ExecuteGetFixHistory(arguments, fixEngine, cancellationToken),
            "analyzer_rollback_fixes" => await ExecuteRollbackFixes(arguments, fixEngine, cancellationToken),
            _ => new ToolCallResult
            {
                Success = false,
                Error = $"Unknown analyzer tool: {toolName}"
            }
        };
    }

    /// <summary>
    /// Builds a failed tool result from an exception raised while executing an analyzer tool.
    /// </summary>
    private static ToolCallResult CreateErrorResult(Exception exception)
    {
        return new ToolCallResult
        {
            Success = false,
            Error = exception.Message
        };
    }
}

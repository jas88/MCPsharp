using System.Text.Json;

namespace MCPsharp.Models;

/// <summary>
/// Represents an MCP tool that can be invoked via JSON-RPC
/// </summary>
public class McpTool
{
    /// <summary>
    /// The name of the tool (lowercase with underscores)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Human-readable description of what the tool does
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema defining the input parameters for the tool
    /// </summary>
    public required JsonDocument InputSchema { get; init; }
}

/// <summary>
/// Request to invoke a tool
/// </summary>
public class ToolCallRequest
{
    /// <summary>
    /// Name of the tool to invoke
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Arguments to pass to the tool (JSON object)
    /// </summary>
    public required JsonDocument Arguments { get; init; }
}

/// <summary>
/// Result of a tool invocation
/// </summary>
public class ToolCallResult
{
    /// <summary>
    /// Whether the tool execution was successful
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Result data from the tool (if successful)
    /// </summary>
    public object? Result { get; init; }

    /// <summary>
    /// Error message (if failed)
    /// </summary>
    public string? Error { get; init; }
}

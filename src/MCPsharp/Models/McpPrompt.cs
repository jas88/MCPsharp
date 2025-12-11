using System.Text.Json.Serialization;

namespace MCPsharp.Models;

/// <summary>
/// Represents an MCP prompt template.
/// </summary>
public sealed class McpPrompt
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("arguments")]
    public IReadOnlyList<McpPromptArgument>? Arguments { get; set; }
}

/// <summary>
/// Represents an argument for an MCP prompt.
/// </summary>
public sealed class McpPromptArgument
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>
/// Result of prompts/list request.
/// </summary>
public sealed class PromptListResult
{
    [JsonPropertyName("prompts")]
    public required IReadOnlyList<McpPrompt> Prompts { get; set; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Result of prompts/get request.
/// </summary>
public sealed class PromptGetResult
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<PromptMessage> Messages { get; set; }
}

/// <summary>
/// A message in a prompt template.
/// </summary>
public sealed class PromptMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }  // "user" or "assistant"

    [JsonPropertyName("content")]
    public required PromptContent Content { get; set; }
}

/// <summary>
/// Content of a prompt message.
/// </summary>
public sealed class PromptContent
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }  // "text"

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

/// <summary>
/// Arguments for prompts/get request.
/// </summary>
public sealed class PromptGetArguments
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("arguments")]
    public Dictionary<string, string>? Arguments { get; set; }
}

using System.Text.Json.Serialization;

namespace MCPsharp.Models;

/// <summary>
/// Represents an MCP resource that can be listed and read.
/// </summary>
public sealed class McpResource
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

/// <summary>
/// Content of an MCP resource.
/// </summary>
public sealed class McpResourceContent
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("blob")]
    public string? Blob { get; set; }  // Base64 encoded binary content
}

/// <summary>
/// Result of resources/list request.
/// </summary>
public sealed class ResourceListResult
{
    [JsonPropertyName("resources")]
    public required IReadOnlyList<McpResource> Resources { get; set; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; set; }
}

/// <summary>
/// Result of resources/read request.
/// </summary>
public sealed class ResourceReadResult
{
    [JsonPropertyName("contents")]
    public required IReadOnlyList<McpResourceContent> Contents { get; set; }
}

/// <summary>
/// Arguments for resources/read request.
/// </summary>
public sealed class ResourceReadArguments
{
    [JsonPropertyName("uri")]
    public required string Uri { get; set; }
}

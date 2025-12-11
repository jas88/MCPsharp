using System.Collections.Concurrent;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Manages MCP resources that can be listed and read by clients.
/// </summary>
public sealed class McpResourceRegistry
{
    private readonly ConcurrentDictionary<string, RegisteredResource> _resources = new();
    private readonly ILogger<McpResourceRegistry>? _logger;

    private sealed class RegisteredResource
    {
        public required McpResource Metadata { get; init; }
        public required Func<Task<McpResourceContent>> ContentProvider { get; init; }
    }

    public McpResourceRegistry(ILogger<McpResourceRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a resource with a content provider.
    /// </summary>
    /// <param name="resource">Resource metadata.</param>
    /// <param name="contentProvider">Async function that provides the resource content.</param>
    public void RegisterResource(McpResource resource, Func<Task<McpResourceContent>> contentProvider)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(contentProvider);

        var registered = new RegisteredResource
        {
            Metadata = resource,
            ContentProvider = contentProvider
        };

        if (_resources.TryAdd(resource.Uri, registered))
        {
            _logger?.LogDebug("Registered resource: {Uri}", resource.Uri);
        }
        else
        {
            // Update existing
            _resources[resource.Uri] = registered;
            _logger?.LogDebug("Updated resource: {Uri}", resource.Uri);
        }
    }

    /// <summary>
    /// Registers a resource with synchronous content.
    /// </summary>
    public void RegisterResource(McpResource resource, Func<McpResourceContent> contentProvider)
    {
        RegisterResource(resource, () => Task.FromResult(contentProvider()));
    }

    /// <summary>
    /// Registers a resource with static content.
    /// </summary>
    public void RegisterStaticResource(McpResource resource, string content)
    {
        var resourceContent = new McpResourceContent
        {
            Uri = resource.Uri,
            MimeType = resource.MimeType ?? "text/plain",
            Text = content
        };
        RegisterResource(resource, () => resourceContent);
    }

    /// <summary>
    /// Unregisters a resource by URI.
    /// </summary>
    public bool UnregisterResource(string uri)
    {
        var removed = _resources.TryRemove(uri, out _);
        if (removed)
        {
            _logger?.LogDebug("Unregistered resource: {Uri}", uri);
        }
        return removed;
    }

    /// <summary>
    /// Lists all registered resources.
    /// </summary>
    public ResourceListResult ListResources()
    {
        var resources = _resources.Values
            .Select(r => r.Metadata)
            .OrderBy(r => r.Uri)
            .ToList();

        return new ResourceListResult
        {
            Resources = resources
        };
    }

    /// <summary>
    /// Reads a resource by URI.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Thrown if resource not found.</exception>
    public async Task<ResourceReadResult> ReadResourceAsync(string uri)
    {
        if (!_resources.TryGetValue(uri, out var resource))
        {
            throw new KeyNotFoundException($"Resource not found: {uri}");
        }

        var content = await resource.ContentProvider().ConfigureAwait(false);

        return new ResourceReadResult
        {
            Contents = [content]
        };
    }

    /// <summary>
    /// Checks if a resource exists.
    /// </summary>
    public bool HasResource(string uri) => _resources.ContainsKey(uri);

    /// <summary>
    /// Gets the count of registered resources.
    /// </summary>
    public int Count => _resources.Count;

    /// <summary>
    /// Clears all registered resources.
    /// </summary>
    public void Clear()
    {
        _resources.Clear();
        _logger?.LogDebug("Cleared all resources");
    }
}

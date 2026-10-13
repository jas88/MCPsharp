using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Comprehensive unit tests for McpResourceRegistry covering resource registration,
/// listing, reading, and utility methods with extensive edge case testing.
/// </summary>
public partial class McpResourceRegistryTests
{
    private static McpResourceRegistry CreateRegistry() => new McpResourceRegistry();

    // ===== Resource Registration Tests =====

    [Test]
    public void RegisterResource_AddsNewResource()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource",
            Name = "Test Resource",
            Description = "A test resource"
        };

        // Act
        registry.RegisterResource(resource, () => new McpResourceContent
        {
            Uri = resource.Uri,
            Text = "content"
        });

        // Assert
        Assert.That(registry.HasResource("test://resource"), Is.True);
        Assert.That(registry.Count, Is.EqualTo(1));
    }

    [Test]
    public void RegisterResource_UpdatesExistingResource()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource",
            Name = "Original Name"
        };

        registry.RegisterResource(resource, () => new McpResourceContent
        {
            Uri = resource.Uri,
            Text = "original content"
        });

        var updatedResource = new McpResource
        {
            Uri = "test://resource",
            Name = "Updated Name"
        };

        // Act
        registry.RegisterResource(updatedResource, () => new McpResourceContent
        {
            Uri = updatedResource.Uri,
            Text = "updated content"
        });

        // Assert
        Assert.That(registry.Count, Is.EqualTo(1)); // Should still be 1, not 2
        var result = registry.ListResources();
        Assert.That(result.Resources, Has.Count.EqualTo(1));
        Assert.That(result.Resources[0].Name, Is.EqualTo("Updated Name"));
    }

    [Test]
    public async Task RegisterResource_WithAsyncContentProvider_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://async",
            Name = "Async Resource"
        };

        // Act
        registry.RegisterResource(resource, async () =>
        {
            await Task.Delay(10); // Simulate async work
            return new McpResourceContent
            {
                Uri = resource.Uri,
                Text = "async content"
            };
        });

        // Assert
        var result = await registry.ReadResourceAsync("test://async");
        Assert.That(result.Contents, Has.Count.EqualTo(1));
        Assert.That(result.Contents[0].Text, Is.EqualTo("async content"));
    }

    [Test]
    public void RegisterResource_WithSyncContentProvider_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://sync",
            Name = "Sync Resource"
        };

        // Act
        registry.RegisterResource(resource, () => new McpResourceContent
        {
            Uri = resource.Uri,
            Text = "sync content"
        });

        // Assert
        Assert.That(registry.HasResource("test://sync"), Is.True);
    }

    [Test]
    public void RegisterStaticResource_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://static",
            Name = "Static Resource"
        };

        // Act
        registry.RegisterStaticResource(resource, "Hello World");

        // Assert
        Assert.That(registry.HasResource("test://static"), Is.True);
    }

    [Test]
    public async Task RegisterStaticResource_PreservesContent()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://static",
            Name = "Static Resource"
        };

        // Act
        registry.RegisterStaticResource(resource, "Hello World");
        var result = await registry.ReadResourceAsync("test://static");

        // Assert
        Assert.That(result.Contents[0].Text, Is.EqualTo("Hello World"));
    }

    [Test]
    public async Task RegisterStaticResource_SetsDefaultMimeType()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://plain",
            Name = "Plain Resource"
        };

        // Act
        registry.RegisterStaticResource(resource, "plain text");
        var result = await registry.ReadResourceAsync("test://plain");

        // Assert
        Assert.That(result.Contents[0].MimeType, Is.EqualTo("text/plain"));
    }

    [Test]
    public async Task RegisterStaticResource_PreservesCustomMimeType()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://json",
            Name = "JSON Resource",
            MimeType = "application/json"
        };

        // Act
        registry.RegisterStaticResource(resource, "{\"key\":\"value\"}");
        var result = await registry.ReadResourceAsync("test://json");

        // Assert
        Assert.That(result.Contents[0].MimeType, Is.EqualTo("application/json"));
    }

    [Test]
    public void RegisterResource_ThrowsOnNullResource()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterResource(null!, () => new McpResourceContent
            {
                Uri = "test://resource",
                Text = "content"
            }));
    }

    [Test]
    public void RegisterResource_ThrowsOnNullAsyncContentProvider()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource",
            Name = "Test"
        };

        // Act & Assert
        Func<Task<McpResourceContent>>? nullFunc = null;
        Assert.Throws<ArgumentNullException>(() =>
            registry.RegisterResource(resource, nullFunc!));
    }

    [Test]
    public void UnregisterResource_RemovesExistingResource()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource",
            Name = "Test Resource"
        };
        registry.RegisterStaticResource(resource, "content");

        // Act
        var removed = registry.UnregisterResource("test://resource");

        // Assert
        Assert.That(removed, Is.True);
        Assert.That(registry.HasResource("test://resource"), Is.False);
        Assert.That(registry.Count, Is.EqualTo(0));
    }

    [Test]
    public void UnregisterResource_ReturnsFalseForNonExistentResource()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var removed = registry.UnregisterResource("test://nonexistent");

        // Assert
        Assert.That(removed, Is.False);
    }

    [Test]
    public void UnregisterResource_CanRemoveAndReRegister()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource",
            Name = "Test Resource"
        };
        registry.RegisterStaticResource(resource, "original");
        registry.UnregisterResource("test://resource");

        // Act
        registry.RegisterStaticResource(resource, "new content");

        // Assert
        Assert.That(registry.HasResource("test://resource"), Is.True);
        Assert.That(registry.Count, Is.EqualTo(1));
    }
}

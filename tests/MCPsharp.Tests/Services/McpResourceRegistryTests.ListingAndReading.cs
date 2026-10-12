using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpResourceRegistryTests
{
    // ===== Resource Listing Tests =====

    [Test]
    public void ListResources_ReturnsAllResources()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource1 = new McpResource { Uri = "test://res1", Name = "Resource 1" };
        var resource2 = new McpResource { Uri = "test://res2", Name = "Resource 2" };
        var resource3 = new McpResource { Uri = "test://res3", Name = "Resource 3" };

        registry.RegisterStaticResource(resource1, "content1");
        registry.RegisterStaticResource(resource2, "content2");
        registry.RegisterStaticResource(resource3, "content3");

        // Act
        var result = registry.ListResources();

        // Assert
        Assert.That(result.Resources, Is.Not.Null);
        Assert.That(result.Resources, Has.Count.EqualTo(3));
    }

    [Test]
    public void ListResources_ReturnsEmptyWhenNoResources()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var result = registry.ListResources();

        // Assert
        Assert.That(result.Resources, Is.Not.Null);
        Assert.That(result.Resources, Is.Empty);
    }

    [Test]
    public void ListResources_ResourcesAreSortedByUri()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource1 = new McpResource { Uri = "test://z", Name = "Z Resource" };
        var resource2 = new McpResource { Uri = "test://a", Name = "A Resource" };
        var resource3 = new McpResource { Uri = "test://m", Name = "M Resource" };

        // Register in non-alphabetical order
        registry.RegisterStaticResource(resource1, "z");
        registry.RegisterStaticResource(resource2, "a");
        registry.RegisterStaticResource(resource3, "m");

        // Act
        var result = registry.ListResources();

        // Assert
        Assert.That(result.Resources[0].Uri, Is.EqualTo("test://a"));
        Assert.That(result.Resources[1].Uri, Is.EqualTo("test://m"));
        Assert.That(result.Resources[2].Uri, Is.EqualTo("test://z"));
    }

    [Test]
    public void ListResources_ContainsResourceMetadata()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource",
            Name = "Test Resource",
            Description = "A test resource",
            MimeType = "text/plain"
        };
        registry.RegisterStaticResource(resource, "content");

        // Act
        var result = registry.ListResources();

        // Assert
        var listed = result.Resources[0];
        Assert.That(listed.Uri, Is.EqualTo("test://resource"));
        Assert.That(listed.Name, Is.EqualTo("Test Resource"));
        Assert.That(listed.Description, Is.EqualTo("A test resource"));
        Assert.That(listed.MimeType, Is.EqualTo("text/plain"));
    }

    // ===== Resource Reading Tests =====

    [Test]
    public async Task ReadResourceAsync_ReturnsContent()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://res", Name = "Test" };
        registry.RegisterStaticResource(resource, "Hello World");

        // Act
        var result = await registry.ReadResourceAsync("test://res");

        // Assert
        Assert.That(result.Contents, Is.Not.Null);
        Assert.That(result.Contents, Has.Count.EqualTo(1));
        Assert.That(result.Contents[0].Text, Is.EqualTo("Hello World"));
    }

    [Test]
    public async Task ReadResourceAsync_ThrowsForUnknownUri()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await registry.ReadResourceAsync("unknown://uri"));

        Assert.That(exception!.Message, Does.Contain("Resource not found"));
        Assert.That(exception.Message, Does.Contain("unknown://uri"));
    }

    [Test]
    public async Task ReadResourceAsync_ContentProviderIsInvokedEachTime()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://counter", Name = "Counter" };
        var counter = 0;

        registry.RegisterResource(resource, () => new McpResourceContent
        {
            Uri = resource.Uri,
            Text = $"Count: {++counter}"
        });

        // Act
        var result1 = await registry.ReadResourceAsync("test://counter");
        var result2 = await registry.ReadResourceAsync("test://counter");

        // Assert
        Assert.That(result1.Contents[0].Text, Is.EqualTo("Count: 1"));
        Assert.That(result2.Contents[0].Text, Is.EqualTo("Count: 2"));
    }

    [Test]
    public async Task ReadResourceAsync_PreservesContentUri()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://res", Name = "Test" };
        registry.RegisterStaticResource(resource, "content");

        // Act
        var result = await registry.ReadResourceAsync("test://res");

        // Assert
        Assert.That(result.Contents[0].Uri, Is.EqualTo("test://res"));
    }

    [Test]
    public async Task ReadResourceAsync_SupportsTextContent()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://text", Name = "Text" };
        registry.RegisterResource(resource, () => new McpResourceContent
        {
            Uri = resource.Uri,
            Text = "text content",
            MimeType = "text/plain"
        });

        // Act
        var result = await registry.ReadResourceAsync("test://text");

        // Assert
        Assert.That(result.Contents[0].Text, Is.EqualTo("text content"));
        Assert.That(result.Contents[0].Blob, Is.Null);
    }

    [Test]
    public async Task ReadResourceAsync_SupportsBlobContent()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://blob", Name = "Blob" };
        var base64Data = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });

        registry.RegisterResource(resource, () => new McpResourceContent
        {
            Uri = resource.Uri,
            Blob = base64Data,
            MimeType = "application/octet-stream"
        });

        // Act
        var result = await registry.ReadResourceAsync("test://blob");

        // Assert
        Assert.That(result.Contents[0].Blob, Is.EqualTo(base64Data));
        Assert.That(result.Contents[0].Text, Is.Null);
    }
}

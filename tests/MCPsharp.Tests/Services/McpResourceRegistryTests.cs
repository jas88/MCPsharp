using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Comprehensive unit tests for McpResourceRegistry covering resource registration,
/// listing, reading, and utility methods with extensive edge case testing.
/// </summary>
public class McpResourceRegistryTests
{
    private McpResourceRegistry CreateRegistry() => new McpResourceRegistry();

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
        var exception = Assert.ThrowsAsync<KeyNotFoundException>(
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

    // ===== Utility Methods Tests =====

    [Test]
    public void HasResource_ReturnsTrueForExistingResource()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://exists", Name = "Test" };
        registry.RegisterStaticResource(resource, "content");

        // Act
        var exists = registry.HasResource("test://exists");

        // Assert
        Assert.That(exists, Is.True);
    }

    [Test]
    public void HasResource_ReturnsFalseForNonExistentResource()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var exists = registry.HasResource("test://nonexistent");

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public void HasResource_ReturnsFalseAfterUnregister()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://temp", Name = "Test" };
        registry.RegisterStaticResource(resource, "content");
        registry.UnregisterResource("test://temp");

        // Act
        var exists = registry.HasResource("test://temp");

        // Assert
        Assert.That(exists, Is.False);
    }

    [Test]
    public void Count_IsAccurate()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert - empty
        Assert.That(registry.Count, Is.EqualTo(0));

        // Add resources
        registry.RegisterStaticResource(new McpResource { Uri = "test://1", Name = "1" }, "1");
        Assert.That(registry.Count, Is.EqualTo(1));

        registry.RegisterStaticResource(new McpResource { Uri = "test://2", Name = "2" }, "2");
        Assert.That(registry.Count, Is.EqualTo(2));

        registry.RegisterStaticResource(new McpResource { Uri = "test://3", Name = "3" }, "3");
        Assert.That(registry.Count, Is.EqualTo(3));

        // Update existing (count should not change)
        registry.RegisterStaticResource(new McpResource { Uri = "test://1", Name = "Updated" }, "updated");
        Assert.That(registry.Count, Is.EqualTo(3));

        // Remove resource
        registry.UnregisterResource("test://2");
        Assert.That(registry.Count, Is.EqualTo(2));
    }

    [Test]
    public void Clear_RemovesAllResources()
    {
        // Arrange
        var registry = CreateRegistry();
        registry.RegisterStaticResource(new McpResource { Uri = "test://1", Name = "1" }, "1");
        registry.RegisterStaticResource(new McpResource { Uri = "test://2", Name = "2" }, "2");
        registry.RegisterStaticResource(new McpResource { Uri = "test://3", Name = "3" }, "3");

        // Act
        registry.Clear();

        // Assert
        Assert.That(registry.Count, Is.EqualTo(0));
        Assert.That(registry.HasResource("test://1"), Is.False);
        Assert.That(registry.HasResource("test://2"), Is.False);
        Assert.That(registry.HasResource("test://3"), Is.False);
        Assert.That(registry.ListResources().Resources, Is.Empty);
    }

    [Test]
    public void Clear_OnEmptyRegistry_DoesNotThrow()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        registry.Clear();
        Assert.That(registry.Count, Is.EqualTo(0));
    }

    // ===== Edge Case Tests =====

    [Test]
    public async Task RegisterResource_WithEmptyContent_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://empty", Name = "Empty" };
        registry.RegisterStaticResource(resource, "");

        // Act
        var result = await registry.ReadResourceAsync("test://empty");

        // Assert
        Assert.That(result.Contents[0].Text, Is.EqualTo(""));
    }

    [Test]
    public async Task RegisterResource_WithVeryLongContent_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://long", Name = "Long" };
        var longContent = new string('x', 100000); // 100KB of text
        registry.RegisterStaticResource(resource, longContent);

        // Act
        var result = await registry.ReadResourceAsync("test://long");

        // Assert
        Assert.That(result.Contents[0].Text, Is.EqualTo(longContent));
    }

    [Test]
    public void RegisterResource_WithSpecialCharactersInUri_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource
        {
            Uri = "test://resource-with-dashes_and_underscores/path?query=value#fragment",
            Name = "Special URI"
        };

        // Act
        registry.RegisterStaticResource(resource, "content");

        // Assert
        Assert.That(registry.HasResource("test://resource-with-dashes_and_underscores/path?query=value#fragment"), Is.True);
    }

    [Test]
    public async Task RegisterResource_WithContentProviderException_ThrowsOnRead()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://error", Name = "Error" };

        registry.RegisterResource(resource, () => Task.FromException<McpResourceContent>(
            new InvalidOperationException("Content provider failed")));

        // Act & Assert
        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.ReadResourceAsync("test://error"));

        Assert.That(exception!.Message, Is.EqualTo("Content provider failed"));
    }

    [Test]
    public async Task RegisterResource_ConcurrentRegistration_ThreadSafe()
    {
        // Arrange
        var registry = CreateRegistry();
        var tasks = new List<Task>();

        // Act - Register 100 resources concurrently
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var resource = new McpResource
                {
                    Uri = $"test://concurrent{index}",
                    Name = $"Concurrent {index}"
                };
                registry.RegisterStaticResource(resource, $"content{index}");
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.That(registry.Count, Is.EqualTo(100));
        for (int i = 0; i < 100; i++)
        {
            Assert.That(registry.HasResource($"test://concurrent{i}"), Is.True);
        }
    }

    [Test]
    public async Task RegisterResource_ConcurrentReadAndWrite_ThreadSafe()
    {
        // Arrange
        var registry = CreateRegistry();
        for (int i = 0; i < 10; i++)
        {
            var resource = new McpResource
            {
                Uri = $"test://shared{i}",
                Name = $"Shared {i}"
            };
            registry.RegisterStaticResource(resource, $"initial{i}");
        }

        var tasks = new List<Task>();

        // Act - Concurrent reads and updates
        for (int i = 0; i < 50; i++)
        {
            var index = i % 10;

            // Add read task
            tasks.Add(Task.Run(async () =>
            {
                await registry.ReadResourceAsync($"test://shared{index}");
            }));

            // Add update task
            tasks.Add(Task.Run(() =>
            {
                var resource = new McpResource
                {
                    Uri = $"test://shared{index}",
                    Name = $"Updated {index}"
                };
                registry.RegisterStaticResource(resource, $"updated{index}");
            }));
        }

        // Assert - Should complete without exceptions
        var exception = await Task.Run(async () =>
        {
            try
            {
                await Task.WhenAll(tasks);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });
        Assert.That(exception, Is.Null);
    }

    [Test]
    public void ListResources_WithManyResources_PerformanceTest()
    {
        // Arrange
        var registry = CreateRegistry();
        for (int i = 0; i < 1000; i++)
        {
            var resource = new McpResource
            {
                Uri = $"test://perf{i:D4}",
                Name = $"Performance Test {i}"
            };
            registry.RegisterStaticResource(resource, $"content{i}");
        }

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = registry.ListResources();
        sw.Stop();

        // Assert
        Assert.That(result.Resources.Count, Is.EqualTo(1000));
        Assert.That(sw.ElapsedMilliseconds, Is.LessThan(1000), $"ListResources took {sw.ElapsedMilliseconds}ms (expected < 1000ms)");
    }
}

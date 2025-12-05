using MCPsharp.Models;
using MCPsharp.Services;
using Xunit;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Comprehensive unit tests for McpResourceRegistry covering resource registration,
/// listing, reading, and utility methods with extensive edge case testing.
/// </summary>
public class McpResourceRegistryTests
{
    private McpResourceRegistry CreateRegistry() => new McpResourceRegistry();

    // ===== Resource Registration Tests =====

    [Fact]
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
        Assert.True(registry.HasResource("test://resource"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
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
        Assert.Equal(1, registry.Count); // Should still be 1, not 2
        var result = registry.ListResources();
        Assert.Single(result.Resources);
        Assert.Equal("Updated Name", result.Resources[0].Name);
    }

    [Fact]
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
        Assert.Single(result.Contents);
        Assert.Equal("async content", result.Contents[0].Text);
    }

    [Fact]
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
        Assert.True(registry.HasResource("test://sync"));
    }

    [Fact]
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
        Assert.True(registry.HasResource("test://static"));
    }

    [Fact]
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
        Assert.Equal("Hello World", result.Contents[0].Text);
    }

    [Fact]
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
        Assert.Equal("text/plain", result.Contents[0].MimeType);
    }

    [Fact]
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
        Assert.Equal("application/json", result.Contents[0].MimeType);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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
        Assert.True(removed);
        Assert.False(registry.HasResource("test://resource"));
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void UnregisterResource_ReturnsFalseForNonExistentResource()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var removed = registry.UnregisterResource("test://nonexistent");

        // Assert
        Assert.False(removed);
    }

    [Fact]
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
        Assert.True(registry.HasResource("test://resource"));
        Assert.Equal(1, registry.Count);
    }

    // ===== Resource Listing Tests =====

    [Fact]
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
        Assert.NotNull(result.Resources);
        Assert.Equal(3, result.Resources.Count);
    }

    [Fact]
    public void ListResources_ReturnsEmptyWhenNoResources()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var result = registry.ListResources();

        // Assert
        Assert.NotNull(result.Resources);
        Assert.Empty(result.Resources);
    }

    [Fact]
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
        Assert.Equal("test://a", result.Resources[0].Uri);
        Assert.Equal("test://m", result.Resources[1].Uri);
        Assert.Equal("test://z", result.Resources[2].Uri);
    }

    [Fact]
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
        Assert.Equal("test://resource", listed.Uri);
        Assert.Equal("Test Resource", listed.Name);
        Assert.Equal("A test resource", listed.Description);
        Assert.Equal("text/plain", listed.MimeType);
    }

    // ===== Resource Reading Tests =====

    [Fact]
    public async Task ReadResourceAsync_ReturnsContent()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://res", Name = "Test" };
        registry.RegisterStaticResource(resource, "Hello World");

        // Act
        var result = await registry.ReadResourceAsync("test://res");

        // Assert
        Assert.NotNull(result.Contents);
        Assert.Single(result.Contents);
        Assert.Equal("Hello World", result.Contents[0].Text);
    }

    [Fact]
    public async Task ReadResourceAsync_ThrowsForUnknownUri()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await registry.ReadResourceAsync("unknown://uri"));

        Assert.Contains("Resource not found", exception.Message);
        Assert.Contains("unknown://uri", exception.Message);
    }

    [Fact]
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
        Assert.Equal("Count: 1", result1.Contents[0].Text);
        Assert.Equal("Count: 2", result2.Contents[0].Text);
    }

    [Fact]
    public async Task ReadResourceAsync_PreservesContentUri()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://res", Name = "Test" };
        registry.RegisterStaticResource(resource, "content");

        // Act
        var result = await registry.ReadResourceAsync("test://res");

        // Assert
        Assert.Equal("test://res", result.Contents[0].Uri);
    }

    [Fact]
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
        Assert.Equal("text content", result.Contents[0].Text);
        Assert.Null(result.Contents[0].Blob);
    }

    [Fact]
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
        Assert.Equal(base64Data, result.Contents[0].Blob);
        Assert.Null(result.Contents[0].Text);
    }

    // ===== Utility Methods Tests =====

    [Fact]
    public void HasResource_ReturnsTrueForExistingResource()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://exists", Name = "Test" };
        registry.RegisterStaticResource(resource, "content");

        // Act
        var exists = registry.HasResource("test://exists");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void HasResource_ReturnsFalseForNonExistentResource()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act
        var exists = registry.HasResource("test://nonexistent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
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
        Assert.False(exists);
    }

    [Fact]
    public void Count_IsAccurate()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert - empty
        Assert.Equal(0, registry.Count);

        // Add resources
        registry.RegisterStaticResource(new McpResource { Uri = "test://1", Name = "1" }, "1");
        Assert.Equal(1, registry.Count);

        registry.RegisterStaticResource(new McpResource { Uri = "test://2", Name = "2" }, "2");
        Assert.Equal(2, registry.Count);

        registry.RegisterStaticResource(new McpResource { Uri = "test://3", Name = "3" }, "3");
        Assert.Equal(3, registry.Count);

        // Update existing (count should not change)
        registry.RegisterStaticResource(new McpResource { Uri = "test://1", Name = "Updated" }, "updated");
        Assert.Equal(3, registry.Count);

        // Remove resource
        registry.UnregisterResource("test://2");
        Assert.Equal(2, registry.Count);
    }

    [Fact]
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
        Assert.Equal(0, registry.Count);
        Assert.False(registry.HasResource("test://1"));
        Assert.False(registry.HasResource("test://2"));
        Assert.False(registry.HasResource("test://3"));
        Assert.Empty(registry.ListResources().Resources);
    }

    [Fact]
    public void Clear_OnEmptyRegistry_DoesNotThrow()
    {
        // Arrange
        var registry = CreateRegistry();

        // Act & Assert
        registry.Clear();
        Assert.Equal(0, registry.Count);
    }

    // ===== Edge Case Tests =====

    [Fact]
    public async Task RegisterResource_WithEmptyContent_Works()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://empty", Name = "Empty" };
        registry.RegisterStaticResource(resource, "");

        // Act
        var result = await registry.ReadResourceAsync("test://empty");

        // Assert
        Assert.Equal("", result.Contents[0].Text);
    }

    [Fact]
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
        Assert.Equal(longContent, result.Contents[0].Text);
    }

    [Fact]
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
        Assert.True(registry.HasResource("test://resource-with-dashes_and_underscores/path?query=value#fragment"));
    }

    [Fact]
    public async Task RegisterResource_WithContentProviderException_ThrowsOnRead()
    {
        // Arrange
        var registry = CreateRegistry();
        var resource = new McpResource { Uri = "test://error", Name = "Error" };

        registry.RegisterResource(resource, () => Task.FromException<McpResourceContent>(
            new InvalidOperationException("Content provider failed")));

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await registry.ReadResourceAsync("test://error"));

        Assert.Equal("Content provider failed", exception.Message);
    }

    [Fact]
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
        Assert.Equal(100, registry.Count);
        for (int i = 0; i < 100; i++)
        {
            Assert.True(registry.HasResource($"test://concurrent{i}"));
        }
    }

    [Fact]
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
        var aggregateException = await Record.ExceptionAsync(async () => await Task.WhenAll(tasks));
        Assert.Null(aggregateException);
    }

    [Fact]
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
        Assert.Equal(1000, result.Resources.Count);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"ListResources took {sw.ElapsedMilliseconds}ms (expected < 1000ms)");
    }
}

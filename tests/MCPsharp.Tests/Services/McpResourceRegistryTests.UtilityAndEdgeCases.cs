using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpResourceRegistryTests
{
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
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
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

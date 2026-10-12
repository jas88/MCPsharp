using MCPsharp.Models;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpPromptRegistryTests
{
    #region Custom Registration Tests

    [Test]
    public void RegisterPrompt_AddsCustomPrompt()
    {
        var customPrompt = new McpPrompt
        {
            Name = "custom-test",
            Description = "Test custom prompt"
        };

        _registry.RegisterPrompt(customPrompt, _ => new PromptGetResult
        {
            Description = "Custom description",
            Messages =
            [
                new PromptMessage
                {
                    Role = "user",
                    Content = new PromptContent
                    {
                        Type = "text",
                        Text = "Custom content"
                    }
                }
            ]
        });

        Assert.Multiple(() =>
        {
            Assert.That(_registry.Count, Is.EqualTo(4));
            Assert.That(_registry.HasPrompt("custom-test"), Is.True);
        });

        var result = _registry.GetPrompt("custom-test");
        Assert.That(result.Messages[0].Content.Text, Is.EqualTo("Custom content"));
    }

    [Test]
    public void RegisterPrompt_UpdatesExistingPrompt()
    {
        var originalResult = _registry.GetPrompt("analyze-codebase");
        var originalText = originalResult.Messages[0].Content.Text;

        var updatedPrompt = new McpPrompt
        {
            Name = "analyze-codebase",
            Description = "Updated description"
        };

        _registry.RegisterPrompt(updatedPrompt, _ => new PromptGetResult
        {
            Description = "New description",
            Messages =
            [
                new PromptMessage
                {
                    Role = "user",
                    Content = new PromptContent
                    {
                        Type = "text",
                        Text = "Updated content"
                    }
                }
            ]
        });

        Assert.Multiple(() =>
        {
            // Count should remain the same (replacement, not addition)
            Assert.That(_registry.Count, Is.EqualTo(3));

            // Content should be updated
            var updatedResult = _registry.GetPrompt("analyze-codebase");
            Assert.That(updatedResult.Messages[0].Content.Text, Is.EqualTo("Updated content"));
            Assert.That(updatedResult.Messages[0].Content.Text, Is.Not.EqualTo(originalText));
        });
    }

    [Test]
    public void RegisterPrompt_WithNullPrompt_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _registry.RegisterPrompt(null!, _ => new PromptGetResult
            {
                Messages = []
            }));
    }

    [Test]
    public void RegisterPrompt_WithNullContentProvider_ThrowsArgumentNullException()
    {
        var prompt = new McpPrompt
        {
            Name = "test",
            Description = "Test"
        };

        Assert.Throws<ArgumentNullException>(() =>
            _registry.RegisterPrompt(prompt, null!));
    }

    [Test]
    public void RegisterPrompt_WithDynamicContentProvider_CallsProviderOnEachGet()
    {
        var callCount = 0;
        var prompt = new McpPrompt
        {
            Name = "dynamic-test",
            Description = "Test dynamic content"
        };

        _registry.RegisterPrompt(prompt, _ =>
        {
            callCount++;
            return new PromptGetResult
            {
                Messages =
                [
                    new PromptMessage
                    {
                        Role = "user",
                        Content = new PromptContent
                        {
                            Type = "text",
                            Text = $"Call {callCount}"
                        }
                    }
                ]
            };
        });

        var result1 = _registry.GetPrompt("dynamic-test");
        var result2 = _registry.GetPrompt("dynamic-test");

        Assert.Multiple(() =>
        {
            Assert.That(callCount, Is.EqualTo(2));
            Assert.That(result1.Messages[0].Content.Text, Is.EqualTo("Call 1"));
            Assert.That(result2.Messages[0].Content.Text, Is.EqualTo("Call 2"));
        });
    }

    #endregion

    #region Utility Methods Tests

    [Test]
    public void HasPrompt_ReturnsTrueForExistingPrompt()
    {
        Assert.That(_registry.HasPrompt("analyze-codebase"), Is.True);
    }

    [Test]
    public void HasPrompt_ReturnsFalseForNonExistentPrompt()
    {
        Assert.That(_registry.HasPrompt("non-existent"), Is.False);
    }

    [Test]
    public void HasPrompt_IsCaseSensitive()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_registry.HasPrompt("analyze-codebase"), Is.True);
            Assert.That(_registry.HasPrompt("Analyze-Codebase"), Is.False);
            Assert.That(_registry.HasPrompt("ANALYZE-CODEBASE"), Is.False);
        });
    }

    [Test]
    public void Count_ReturnsAccurateCount()
    {
        Assert.That(_registry.Count, Is.EqualTo(3));

        var customPrompt = new McpPrompt
        {
            Name = "custom",
            Description = "Test"
        };

        _registry.RegisterPrompt(customPrompt, _ => new PromptGetResult { Messages = [] });

        Assert.That(_registry.Count, Is.EqualTo(4));
    }

    [Test]
    public void Count_DoesNotChangeOnReplacement()
    {
        var initialCount = _registry.Count;

        var updatedPrompt = new McpPrompt
        {
            Name = "analyze-codebase",
            Description = "Updated"
        };

        _registry.RegisterPrompt(updatedPrompt, _ => new PromptGetResult { Messages = [] });

        Assert.That(_registry.Count, Is.EqualTo(initialCount));
    }

    #endregion
}

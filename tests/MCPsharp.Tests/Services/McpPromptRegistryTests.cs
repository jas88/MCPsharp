using NUnit.Framework;
using MCPsharp.Services;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Unit tests for McpPromptRegistry service.
/// </summary>
[TestFixture]
public class McpPromptRegistryTests
{
    private McpPromptRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new McpPromptRegistry();
    }

    #region Built-in Prompts Tests

    [Test]
    public void Registry_InitializesWithBuiltInPrompts()
    {
        // Built-in prompts should be registered automatically
        Assert.That(_registry.Count, Is.EqualTo(3), "Expected exactly 3 built-in prompts");
        Assert.Multiple(() =>
        {
            Assert.That(_registry.HasPrompt("analyze-codebase"), Is.True);
            Assert.That(_registry.HasPrompt("implement-feature"), Is.True);
            Assert.That(_registry.HasPrompt("fix-bug"), Is.True);
        });
    }

    [Test]
    public void AnalyzeCodebasePrompt_Exists()
    {
        var result = _registry.GetPrompt("analyze-codebase");

        Assert.Multiple(() =>
        {
            Assert.That(result.Messages, Is.Not.Empty);
            Assert.That(result.Messages[0].Role, Is.EqualTo("user"));
            Assert.That(result.Messages[0].Content.Type, Is.EqualTo("text"));
            Assert.That(result.Messages[0].Content.Text, Does.Contain("MCPsharp"));
            Assert.That(result.Messages[0].Content.Text, Does.Contain("find_symbol"));
            Assert.That(result.Description, Does.Contain("Analyze codebase"));
        });
    }

    [Test]
    public void ImplementFeaturePrompt_Exists()
    {
        var result = _registry.GetPrompt("implement-feature",
            new Dictionary<string, string> { { "feature", "authentication" } });

        Assert.Multiple(() =>
        {
            Assert.That(result.Messages, Is.Not.Empty);
            Assert.That(result.Messages[0].Role, Is.EqualTo("user"));
            Assert.That(result.Messages[0].Content.Type, Is.EqualTo("text"));
            Assert.That(result.Messages[0].Content.Text, Does.Contain("authentication"));
            Assert.That(result.Messages[0].Content.Text, Does.Contain("ask_codebase"));
            Assert.That(result.Description, Does.Contain("Implement a feature"));
        });
    }

    [Test]
    public void FixBugPrompt_Exists()
    {
        var result = _registry.GetPrompt("fix-bug",
            new Dictionary<string, string> { { "issue", "null reference" } });

        Assert.Multiple(() =>
        {
            Assert.That(result.Messages, Is.Not.Empty);
            Assert.That(result.Messages[0].Role, Is.EqualTo("user"));
            Assert.That(result.Messages[0].Content.Type, Is.EqualTo("text"));
            Assert.That(result.Messages[0].Content.Text, Does.Contain("null reference"));
            Assert.That(result.Messages[0].Content.Text, Does.Contain("find_call_chains"));
            Assert.That(result.Description, Does.Contain("Debug and fix"));
        });
    }

    #endregion

    #region Prompt Listing Tests

    [Test]
    public void ListPrompts_ReturnsAllPrompts()
    {
        var result = _registry.ListPrompts();

        Assert.That(result.Prompts, Is.Not.Empty);
        Assert.That(result.Prompts.Count, Is.EqualTo(3));
        Assert.That(result.Prompts.Select(p => p.Name),
            Is.EquivalentTo(new[] { "analyze-codebase", "implement-feature", "fix-bug" }));
    }

    [Test]
    public void ListPrompts_ReturnsSortedByName()
    {
        var result = _registry.ListPrompts();

        var names = result.Prompts.Select(p => p.Name).ToList();
        var sortedNames = names.OrderBy(n => n).ToList();

        Assert.That(names, Is.EqualTo(sortedNames), "Prompts should be sorted by name");
    }

    [Test]
    public void ListPrompts_IncludesMetadata()
    {
        var result = _registry.ListPrompts();

        foreach (var prompt in result.Prompts)
        {
            Assert.Multiple(() =>
            {
                Assert.That(prompt.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(prompt.Description, Is.Not.Null.And.Not.Empty);
            });
        }
    }

    [Test]
    public void ListPrompts_IncludesArgumentsWhereApplicable()
    {
        var result = _registry.ListPrompts();

        var analyzePrompt = result.Prompts.First(p => p.Name == "analyze-codebase");
        var implementPrompt = result.Prompts.First(p => p.Name == "implement-feature");
        var fixBugPrompt = result.Prompts.First(p => p.Name == "fix-bug");

        Assert.Multiple(() =>
        {
            // analyze-codebase has no arguments
            Assert.That(analyzePrompt.Arguments, Is.Null.Or.Empty);

            // implement-feature has 'feature' argument
            Assert.That(implementPrompt.Arguments, Is.Not.Null);
            Assert.That(implementPrompt.Arguments!.Count, Is.EqualTo(1));
            Assert.That(implementPrompt.Arguments![0].Name, Is.EqualTo("feature"));
            Assert.That(implementPrompt.Arguments![0].Required, Is.True);

            // fix-bug has 'issue' argument
            Assert.That(fixBugPrompt.Arguments, Is.Not.Null);
            Assert.That(fixBugPrompt.Arguments!.Count, Is.EqualTo(1));
            Assert.That(fixBugPrompt.Arguments![0].Name, Is.EqualTo("issue"));
            Assert.That(fixBugPrompt.Arguments![0].Required, Is.True);
        });
    }

    #endregion

    #region Prompt Retrieval Tests

    [Test]
    public void GetPrompt_ReturnsPromptContent()
    {
        var result = _registry.GetPrompt("analyze-codebase");

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Messages, Is.Not.Empty);
            Assert.That(result.Messages[0].Role, Is.EqualTo("user"));
            Assert.That(result.Messages[0].Content.Type, Is.EqualTo("text"));
            Assert.That(result.Messages[0].Content.Text, Is.Not.Empty);
        });
    }

    [Test]
    public void GetPrompt_ThrowsKeyNotFoundException_ForUnknownName()
    {
        var ex = Assert.Throws<KeyNotFoundException>(
            () => _registry.GetPrompt("unknown-prompt"));

        Assert.That(ex!.Message, Does.Contain("unknown-prompt"));
    }

    [Test]
    public void GetPrompt_WithoutArguments_UsesEmptyString()
    {
        var result = _registry.GetPrompt("implement-feature");

        // When no arguments provided (null), args?.GetValueOrDefault returns null, which interpolates as empty string
        Assert.That(result.Messages[0].Content.Text, Does.Contain("To implement ''"));
    }

    [Test]
    public void GetPrompt_WithArguments_SubstitutesValues()
    {
        var result = _registry.GetPrompt("implement-feature",
            new Dictionary<string, string> { { "feature", "dark mode" } });

        Assert.Multiple(() =>
        {
            Assert.That(result.Messages[0].Content.Text, Does.Contain("dark mode"));
            Assert.That(result.Messages[0].Content.Text, Does.Not.Contain("[feature]"));
        });
    }

    [Test]
    public void GetPrompt_WithMultipleSubstitutions_ReplacesAllOccurrences()
    {
        var result = _registry.GetPrompt("fix-bug",
            new Dictionary<string, string> { { "issue", "memory leak in cache" } });

        Assert.Multiple(() =>
        {
            Assert.That(result.Messages[0].Content.Text, Does.Contain("memory leak in cache"));
            Assert.That(result.Messages[0].Content.Text, Does.Not.Contain("[bug]"));
        });
    }

    [Test]
    public void GetPrompt_WithEmptyArguments_UsesDefaultPlaceholder()
    {
        var result = _registry.GetPrompt("implement-feature", new Dictionary<string, string>());

        Assert.That(result.Messages[0].Content.Text, Does.Contain("[feature]"));
    }

    [Test]
    public void GetPrompt_WithNullArguments_UsesEmptyString()
    {
        var result = _registry.GetPrompt("implement-feature", null);

        // When null is passed, args?.GetValueOrDefault returns null, which interpolates as empty string
        Assert.That(result.Messages[0].Content.Text, Does.Contain("To implement ''"));
    }

    #endregion

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

    #region Logger Integration Tests

    [Test]
    public void Registry_CanBeCreatedWithLogger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<McpPromptRegistry>();

        Assert.DoesNotThrow(() => new McpPromptRegistry(logger));
    }

    [Test]
    public void Registry_CanBeCreatedWithoutLogger()
    {
        Assert.DoesNotThrow(() => new McpPromptRegistry(null));
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public void Registry_ThreadSafeRegistration()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            int index = i;
            tasks.Add(Task.Run(() =>
            {
                var prompt = new McpPrompt
                {
                    Name = $"concurrent-{index}",
                    Description = $"Test {index}"
                };

                _registry.RegisterPrompt(prompt, _ => new PromptGetResult
                {
                    Messages =
                    [
                        new PromptMessage
                        {
                            Role = "user",
                            Content = new PromptContent
                            {
                                Type = "text",
                                Text = $"Content {index}"
                            }
                        }
                    ]
                });
            }));
        }

        Assert.DoesNotThrow(() => Task.WaitAll(tasks.ToArray()));
        Assert.That(_registry.Count, Is.EqualTo(13)); // 3 built-in + 10 custom
    }

    [Test]
    public void Registry_ThreadSafeRetrieval()
    {
        var tasks = new List<Task>();

        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                Assert.DoesNotThrow(() => _registry.GetPrompt("analyze-codebase"));
                Assert.DoesNotThrow(() => _registry.ListPrompts());
                Assert.DoesNotThrow(() => _registry.HasPrompt("fix-bug"));
            }));
        }

        Assert.DoesNotThrow(() => Task.WaitAll(tasks.ToArray()));
    }

    #endregion

    #region Edge Cases

    [Test]
    public void GetPrompt_WithSpecialCharactersInArguments_SubstitutesCorrectly()
    {
        var result = _registry.GetPrompt("implement-feature",
            new Dictionary<string, string>
            {
                { "feature", "user's \"special\" <feature> & more" }
            });

        Assert.That(result.Messages[0].Content.Text,
            Does.Contain("user's \"special\" <feature> & more"));
    }

    [Test]
    public void GetPrompt_WithEmptyStringArgument_SubstitutesEmptyString()
    {
        var result = _registry.GetPrompt("implement-feature",
            new Dictionary<string, string> { { "feature", "" } });

        // Should contain empty string substitution, not the default placeholder
        var text = result.Messages[0].Content.Text;
        Assert.That(text, Does.Contain("To implement ''"));
    }

    [Test]
    public void GetPrompt_WithVeryLongArgument_SubstitutesCorrectly()
    {
        var longText = new string('x', 1000);
        var result = _registry.GetPrompt("implement-feature",
            new Dictionary<string, string> { { "feature", longText } });

        Assert.That(result.Messages[0].Content.Text, Does.Contain(longText));
    }

    [Test]
    public void ListPrompts_AfterMultipleRegistrations_MaintainsSortOrder()
    {
        // Add prompts in non-alphabetical order
        var prompts = new[] { "zebra", "alpha", "mike", "bravo" };

        foreach (var name in prompts)
        {
            _registry.RegisterPrompt(
                new McpPrompt { Name = name, Description = "Test" },
                _ => new PromptGetResult { Messages = [] });
        }

        var result = _registry.ListPrompts();
        var names = result.Prompts.Select(p => p.Name).ToList();

        Assert.That(names, Is.Ordered);
    }

    #endregion
}

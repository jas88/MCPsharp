using MCPsharp.Models;
using MCPsharp.Services;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// Unit tests for McpPromptRegistry service.
/// </summary>
[TestFixture]
public partial class McpPromptRegistryTests
{
    private static readonly string[] ExpectedPromptNames = ["analyze-codebase", "implement-feature", "fix-bug"];

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

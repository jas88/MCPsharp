using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public partial class McpPromptRegistryTests
{
    #region Prompt Listing Tests

    [Test]
    public void ListPrompts_ReturnsAllPrompts()
    {
        var result = _registry.ListPrompts();

        Assert.That(result.Prompts, Is.Not.Empty);
        Assert.That(result.Prompts.Count, Is.EqualTo(3));
        Assert.That(result.Prompts.Select(p => p.Name),
            Is.EquivalentTo(ExpectedPromptNames));
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
}

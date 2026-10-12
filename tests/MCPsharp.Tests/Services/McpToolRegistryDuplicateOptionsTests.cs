using System.Text.Json;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

/// <summary>
/// The duplicate detection tools' "options" objects must actually reach the service options.
/// </summary>
[TestFixture]
public class McpToolRegistryDuplicateOptionsTests
{
    private static JsonDocument Arguments(string json) => JsonDocument.Parse(json);

    [Test]
    public void ParseDuplicateDetectionOptions_ReadsEveryOption()
    {
        using var arguments = Arguments("""
            {"options": {"minBlockSize": 7, "maxBlockSize": 70, "similarityThreshold": 0.55,
                         "ignoreGeneratedCode": false, "ignoreTestCode": true, "ignoreTrivialDifferences": false,
                         "detectionTypes": ["methods", "Classes", "unknown"]}}
            """);

        var options = McpToolRegistry.ParseDuplicateDetectionOptions(arguments);

        Assert.That(options.MinBlockSize, Is.EqualTo(7));
        Assert.That(options.MaxBlockSize, Is.EqualTo(70));
        Assert.That(options.SimilarityThreshold, Is.EqualTo(0.55));
        Assert.That(options.IgnoreGeneratedCode, Is.False);
        Assert.That(options.IgnoreTestCode, Is.True);
        Assert.That(options.IgnoreTrivialDifferences, Is.False);
        Assert.That(options.DetectionTypes, Is.EqualTo(DuplicateDetectionTypes.Methods | DuplicateDetectionTypes.Classes));
    }

    [Test]
    public void ParseDuplicateDetectionOptions_WithoutOptions_KeepsDefaults()
    {
        using var arguments = Arguments("""{"projectPath": "x"}""");

        var options = McpToolRegistry.ParseDuplicateDetectionOptions(arguments);

        var defaults = new DuplicateDetectionOptions();
        Assert.That(options.MinBlockSize, Is.EqualTo(defaults.MinBlockSize));
        Assert.That(options.SimilarityThreshold, Is.EqualTo(defaults.SimilarityThreshold));
        Assert.That(options.DetectionTypes, Is.EqualTo(defaults.DetectionTypes));
        Assert.That(options.IgnoreGeneratedCode, Is.EqualTo(defaults.IgnoreGeneratedCode));
    }

    [Test]
    public void ParseRefactoringOptions_ReadsEveryOption()
    {
        using var arguments = Arguments("""
            {"options": {"maxSuggestions": 3, "prioritizeBreakingChanges": true, "refactoringTypes": ["ExtractMethod", "utilityclass"]}}
            """);

        var options = McpToolRegistry.ParseRefactoringOptions(arguments);

        Assert.That(options.MaxSuggestions, Is.EqualTo(3));
        Assert.That(options.PrioritizeBreakingChanges, Is.True);
        Assert.That(options.RefactoringTypes, Is.EqualTo(RefactoringTypes.ExtractMethod | RefactoringTypes.UtilityClass));
    }

    [Test]
    public void ParseCodeComparisonOptions_ReadsEveryOption()
    {
        using var arguments = Arguments("""
            {"options": {"ignoreWhitespace": false, "ignoreComments": false, "ignoreIdentifiers": true,
                         "ignoreStringLiterals": true, "ignoreNumericLiterals": true}}
            """);

        var options = McpToolRegistry.ParseCodeComparisonOptions(arguments);

        Assert.That(options.IgnoreWhitespace, Is.False);
        Assert.That(options.IgnoreComments, Is.False);
        Assert.That(options.IgnoreIdentifiers, Is.True);
        Assert.That(options.IgnoreStringLiterals, Is.True);
        Assert.That(options.IgnoreNumericLiterals, Is.True);
    }
}

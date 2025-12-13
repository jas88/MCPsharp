using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Services;
using NUnit.Framework;

namespace MCPsharp.Tests.Services;

public class ResponseProcessorTests
{
    private ResponseConfiguration _defaultConfig = null!;
    private ResponseProcessor _processor = null!;

    [SetUp]
    public void SetUp()
    {
        _defaultConfig = new ResponseConfiguration
        {
            MaxTokens = 100,
            EnableTruncation = true,
            TruncateLength = 200
        };
        _processor = new ResponseProcessor(_defaultConfig);
    }

    [Test]
    public void ProcessResponse_SmallResponse_ShouldNotTruncate()
    {
        // Arrange
        var smallResponse = new { message = "This is a small response" };
        var toolName = "test_tool";

        // Act
        var result = _processor.ProcessResponse(smallResponse, toolName);

        // Assert
        Assert.That(result.WasTruncated, Is.False);
        Assert.That(result.EstimatedTokenCount, Is.EqualTo(result.OriginalTokenCount));
        Assert.That(result.Warning, Is.Null);
    }

    [Test]
    public void ProcessResponse_LargeResponse_ShouldTruncate()
    {
        // Arrange
        var largeResponse = new
        {
            message = "This is a large response",
            data = new string('x', 2000) // This should exceed our 100 token limit
        };
        var toolName = "test_tool";

        // Act
        var result = _processor.ProcessResponse(largeResponse, toolName);

        // Assert
        Assert.That(result.WasTruncated, Is.True);
        Assert.That(result.EstimatedTokenCount, Is.LessThanOrEqualTo(_defaultConfig.MaxTokens));
        Assert.That(result.OriginalTokenCount, Is.GreaterThan(_defaultConfig.MaxTokens));
        Assert.That(result.Warning, Is.Not.Null);
    }

    [Test]
    public void ProcessResponse_ExemptTool_ShouldNotTruncate()
    {
        // Arrange
        var config = new ResponseConfiguration
        {
            MaxTokens = 10,
            EnableTruncation = true,
            ExemptTools = new HashSet<string> { "exempt_tool" }
        };
        var processor = new ResponseProcessor(config);
        var largeResponse = new { data = new string('x', 1000) };
        var toolName = "exempt_tool";

        // Act
        var result = processor.ProcessResponse(largeResponse, toolName);

        // Assert
        Assert.That(result.WasTruncated, Is.False);
        Assert.That(result.EstimatedTokenCount, Is.GreaterThan(config.MaxTokens));
        Assert.That(result.Metadata.ContainsKey("exempt"), Is.True);
    }

    [Test]
    public void ProcessResponse_TruncationDisabled_ShouldReturnError()
    {
        // Arrange
        var config = new ResponseConfiguration
        {
            MaxTokens = 10,
            EnableTruncation = false
        };
        var processor = new ResponseProcessor(config);
        var largeResponse = new { data = new string('x', 1000) };
        var toolName = "test_tool";

        // Act
        var result = processor.ProcessResponse(largeResponse, toolName);

        // Assert
        Assert.That(result.WasTruncated, Is.False);
        Assert.That(result.EstimatedTokenCount, Is.GreaterThan(config.MaxTokens));
        Assert.That(result.Warning, Is.Not.Null);
        Assert.That(result.Warning, Does.Contain("exceeds token limit"));
    }

    [TestCase(SummaryStyle.Ellipsis)]
    [TestCase(SummaryStyle.Paragraphs)]
    [TestCase(SummaryStyle.JsonStructure)]
    [TestCase(SummaryStyle.LineBased)]
    public void ProcessResponse_DifferentSummaryStyles_ShouldProduceValidResults(SummaryStyle style)
    {
        // Arrange
        var config = new ResponseConfiguration
        {
            MaxTokens = 50,
            EnableTruncation = true,
            SummaryStyle = style
        };
        var processor = new ResponseProcessor(config);
        var largeResponse = new
        {
            content = "This is a test response with multiple paragraphs.\n\n" +
                     "Second paragraph with more content.\n\n" +
                     "Third paragraph with even more content to ensure truncation.",
            data = new List<string> { "item1", "item2", "item3", "item4", "item5" }
        };
        var toolName = "test_tool";

        // Act
        var result = processor.ProcessResponse(largeResponse, toolName);

        // Assert
        Assert.That(result.EstimatedTokenCount, Is.LessThanOrEqualTo(config.MaxTokens));
        Assert.That(result.Content, Is.Not.Null);
        Assert.That(result.Metadata.GetValueOrDefault("truncationStyle")?.ToString() ?? "", Does.Contain(style.ToString()));
    }

    [Test]
    public void ProcessResponse_NearWarningThreshold_ShouldIncludeWarning()
    {
        // Arrange
        var config = new ResponseConfiguration
        {
            MaxTokens = 100,
            WarningThreshold = 0.5
        };
        var processor = new ResponseProcessor(config);
        var mediumResponse = new { data = new string('x', 300) }; // Should be around 75 tokens
        var toolName = "test_tool";

        // Act
        var result = processor.ProcessResponse(mediumResponse, toolName);

        // Assert
        Assert.That(result.WasTruncated, Is.False);
        Assert.That(result.Warning, Is.Not.Null);
        Assert.That(result.Warning, Does.Contain("using"));
    }

    [Test]
    public void LoadFromEnvironment_ShouldApplyEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MCP_MAX_TOKENS", "500");
        Environment.SetEnvironmentVariable("MCP_ENABLE_TRUNCATION", "false");
        Environment.SetEnvironmentVariable("MCP_SUMMARY_STYLE", "JsonStructure");
        Environment.SetEnvironmentVariable("MCP_EXEMPT_TOOLS", "tool1,tool2,tool3");

        // Act
        var config = ResponseConfiguration.LoadFromEnvironment();

        // Assert
        Assert.That(config.MaxTokens, Is.EqualTo(500));
        Assert.That(config.EnableTruncation, Is.False);
        Assert.That(config.SummaryStyle, Is.EqualTo(SummaryStyle.JsonStructure));
        Assert.That(config.ExemptTools, Does.Contain("tool1"));
        Assert.That(config.ExemptTools, Does.Contain("tool2"));
        Assert.That(config.ExemptTools, Does.Contain("tool3"));

        // Cleanup
        Environment.SetEnvironmentVariable("MCP_MAX_TOKENS", null);
        Environment.SetEnvironmentVariable("MCP_ENABLE_TRUNCATION", null);
        Environment.SetEnvironmentVariable("MCP_SUMMARY_STYLE", null);
        Environment.SetEnvironmentVariable("MCP_EXEMPT_TOOLS", null);
    }

    [Test]
    public void ProcessResponse_WildcardExemption_ShouldMatchPattern()
    {
        // Arrange
        var config = new ResponseConfiguration
        {
            MaxTokens = 10,
            ExemptTools = new HashSet<string> { "stream_*", "bulk_*" }
        };
        var processor = new ResponseProcessor(config);
        var largeResponse = new { data = new string('x', 100) };

        // Act & Assert - stream tools should be exempt
        var streamResult = processor.ProcessResponse(largeResponse, "stream_process_file");
        Assert.That(streamResult.WasTruncated, Is.False);

        // Act & Assert - bulk tools should be exempt
        var bulkResult = processor.ProcessResponse(largeResponse, "bulk_replace");
        Assert.That(bulkResult.WasTruncated, Is.False);

        // Act & Assert - other tools should not be exempt
        var otherResult = processor.ProcessResponse(largeResponse, "other_tool");
        Assert.That(otherResult.WasTruncated, Is.True);
    }
}

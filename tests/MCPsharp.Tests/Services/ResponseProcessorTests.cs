using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Services;
using Xunit;

namespace MCPsharp.Tests.Services;

public class ResponseProcessorTests
{
    private readonly ResponseConfiguration _defaultConfig;
    private readonly ResponseProcessor _processor;

    public ResponseProcessorTests()
    {
        _defaultConfig = new ResponseConfiguration
        {
            MaxTokens = 100,
            EnableTruncation = true,
            TruncateLength = 200
        };
        _processor = new ResponseProcessor(_defaultConfig);
    }

    [Fact]
    public void ProcessResponse_SmallResponse_ShouldNotTruncate()
    {
        // Arrange
        var smallResponse = new { message = "This is a small response" };
        var toolName = "test_tool";

        // Act
        var result = _processor.ProcessResponse(smallResponse, toolName);

        // Assert
        Assert.False(result.WasTruncated);
        Assert.Equal(result.EstimatedTokenCount, result.OriginalTokenCount);
        Assert.Null(result.Warning);
    }

    [Fact]
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
        Assert.True(result.WasTruncated);
        Assert.True(result.EstimatedTokenCount <= _defaultConfig.MaxTokens);
        Assert.True(result.OriginalTokenCount > _defaultConfig.MaxTokens);
        Assert.NotNull(result.Warning);
    }

    [Fact]
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
        Assert.False(result.WasTruncated);
        Assert.True(result.EstimatedTokenCount > config.MaxTokens);
        Assert.True(result.Metadata.ContainsKey("exempt"));
    }

    [Fact]
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
        Assert.False(result.WasTruncated);
        Assert.True(result.EstimatedTokenCount > config.MaxTokens);
        Assert.NotNull(result.Warning);
        Assert.Contains("exceeds token limit", result.Warning);
    }

    [Theory]
    [InlineData(SummaryStyle.Ellipsis)]
    [InlineData(SummaryStyle.Paragraphs)]
    [InlineData(SummaryStyle.JsonStructure)]
    [InlineData(SummaryStyle.LineBased)]
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
        Assert.True(result.EstimatedTokenCount <= config.MaxTokens);
        Assert.NotNull(result.Content);
        Assert.Contains(style.ToString(), result.Metadata.GetValueOrDefault("truncationStyle")?.ToString() ?? "");
    }

    [Fact]
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
        Assert.False(result.WasTruncated);
        Assert.NotNull(result.Warning);
        Assert.Contains("using", result.Warning);
    }

    [Fact]
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
        Assert.Equal(500, config.MaxTokens);
        Assert.False(config.EnableTruncation);
        Assert.Equal(SummaryStyle.JsonStructure, config.SummaryStyle);
        Assert.Contains("tool1", config.ExemptTools);
        Assert.Contains("tool2", config.ExemptTools);
        Assert.Contains("tool3", config.ExemptTools);

        // Cleanup
        Environment.SetEnvironmentVariable("MCP_MAX_TOKENS", null);
        Environment.SetEnvironmentVariable("MCP_ENABLE_TRUNCATION", null);
        Environment.SetEnvironmentVariable("MCP_SUMMARY_STYLE", null);
        Environment.SetEnvironmentVariable("MCP_EXEMPT_TOOLS", null);
    }

    [Fact]
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
        Assert.False(streamResult.WasTruncated);

        // Act & Assert - bulk tools should be exempt
        var bulkResult = processor.ProcessResponse(largeResponse, "bulk_replace");
        Assert.False(bulkResult.WasTruncated);

        // Act & Assert - other tools should not be exempt
        var otherResult = processor.ProcessResponse(largeResponse, "other_tool");
        Assert.True(otherResult.WasTruncated);
    }
}
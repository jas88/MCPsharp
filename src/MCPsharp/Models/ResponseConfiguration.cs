namespace MCPsharp.Models;

/// <summary>
/// Configuration for managing tool response size and token limits
/// </summary>
public class ResponseConfiguration
{
    /// <summary>
    /// Maximum number of tokens allowed in a tool response (default: 4000)
    /// </summary>
    public int MaxTokens { get; set; } = 4000;

    /// <summary>
    /// Whether to enable automatic truncation of responses that exceed token limits
    /// </summary>
    public bool EnableTruncation { get; set; } = true;

    /// <summary>
    /// Style of summarization to use when truncating responses
    /// </summary>
    public SummaryStyle SummaryStyle { get; set; } = SummaryStyle.Ellipsis;

    /// <summary>
    /// Maximum number of characters to show when truncating with ellipsis
    /// </summary>
    public int TruncateLength { get; set; } = 1000;

    /// <summary>
    /// Whether to include token count information in response metadata
    /// </summary>
    public bool IncludeTokenCount { get; set; } = false;

    /// <summary>
    /// Tools that are exempt from token limiting (wildcards supported)
    /// </summary>
    public HashSet<string> ExemptTools { get; set; } = new();

    /// <summary>
    /// Warning threshold for token usage (percentage of MaxTokens)
    /// </summary>
    public double WarningThreshold { get; set; } = 0.8;

    /// <summary>
    /// Load configuration from environment variables
    /// </summary>
    public static ResponseConfiguration LoadFromEnvironment()
    {
        var config = new ResponseConfiguration();

        // Max tokens configuration
        if (int.TryParse(Environment.GetEnvironmentVariable("MCP_MAX_TOKENS"), out var maxTokens) && maxTokens > 0)
            config.MaxTokens = maxTokens;

        // Truncation settings
        if (bool.TryParse(Environment.GetEnvironmentVariable("MCP_ENABLE_TRUNCATION"), out var enableTruncation))
            config.EnableTruncation = enableTruncation;

        if (int.TryParse(Environment.GetEnvironmentVariable("MCP_TRUNCATE_LENGTH"), out var truncateLength) && truncateLength > 0)
            config.TruncateLength = truncateLength;

        // Summary style
        if (Enum.TryParse<SummaryStyle>(Environment.GetEnvironmentVariable("MCP_SUMMARY_STYLE"), true, out var summaryStyle))
            config.SummaryStyle = summaryStyle;

        // Metadata settings
        if (bool.TryParse(Environment.GetEnvironmentVariable("MCP_INCLUDE_TOKEN_COUNT"), out var includeTokenCount))
            config.IncludeTokenCount = includeTokenCount;

        // Warning threshold
        if (double.TryParse(Environment.GetEnvironmentVariable("MCP_WARNING_THRESHOLD"), out var warningThreshold) &&
            warningThreshold > 0 && warningThreshold <= 1.0)
            config.WarningThreshold = warningThreshold;

        // Exempt tools
        var exemptToolsEnv = Environment.GetEnvironmentVariable("MCP_EXEMPT_TOOLS");
        if (!string.IsNullOrEmpty(exemptToolsEnv))
        {
            var exemptTools = exemptToolsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => !string.IsNullOrEmpty(t));

            config.ExemptTools = new HashSet<string>(exemptTools, StringComparer.OrdinalIgnoreCase);
        }

        return config;
    }
}

/// <summary>
/// Styles for summarizing truncated responses
/// </summary>
public enum SummaryStyle
{
    /// <summary>
    /// Simple ellipsis truncation
    /// </summary>
    Ellipsis,

    /// <summary>
    /// Intelligent paragraph-based summarization
    /// </summary>
    Paragraphs,

    /// <summary>
    /// JSON-aware truncation that preserves structure
    /// </summary>
    JsonStructure,

    /// <summary>
    /// Line-based truncation for code and logs
    /// </summary>
    LineBased
}

/// <summary>
/// Result of response processing with token limiting
/// </summary>
public class ProcessedResponse
{
    /// <summary>
    /// The processed (possibly truncated) response content
    /// </summary>
    public required object Content { get; init; }

    /// <summary>
    /// Whether the response was truncated
    /// </summary>
    public bool WasTruncated { get; init; }

    /// <summary>
    /// Estimated token count of the response
    /// </summary>
    public int EstimatedTokenCount { get; init; }

    /// <summary>
    /// Original token count before truncation
    /// </summary>
    public int OriginalTokenCount { get; init; }

    /// <summary>
    /// Warning message if response is near token limit
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// Metadata about the processing
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}
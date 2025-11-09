using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for processing tool responses to respect token limits
/// </summary>
public class ResponseProcessor
{
    private readonly ResponseConfiguration _config;
    private readonly ILogger<ResponseProcessor>? _logger;

    // Approximate token to character ratio (4 chars ≈ 1 token for English text)
    private const double TokenToCharRatio = 0.25;

    // Patterns for different content types
    private static readonly Regex JsonPattern = new(@"^\s*[\[\{]", RegexOptions.Compiled);
    private static readonly Regex CodePattern = new(@"^\s*(using|namespace|public|private|class|interface|function|def|import|package)", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex LinePattern = new(@"\r?\n", RegexOptions.Compiled);

    public ResponseProcessor(ResponseConfiguration config, ILogger<ResponseProcessor>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <summary>
    /// Process a tool response to ensure it respects token limits
    /// </summary>
    public ProcessedResponse ProcessResponse(object response, string toolName)
    {
        // Check if tool is exempt from token limiting
        if (IsToolExempt(toolName))
        {
            var originalTokens = EstimateTokenCount(response);
            return new ProcessedResponse
            {
                Content = response,
                WasTruncated = false,
                EstimatedTokenCount = originalTokens,
                OriginalTokenCount = originalTokens,
                Metadata = { ["exempt"] = true }
            };
        }

        var originalTokenCount = EstimateTokenCount(response);

        // Check if response is within limits
        if (originalTokenCount <= _config.MaxTokens)
        {
            var warning = CheckForWarning(originalTokenCount);
            return new ProcessedResponse
            {
                Content = response,
                WasTruncated = false,
                EstimatedTokenCount = originalTokenCount,
                OriginalTokenCount = originalTokenCount,
                Warning = warning,
                Metadata = { ["toolName"] = toolName }
            };
        }

        // Response exceeds limits - truncate it
        if (!_config.EnableTruncation)
        {
            return new ProcessedResponse
            {
                Content = new
                {
                    error = $"Response exceeds token limit of {_config.MaxTokens} tokens",
                    tokenCount = originalTokenCount,
                    toolName
                },
                WasTruncated = false,
                EstimatedTokenCount = originalTokenCount,
                OriginalTokenCount = originalTokenCount,
                Warning = "Response exceeds token limit and truncation is disabled",
                Metadata = { ["toolName"] = toolName, ["error"] = true }
            };
        }

        // Perform truncation
        var truncatedContent = TruncateResponse(response, toolName);
        var truncatedTokens = EstimateTokenCount(truncatedContent);

        _logger?.LogInformation("Truncated response for tool {ToolName} from {OriginalTokens} to {TruncatedTokens} tokens",
            toolName, originalTokenCount, truncatedTokens);

        return new ProcessedResponse
        {
            Content = truncatedContent,
            WasTruncated = true,
            EstimatedTokenCount = truncatedTokens,
            OriginalTokenCount = originalTokenCount,
            Warning = $"Response truncated from {originalTokenCount} to {truncatedTokens} tokens",
            Metadata =
            {
                ["toolName"] = toolName,
                ["originalLength"] = originalTokenCount,
                ["truncatedLength"] = truncatedTokens,
                ["truncationStyle"] = _config.SummaryStyle.ToString()
            }
        };
    }

    /// <summary>
    /// Estimate token count for an object
    /// </summary>
    private int EstimateTokenCount(object obj)
    {
        if (obj == null) return 0;

        string jsonString;

        try
        {
            jsonString = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }
        catch
        {
            // Fallback to string representation
            jsonString = obj.ToString() ?? string.Empty;
        }

        // Simple estimation: characters / 4 ≈ tokens
        return (int)Math.Ceiling(jsonString.Length * TokenToCharRatio);
    }

    /// <summary>
    /// Check if a tool is exempt from token limiting
    /// </summary>
    private bool IsToolExempt(string toolName)
    {
        return _config.ExemptTools.Any(exempt =>
            string.Equals(exempt, toolName, StringComparison.OrdinalIgnoreCase) ||
            (exempt.EndsWith("*") && toolName.StartsWith(exempt[..^1], StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Check if response is near warning threshold
    /// </summary>
    private string? CheckForWarning(int tokenCount)
    {
        var threshold = (int)(_config.MaxTokens * _config.WarningThreshold);
        if (tokenCount >= threshold)
        {
            return $"Response is using {(double)tokenCount / _config.MaxTokens:P1} of token limit";
        }
        return null;
    }

    /// <summary>
    /// Truncate a response based on the configured summary style
    /// </summary>
    private object TruncateResponse(object response, string toolName)
    {
        try
        {
            var jsonString = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            return _config.SummaryStyle switch
            {
                SummaryStyle.Ellipsis => TruncateWithEllipsis(jsonString),
                SummaryStyle.Paragraphs => TruncateByParagraphs(jsonString),
                SummaryStyle.JsonStructure => TruncateJsonStructure(response),
                SummaryStyle.LineBased => TruncateByLines(jsonString),
                _ => TruncateWithEllipsis(jsonString)
            };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to truncate response, using fallback method");
            return TruncateWithEllipsis(response.ToString() ?? string.Empty);
        }
    }

    /// <summary>
    /// Simple ellipsis truncation
    /// </summary>
    private object TruncateWithEllipsis(string content)
    {
        const string suffix = "... [truncated]";

        // Calculate target length from MaxTokens, then limit by TruncateLength if set
        var tokenBasedLength = (int)(_config.MaxTokens / TokenToCharRatio);
        var targetLength = _config.TruncateLength > 0 ? Math.Min(_config.TruncateLength, tokenBasedLength) : tokenBasedLength;

        if (content.Length <= targetLength)
            return content;

        // Account for suffix length
        var contentTargetLength = Math.Max(0, targetLength - suffix.Length);

        // Try to truncate at a word boundary
        var truncated = content.Substring(0, contentTargetLength);
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > contentTargetLength * 0.8) // Only cut at word boundary if it's not too far back
        {
            truncated = truncated.Substring(0, lastSpace);
        }

        return $"{truncated}{suffix}";
    }

    /// <summary>
    /// Paragraph-based truncation
    /// </summary>
    private object TruncateByParagraphs(string content)
    {
        const string suffix = "\n\n... [truncated]";
        var targetChars = (int)(_config.MaxTokens / TokenToCharRatio);

        if (content.Length <= targetChars)
            return content;

        // Account for suffix length
        var contentTargetLength = Math.Max(0, targetChars - suffix.Length);

        // Unescape JSON to get actual newlines, then split by paragraphs
        var unescapedContent = content.Replace("\\n", "\n");
        var paragraphs = unescapedContent.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        var currentLength = 0;
        var resultParagraphs = new List<string>();

        foreach (var paragraph in paragraphs)
        {
            // Calculate length in original JSON format (with escaped newlines)
            var jsonLength = paragraph.Replace("\n", "\\n").Length;
            if (currentLength + jsonLength > contentTargetLength && resultParagraphs.Count > 0)
                break;

            resultParagraphs.Add(paragraph);
            currentLength += jsonLength + 4; // +4 for escaped paragraph break (\n\n becomes \\n\\n)
        }

        var result = string.Join("\n\n", resultParagraphs);

        // Re-escape newlines for JSON output
        var jsonResult = result.Replace("\n", "\\n");

        if (currentLength < content.Length)
        {
            jsonResult += suffix;
        }

        return jsonResult;
    }

    /// <summary>
    /// JSON-aware truncation that preserves structure
    /// </summary>
    private object TruncateJsonStructure(object response)
    {
        try
        {
            var jsonElement = JsonSerializer.SerializeToElement(response);
            return TruncateJsonElement(jsonElement, (int)(_config.MaxTokens * 0.8));
        }
        catch
        {
            return TruncateWithEllipsis(JsonSerializer.Serialize(response));
        }
    }

    /// <summary>
    /// Recursively truncate JSON element while preserving structure
    /// </summary>
    private object TruncateJsonElement(JsonElement element, int targetTokens)
    {
        var elementTokens = EstimateTokenCount(element);

        if (elementTokens <= targetTokens)
            return element;

        return element.ValueKind switch
        {
            JsonValueKind.Object => TruncateJsonObject(element, targetTokens),
            JsonValueKind.Array => TruncateJsonArray(element, targetTokens),
            JsonValueKind.String => TruncateJsonString(element.GetString() ?? string.Empty, targetTokens),
            _ => element
        };
    }

    /// <summary>
    /// Truncate JSON object by keeping most important properties
    /// </summary>
    private Dictionary<string, object> TruncateJsonObject(JsonElement obj, int targetTokens)
    {
        var result = new Dictionary<string, object>();
        var currentTokens = 2; // Start with overhead for {}

        // Get properties and sort by importance (simple heuristic: shorter values first)
        var properties = obj.EnumerateObject()
            .OrderBy(p => EstimateTokenCount(p.Value))
            .ToList();

        foreach (var prop in properties)
        {
            var propTokens = EstimateTokenCount(prop.Name) + EstimateTokenCount(prop.Value) + 3; // +3 for quotes and colon

            if (currentTokens + propTokens > targetTokens && result.Count > 0)
                break;

            result[prop.Name] = TruncateJsonElement(prop.Value, Math.Max(100, targetTokens - currentTokens));
            currentTokens += propTokens;
        }

        if (properties.Count > result.Count)
        {
            result["__truncated__"] = $"Original had {properties.Count} properties";
        }

        return result;
    }

    /// <summary>
    /// Truncate JSON array by keeping first elements
    /// </summary>
    private List<object> TruncateJsonArray(JsonElement array, int targetTokens)
    {
        var result = new List<object>();
        var currentTokens = 2; // Start with overhead for []

        foreach (var item in array.EnumerateArray())
        {
            var itemTokens = EstimateTokenCount(item) + 1; // +1 for comma

            if (currentTokens + itemTokens > targetTokens && result.Count > 0)
                break;

            result.Add(TruncateJsonElement(item, Math.Max(100, targetTokens - currentTokens)));
            currentTokens += itemTokens;
        }

        if (array.GetArrayLength() > result.Count)
        {
            result.Add($"... [{array.GetArrayLength() - result.Count} items truncated]");
        }

        return result;
    }

    /// <summary>
    /// Truncate JSON string
    /// </summary>
    private string TruncateJsonString(string str, int targetTokens)
    {
        const string suffix = "... [truncated]";
        var targetChars = (int)(targetTokens / TokenToCharRatio);
        if (str.Length <= targetChars)
            return str;

        // Account for suffix length
        var contentTargetLength = Math.Max(0, targetChars - suffix.Length);
        var truncated = str.Substring(0, contentTargetLength);
        return $"{truncated}{suffix}";
    }

    /// <summary>
    /// Line-based truncation for code and logs
    /// </summary>
    private object TruncateByLines(string content)
    {
        var suffix = Environment.NewLine + "... [truncated]";
        var targetChars = (int)(_config.MaxTokens / TokenToCharRatio);

        if (content.Length <= targetChars)
            return content;

        // Account for suffix length
        var contentTargetLength = Math.Max(0, targetChars - suffix.Length);

        // Unescape JSON to get actual newlines, then split by lines
        var unescapedContent = content.Replace("\\n", "\n");
        var lines = unescapedContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var currentLength = 0;
        var resultLines = new List<string>();

        foreach (var line in lines)
        {
            // Calculate length in original JSON format (with escaped newlines)
            var jsonLength = line.Replace("\n", "\\n").Length;
            if (currentLength + jsonLength > contentTargetLength && resultLines.Count > 0)
                break;

            resultLines.Add(line);
            currentLength += jsonLength + 2; // +2 for escaped newline (\n becomes \\n)
        }

        var result = string.Join("\n", resultLines);

        // Re-escape newlines for JSON output
        var jsonResult = result.Replace("\n", "\\n");

        if (currentLength < content.Length)
        {
            jsonResult += suffix;
        }

        return jsonResult;
    }
}
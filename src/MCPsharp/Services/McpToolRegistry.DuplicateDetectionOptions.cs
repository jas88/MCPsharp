using Microsoft.Extensions.Logging;
using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Argument parsing helpers for the duplicate code detection tools
/// </summary>
public partial class McpToolRegistry
{
    #region Helper Methods for Duplicate Code Detection

    /// <summary>
    /// The "options" object argument, when present.
    /// </summary>
    private static JsonElement? GetOptionsObject(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");
        return optionsElement is { ValueKind: JsonValueKind.Object } ? optionsElement : null;
    }

    private static int? GetIntOption(JsonElement options, string name) =>
        options.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static double? GetDoubleOption(JsonElement options, string name) =>
        options.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)
            ? result
            : null;

    private static bool? GetBoolOption(JsonElement options, string name) =>
        options.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    /// <summary>
    /// Combine the case-insensitive flag names in a string array option; unknown names are ignored.
    /// </summary>
    private static TFlags? GetFlagsOption<TFlags>(JsonElement options, string name) where TFlags : struct, Enum
    {
        if (!options.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        ulong combined = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Enum.TryParse<TFlags>(item.GetString(), true, out var flag))
            {
                combined |= Convert.ToUInt64(flag, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return (TFlags)Enum.ToObject(typeof(TFlags), combined);
    }

    internal static DuplicateDetectionOptions ParseDuplicateDetectionOptions(JsonDocument arguments)
    {
        var options = new DuplicateDetectionOptions();
        if (GetOptionsObject(arguments) is not { } element)
        {
            return options;
        }

        return options with
        {
            MinBlockSize = GetIntOption(element, "minBlockSize") ?? options.MinBlockSize,
            MaxBlockSize = GetIntOption(element, "maxBlockSize") ?? options.MaxBlockSize,
            SimilarityThreshold = GetDoubleOption(element, "similarityThreshold") ?? options.SimilarityThreshold,
            IgnoreGeneratedCode = GetBoolOption(element, "ignoreGeneratedCode") ?? options.IgnoreGeneratedCode,
            IgnoreTestCode = GetBoolOption(element, "ignoreTestCode") ?? options.IgnoreTestCode,
            IgnoreTrivialDifferences = GetBoolOption(element, "ignoreTrivialDifferences") ?? options.IgnoreTrivialDifferences,
            DetectionTypes = GetFlagsOption<DuplicateDetectionTypes>(element, "detectionTypes") ?? options.DetectionTypes
        };
    }

    internal static RefactoringOptions ParseRefactoringOptions(JsonDocument arguments)
    {
        var options = new RefactoringOptions();
        if (GetOptionsObject(arguments) is not { } element)
        {
            return options;
        }

        return options with
        {
            MaxSuggestions = GetIntOption(element, "maxSuggestions") ?? options.MaxSuggestions,
            PrioritizeBreakingChanges = GetBoolOption(element, "prioritizeBreakingChanges") ?? options.PrioritizeBreakingChanges,
            RefactoringTypes = GetFlagsOption<RefactoringTypes>(element, "refactoringTypes") ?? options.RefactoringTypes
        };
    }

    internal static CodeComparisonOptions ParseCodeComparisonOptions(JsonDocument arguments)
    {
        var options = new CodeComparisonOptions();
        if (GetOptionsObject(arguments) is not { } element)
        {
            return options;
        }

        return options with
        {
            IgnoreWhitespace = GetBoolOption(element, "ignoreWhitespace") ?? options.IgnoreWhitespace,
            IgnoreComments = GetBoolOption(element, "ignoreComments") ?? options.IgnoreComments,
            IgnoreIdentifiers = GetBoolOption(element, "ignoreIdentifiers") ?? options.IgnoreIdentifiers,
            IgnoreStringLiterals = GetBoolOption(element, "ignoreStringLiterals") ?? options.IgnoreStringLiterals,
            IgnoreNumericLiterals = GetBoolOption(element, "ignoreNumericLiterals") ?? options.IgnoreNumericLiterals
        };
    }

    private CodeBlock? ParseCodeBlockDefinition(JsonElement blockElement)
    {
        try
        {
            var blockObj = JsonSerializer.Deserialize<Dictionary<string, object>>(blockElement.GetRawText());
            if (blockObj == null) return null;

            if (!blockObj.TryGetValue("filePath", out var filePathObj) ||
                !blockObj.TryGetValue("startLine", out var startLineObj) ||
                !blockObj.TryGetValue("endLine", out var endLineObj))
            {
                return null;
            }

            var filePath = filePathObj.ToString();
            if (!int.TryParse(startLineObj.ToString(), out var startLine) ||
                !int.TryParse(endLineObj.ToString(), out var endLine))
            {
                return null;
            }

            // Create a basic code block definition
            // In a real implementation, we would need to read the actual source code and extract details
            return new CodeBlock
            {
                FilePath = filePath ?? string.Empty,
                StartLine = startLine,
                EndLine = endLine,
                StartColumn = 1,
                EndColumn = 100, // Placeholder
                SourceCode = $"// Code from {filePath}:{startLine}-{endLine}", // Placeholder
                NormalizedCode = $"// Code from {filePath}:{startLine}-{endLine}", // Placeholder
                CodeHash = ComputeHash($"{filePath}:{startLine}:{endLine}"),
                ElementType = CodeElementType.CodeBlock,
                ElementName = $"Block_{startLine}_{endLine}",
                Accessibility = Accessibility.Private,
                IsGenerated = false,
                IsTestCode = false,
                Complexity = new MCPsharp.Models.ComplexityMetrics
                {
                    CyclomaticComplexity = 1,
                    CognitiveComplexity = 1,
                    LinesOfCode = endLine - startLine + 1,
                    LogicalLinesOfCode = endLine - startLine + 1,
                    ParameterCount = 0,
                    NestingDepth = 1,
                    OverallScore = 1.0
                },
                Tokens = new List<CodeToken>(),
                AstStructure = new AstStructure
                {
                    StructuralHash = ComputeHash($"structure_{filePath}_{startLine}_{endLine}"),
                    NodeTypes = new List<string> { "Block" },
                    Depth = 1,
                    NodeCount = 1,
                    StructuralComplexity = 1.0,
                    ControlFlowPatterns = new List<ControlFlowPattern>(),
                    DataFlowPatterns = new List<DataFlowPattern>()
                }
            };
        }
        catch (Exception ex)
        {
            LogInvalidCodeBlockDefinition(_logger, ex);
            return null;
        }
    }

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse code block definition")]
    private static partial void LogInvalidCodeBlockDefinition(ILogger logger, Exception exception);
}

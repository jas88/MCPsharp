using Microsoft.Extensions.Logging;
using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Duplicate code detection tool execution methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    #region Duplicate Code Detection Tool Execution

    private async Task<ToolCallResult> ExecuteDetectDuplicates(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.DetectDuplicatesAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object?>
                {
                    ["duplicateGroups"] = result.DuplicateGroups,
                    ["metrics"] = result.Metrics,
                    ["refactoringSuggestions"] = result.RefactoringSuggestions,
                    ["hotspots"] = result.Hotspots,
                    ["analysisDuration"] = result.AnalysisDuration.TotalMilliseconds,
                    ["filesAnalyzed"] = result.FilesAnalyzed,
                    ["warnings"] = result.Warnings
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteDetectDuplicates));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error detecting duplicates: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteFindExactDuplicates(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.FindExactDuplicatesAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["duplicateGroups"] = result,
                    ["totalGroups"] = result.Count,
                    ["totalDuplicates"] = result.Sum(g => g.CodeBlocks.Count)
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteFindExactDuplicates));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error finding exact duplicates: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteFindNearDuplicates(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var similarityThreshold = GetDoubleFromArguments(arguments, "similarityThreshold", 0.8);
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.FindNearDuplicatesAsync(projectPath, similarityThreshold, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["duplicateGroups"] = result,
                    ["totalGroups"] = result.Count,
                    ["totalDuplicates"] = result.Sum(g => g.CodeBlocks.Count),
                    ["similarityThreshold"] = similarityThreshold,
                    ["averageSimilarity"] = result.Any() ? result.Average(g => g.SimilarityScore) : 0.0
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteFindNearDuplicates));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error finding near duplicates: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeDuplicationMetrics(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.AnalyzeDuplicationMetricsAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["metrics"] = result,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["totalDuplicateGroups"] = result.TotalDuplicateGroups,
                        ["exactDuplicates"] = result.ExactDuplicateGroups,
                        ["nearMissDuplicates"] = result.NearMissDuplicateGroups,
                        ["totalDuplicateLines"] = result.TotalDuplicateLines,
                        ["duplicationPercentage"] = result.DuplicationPercentage,
                        ["filesWithDuplicates"] = result.FilesWithDuplicates,
                        ["estimatedMaintenanceCost"] = result.EstimatedMaintenanceCost,
                        ["estimatedRefactoringSavings"] = result.EstimatedRefactoringSavings
                    }
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteAnalyzeDuplicationMetrics));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error analyzing duplication metrics: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetRefactoringSuggestions(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var duplicatesElement = GetPropertyFromArguments(arguments, "duplicates");
            var options = ParseRefactoringOptions(arguments);

            List<MCPsharp.Models.DuplicateGroup> duplicates;

            if (duplicatesElement.HasValue && duplicatesElement.Value.ValueKind == JsonValueKind.Array)
            {
                // Parse provided duplicates
                duplicates = JsonSerializer.Deserialize<List<MCPsharp.Models.DuplicateGroup>>(duplicatesElement.Value.GetRawText(), ToolArgumentJsonOptions) ?? new List<MCPsharp.Models.DuplicateGroup>();
            }
            else
            {
                // Duplicate detection results use a different model type than the refactoring API expects,
                // so no automatic detection is performed here; callers should pass 'duplicates' explicitly.
                duplicates = new List<MCPsharp.Models.DuplicateGroup>();
            }

            var result = await _duplicateCodeDetector.GetRefactoringSuggestionsAsync(projectPath, duplicates, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["suggestions"] = result,
                    ["totalSuggestions"] = result.Count,
                    ["highPrioritySuggestions"] = result.Count(s => s.Priority == RefactoringPriority.High || s.Priority == RefactoringPriority.Critical),
                    ["breakingChanges"] = result.Count(s => s.IsBreakingChange),
                    ["estimatedTotalEffort"] = result.Sum(s => s.EstimatedEffort),
                    ["estimatedTotalBenefit"] = result.Sum(s => s.EstimatedBenefit)
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetRefactoringSuggestions));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting refactoring suggestions: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetDuplicateHotspots(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var options = ParseDuplicateDetectionOptions(arguments);

            var result = await _duplicateCodeDetector.GetDuplicateHotspotsAsync(projectPath, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["hotspots"] = result,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["fileHotspots"] = result.FileHotspots.Count,
                        ["classHotspots"] = result.ClassHotspots.Count,
                        ["methodHotspots"] = result.MethodHotspots.Count,
                        ["directoryHotspots"] = result.DirectoryHotspots.Count,
                        ["criticalFileHotspots"] = result.FileHotspots.Count(f => f.RiskLevel == HotspotRiskLevel.Critical),
                        ["criticalMethodHotspots"] = result.MethodHotspots.Count(m => m.RiskLevel == HotspotRiskLevel.Critical)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetDuplicateHotspots));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting duplicate hotspots: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteCompareCodeBlocks(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var block1Element = GetPropertyFromArguments(arguments, "codeBlock1");
            var block2Element = GetPropertyFromArguments(arguments, "codeBlock2");
            var options = ParseCodeComparisonOptions(arguments);

            if (block1Element == null || block2Element == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Both codeBlock1 and codeBlock2 are required"
                };
            }

            var block1 = ParseCodeBlockDefinition(block1Element.Value);
            var block2 = ParseCodeBlockDefinition(block2Element.Value);

            if (block1 == null || block2 == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid code block definitions. Each block must include filePath, startLine, and endLine."
                };
            }

            var result = await _duplicateCodeDetector.CompareCodeBlocksAsync(block1, block2, options, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["similarity"] = new Dictionary<string, object>
                    {
                        ["overall"] = result.OverallSimilarity,
                        ["structural"] = result.StructuralSimilarity,
                        ["token"] = result.TokenSimilarity,
                        ["semantic"] = result.SemanticSimilarity
                    },
                    ["isDuplicate"] = result.IsDuplicate,
                    ["duplicateType"] = result.DuplicateType?.ToString() ?? "None",
                    ["differences"] = result.Differences,
                    ["commonPatterns"] = result.CommonPatterns,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["similarityPercentage"] = Math.Round(result.OverallSimilarity * 100, 2),
                        ["differenceCount"] = result.Differences.Count,
                        ["commonPatternCount"] = result.CommonPatterns.Count,
                        ["recommendation"] = result.IsDuplicate ? "Consider refactoring to eliminate duplication" : "Code blocks are sufficiently different"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteCompareCodeBlocks));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error comparing code blocks: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteValidateRefactoring(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_duplicateCodeDetector == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Duplicate code detection service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments) ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
            var suggestionElement = GetPropertyFromArguments(arguments, "suggestion");

            if (suggestionElement == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Refactoring suggestion is required"
                };
            }

            var suggestion = JsonSerializer.Deserialize<MCPsharp.Models.RefactoringSuggestion>(suggestionElement.Value.GetRawText(), ToolArgumentJsonOptions);
            if (suggestion == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid refactoring suggestion format"
                };
            }

            var result = await _duplicateCodeDetector.ValidateRefactoringAsync(projectPath, suggestion, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["isValid"] = result.IsValid,
                    ["overallRisk"] = result.OverallRisk.ToString(),
                    ["issues"] = result.Issues,
                    ["dependencyImpacts"] = result.DependencyImpacts,
                    ["recommendations"] = result.Recommendations,
                    ["summary"] = new Dictionary<string, object>
                    {
                        ["criticalIssues"] = result.Issues.Count(i => i.Severity == MCPsharp.Models.ValidationSeverity.Critical),
                        ["errorIssues"] = result.Issues.Count(i => i.Severity == MCPsharp.Models.ValidationSeverity.Error),
                        ["warningIssues"] = result.Issues.Count(i => i.Severity == MCPsharp.Models.ValidationSeverity.Warning),
                        ["breakingChanges"] = result.DependencyImpacts.Count(d => d.IsBreakingChange),
                        ["actionRequired"] = !result.IsValid || result.Issues.Any(i => i.Severity >= MCPsharp.Models.ValidationSeverity.Error)
                    }
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteValidateRefactoring));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error validating refactoring: {ex.Message}"
            };
        }
    }

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

    #endregion

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to parse code block definition")]
    private static partial void LogInvalidCodeBlockDefinition(ILogger logger, Exception exception);
}

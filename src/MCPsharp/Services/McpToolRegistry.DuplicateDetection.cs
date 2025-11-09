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
                Result = new Dictionary<string, object>
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
                duplicates = JsonSerializer.Deserialize<List<MCPsharp.Models.DuplicateGroup>>(duplicatesElement.Value.GetRawText()) ?? new List<MCPsharp.Models.DuplicateGroup>();
            }
            else
            {
                // Detect duplicates first
                var detectionOptions = ParseDuplicateDetectionOptions(arguments);
                duplicates = new List<MCPsharp.Models.DuplicateGroup>(); // Skip mapping for now since service expects different type
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

            var suggestion = JsonSerializer.Deserialize<MCPsharp.Models.RefactoringSuggestion>(suggestionElement.Value.GetRawText());
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
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error validating refactoring: {ex.Message}"
            };
        }
    }

    #region Helper Methods for Duplicate Code Detection

    private DuplicateDetectionOptions ParseDuplicateDetectionOptions(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");

        var options = new DuplicateDetectionOptions();

        if (optionsElement.HasValue && optionsElement.Value.ValueKind == JsonValueKind.Object)
        {
            var optionsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.Value.GetRawText());

            if (optionsObj != null)
            {
                if (optionsObj.TryGetValue("minBlockSize", out var minBlockSize) && minBlockSize is int minBlock)
                    options = options with { MinBlockSize = minBlock };

                if (optionsObj.TryGetValue("maxBlockSize", out var maxBlockSize) && maxBlockSize is int maxBlock)
                    options = options with { MaxBlockSize = maxBlock };

                if (optionsObj.TryGetValue("similarityThreshold", out var similarityThreshold) && similarityThreshold is double similarity)
                    options = options with { SimilarityThreshold = similarity };

                if (optionsObj.TryGetValue("ignoreGeneratedCode", out var ignoreGenerated) && ignoreGenerated is bool ignoreGen)
                    options = options with { IgnoreGeneratedCode = ignoreGen };

                if (optionsObj.TryGetValue("ignoreTestCode", out var ignoreTest) && ignoreTest is bool ignoreTestCode)
                    options = options with { IgnoreTestCode = ignoreTestCode };

                if (optionsObj.TryGetValue("ignoreTrivialDifferences", out var ignoreTrivial) && ignoreTrivial is bool ignoreTrivialDifferences)
                    options = options with { IgnoreTrivialDifferences = ignoreTrivialDifferences };

                if (optionsObj.TryGetValue("detectionTypes", out var detectionTypes) && detectionTypes is JsonElement typesElement && typesElement.ValueKind == JsonValueKind.Array)
                {
                    var typeStrings = JsonSerializer.Deserialize<string[]>(typesElement.GetRawText());
                    if (typeStrings != null)
                    {
                        var types = DuplicateDetectionTypes.None;
                        foreach (var typeString in typeStrings)
                        {
                            if (Enum.TryParse<DuplicateDetectionTypes>(typeString, true, out var type))
                                types |= type;
                        }
                        options = options with { DetectionTypes = types };
                    }
                }
            }
        }

        return options;
    }

    private RefactoringOptions ParseRefactoringOptions(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");

        var options = new RefactoringOptions();

        if (optionsElement.HasValue && optionsElement.Value.ValueKind == JsonValueKind.Object)
        {
            var optionsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.Value.GetRawText());

            if (optionsObj != null)
            {
                if (optionsObj.TryGetValue("maxSuggestions", out var maxSuggestions) && maxSuggestions is int maxSugg)
                    options = options with { MaxSuggestions = maxSugg };

                if (optionsObj.TryGetValue("prioritizeBreakingChanges", out var prioritizeBreaking) && prioritizeBreaking is bool prioritize)
                    options = options with { PrioritizeBreakingChanges = prioritize };

                if (optionsObj.TryGetValue("refactoringTypes", out var refactoringTypes) && refactoringTypes is JsonElement typesElement && typesElement.ValueKind == JsonValueKind.Array)
                {
                    var typeStrings = JsonSerializer.Deserialize<string[]>(typesElement.GetRawText());
                    if (typeStrings != null)
                    {
                        var types = RefactoringTypes.None;
                        foreach (var typeString in typeStrings)
                        {
                            if (Enum.TryParse<RefactoringTypes>(typeString, true, out var type))
                                types |= type;
                        }
                        options = options with { RefactoringTypes = types };
                    }
                }
            }
        }

        return options;
    }

    private CodeComparisonOptions ParseCodeComparisonOptions(JsonDocument arguments)
    {
        var optionsElement = GetPropertyFromArguments(arguments, "options");

        var options = new CodeComparisonOptions();

        if (optionsElement.HasValue && optionsElement.Value.ValueKind == JsonValueKind.Object)
        {
            var optionsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(optionsElement.Value.GetRawText());

            if (optionsObj != null)
            {
                if (optionsObj.TryGetValue("ignoreWhitespace", out var ignoreWhitespace) && ignoreWhitespace is bool ignoreWs)
                    options = options with { IgnoreWhitespace = ignoreWs };

                if (optionsObj.TryGetValue("ignoreComments", out var ignoreComments) && ignoreComments is bool ignoreComm)
                    options = options with { IgnoreComments = ignoreComm };

                if (optionsObj.TryGetValue("ignoreIdentifiers", out var ignoreIdentifiers) && ignoreIdentifiers is bool ignoreIds)
                    options = options with { IgnoreIdentifiers = ignoreIds };

                if (optionsObj.TryGetValue("ignoreStringLiterals", out var ignoreStrings) && ignoreStrings is bool ignoreStr)
                    options = options with { IgnoreStringLiterals = ignoreStr };

                if (optionsObj.TryGetValue("ignoreNumericLiterals", out var ignoreNumbers) && ignoreNumbers is bool ignoreNum)
                    options = options with { IgnoreNumericLiterals = ignoreNum };
            }
        }

        return options;
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
                FilePath = filePath,
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
        catch
        {
            return null;
        }
    }

    #endregion

    #endregion
}

using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.LargeFileOptimization;

namespace MCPsharp.Services;

/// <summary>
/// Large file optimization tool execution methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    private async Task<ToolCallResult> ExecuteAnalyzeLargeFiles(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var maxLines = GetIntArgument(arguments, "maxLines");

        try
        {
            var result = await _largeFileOptimizer.AnalyzeLargeFilesAsync(projectPath, maxLines, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalFilesAnalyzed"] = result.TotalFilesAnalyzed,
                    ["filesAboveThreshold"] = result.FilesAboveThreshold,
                    ["averageFileSize"] = result.AverageFileSize,
                    ["largestFileSize"] = result.LargestFileSize,
                    ["analysisTime"] = result.AnalysisDuration.TotalSeconds,
                    ["largeFiles"] = result.LargeFiles.Take(10).Select(f => new
                    {
                        filePath = f.FilePath,
                        lineCount = f.LineCount,
                        sizeCategory = f.SizeCategory.ToString(),
                        largeClassCount = f.LargeClasses.Count,
                        largeMethodCount = f.LargeMethods.Count,
                        codeSmellCount = f.CodeSmells.Count,
                        optimizationPriority = f.OptimizationPriority,
                        immediateActions = f.ImmediateActions.Take(3).ToList()
                    }).ToList(),
                    ["fileSizeDistribution"] = result.FileSizeDistribution,
                    ["statistics"] = new
                    {
                        filesNeedingOptimization = result.Statistics.FilesNeedingOptimization,
                        classesNeedingSplitting = result.Statistics.ClassesNeedingSplitting,
                        methodsNeedingRefactoring = result.Statistics.MethodsNeedingRefactoring,
                        totalCodeSmells = result.Statistics.TotalCodeSmells
                    }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing large files: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteOptimizeLargeClass(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var result = await _largeFileOptimizer.OptimizeLargeClassAsync(filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["className"] = result.ClassName,
                    ["filePath"] = result.FilePath,
                    ["priority"] = result.Priority.ToString(),
                    ["estimatedEffortHours"] = result.EstimatedEffortHours,
                    ["expectedBenefit"] = result.ExpectedBenefit,
                    ["currentMetrics"] = new
                    {
                        lineCount = result.CurrentMetrics.LineCount,
                        methodCount = result.CurrentMetrics.MethodCount,
                        propertyCount = result.CurrentMetrics.PropertyCount,
                        fieldCount = result.CurrentMetrics.FieldCount,
                        responsibilities = result.CurrentMetrics.Responsibilities.Count,
                        dependencies = result.CurrentMetrics.Dependencies.Count,
                        godClassScore = result.CurrentMetrics.GodClassScore.GodClassScore,
                        isTooLarge = result.CurrentMetrics.IsTooLarge
                    },
                    ["splittingStrategies"] = result.SplittingStrategies.Take(5).Select(s => new
                    {
                        strategyName = s.StrategyName,
                        description = s.Description,
                        newClassName = s.NewClassName,
                        splitType = s.SplitType.ToString(),
                        confidence = s.Confidence,
                        estimatedEffort = s.EstimatedEffortHours,
                        pros = s.Pros.Take(3).ToList(),
                        cons = s.Cons.Take(3).ToList()
                    }).ToList(),
                    ["recommendedActions"] = result.RecommendedActions.Take(5).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error optimizing large class: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteOptimizeLargeMethod(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        var methodName = GetStringArgument(arguments, "methodName");

        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(methodName))
        {
            return new ToolCallResult { Success = false, Error = "filePath and methodName parameters are required" };
        }

        try
        {
            var result = await _largeFileOptimizer.OptimizeLargeMethodAsync(filePath, methodName, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["methodName"] = result.MethodName,
                    ["className"] = result.ClassName,
                    ["filePath"] = result.FilePath,
                    ["priority"] = result.Priority.ToString(),
                    ["estimatedEffortHours"] = result.EstimatedEffortHours,
                    ["expectedBenefit"] = result.ExpectedBenefit,
                    ["currentMetrics"] = new
                    {
                        lineCount = result.CurrentMetrics.LineCount,
                        parameterCount = result.CurrentMetrics.ParameterCount,
                        localVariableCount = result.CurrentMetrics.LocalVariableCount,
                        loopCount = result.CurrentMetrics.LoopCount,
                        conditionalCount = result.CurrentMetrics.ConditionalCount,
                        cyclomaticComplexity = result.CurrentMetrics.Complexity.CyclomaticComplexity,
                        cognitiveComplexity = result.CurrentMetrics.Complexity.CognitiveComplexity,
                        isTooLarge = result.CurrentMetrics.IsTooLarge,
                        isTooComplex = result.CurrentMetrics.IsTooComplex
                    },
                    ["refactoringStrategies"] = result.RefactoringStrategies.Take(5).Select(s => new
                    {
                        strategyName = s.StrategyName,
                        description = s.Description,
                        refactoringType = s.RefactoringType.ToString(),
                        confidence = s.Confidence,
                        estimatedEffort = s.EstimatedEffortHours,
                        pros = s.Pros.Take(3).ToList(),
                        cons = s.Cons.Take(3).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error optimizing large method: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetComplexityMetrics(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        var methodName = GetStringArgument(arguments, "methodName");

        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var metrics = await _largeFileOptimizer.GetComplexityMetricsAsync(filePath, methodName, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["cyclomaticComplexity"] = metrics.CyclomaticComplexity,
                    ["cognitiveComplexity"] = metrics.CognitiveComplexity,
                    ["halsteadVolume"] = metrics.HalsteadVolume,
                    ["halsteadDifficulty"] = metrics.HalsteadDifficulty,
                    ["maintainabilityIndex"] = metrics.MaintainabilityIndex,
                    ["maximumNestingDepth"] = metrics.MaximumNestingDepth,
                    ["numberOfDecisionPoints"] = metrics.NumberOfDecisionPoints,
                    ["complexityLevel"] = metrics.ComplexityLevel.ToString(),
                    ["hotspots"] = metrics.Hotspots.Take(5).Select(h => new
                    {
                        startLine = h.StartLine,
                        endLine = h.EndLine,
                        hotspotType = h.HotspotType,
                        localComplexity = h.LocalComplexity,
                        description = h.Description,
                        suggestion = h.Suggestion
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting complexity metrics: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGenerateOptimizationPlan(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var plan = await _largeFileOptimizer.GenerateOptimizationPlanAsync(filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["filePath"] = plan.FilePath,
                    ["overallPriority"] = plan.OverallPriority.ToString(),
                    ["totalEstimatedEffortHours"] = plan.TotalEstimatedEffortHours,
                    ["totalExpectedBenefit"] = plan.TotalExpectedBenefit,
                    ["totalActions"] = plan.Actions.Count,
                    ["actions"] = plan.Actions.Take(10).Select(a => new
                    {
                        title = a.Title,
                        description = a.Description,
                        actionType = a.ActionType.ToString(),
                        priority = a.Priority,
                        estimatedEffort = a.EstimatedEffortHours,
                        expectedBenefit = a.ExpectedBenefit,
                        isRecommended = a.IsRecommended
                    }).ToList(),
                    ["prerequisites"] = plan.Prerequisites.Take(5).ToList(),
                    ["risks"] = plan.Risks.Take(5).ToList(),
                    ["recommendations"] = plan.Recommendations.Take(5).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error generating optimization plan: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectGodClasses(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var filePath = GetStringArgument(arguments, "filePath");

        try
        {
            var godClasses = await _largeFileOptimizer.DetectGodClassesAsync(projectPath, filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalGodClasses"] = godClasses.Count,
                    ["criticalGodClasses"] = godClasses.Count(gc => gc.Severity == GodClassSeverity.Critical),
                    ["highSeverityGodClasses"] = godClasses.Count(gc => gc.Severity == GodClassSeverity.High),
                    ["godClasses"] = godClasses.Take(10).Select(gc => new
                    {
                        className = gc.ClassName,
                        filePath = gc.FilePath,
                        godClassScore = gc.GodClassScore,
                        severity = gc.Severity.ToString(),
                        responsibilityCount = gc.Responsibilities.Count,
                        violations = gc.Violations.Take(3).ToList(),
                        recommendedSplits = gc.RecommendedSplits.Take(3).Select(s => new
                        {
                            strategyName = s.StrategyName,
                            newClassName = s.NewClassName,
                            confidence = s.Confidence
                        }).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error detecting god classes: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectGodMethods(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var filePath = GetStringArgument(arguments, "filePath");

        try
        {
            var godMethods = await _largeFileOptimizer.DetectGodMethodsAsync(projectPath, filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalGodMethods"] = godMethods.Count,
                    ["criticalGodMethods"] = godMethods.Count(gm => gm.Severity == GodMethodSeverity.Critical),
                    ["highSeverityGodMethods"] = godMethods.Count(gm => gm.Severity == GodMethodSeverity.High),
                    ["godMethods"] = godMethods.Take(10).Select(gm => new
                    {
                        methodName = gm.MethodName,
                        className = gm.ClassName,
                        filePath = gm.FilePath,
                        godMethodScore = gm.GodMethodScore,
                        severity = gm.Severity.ToString(),
                        violations = gm.Violations.Take(3).ToList(),
                        recommendedRefactorings = gm.RecommendedRefactorings.Take(3).Select(r => new
                        {
                            strategyName = r.StrategyName,
                            refactoringType = r.RefactoringType.ToString(),
                            confidence = r.Confidence
                        }).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error detecting god methods: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCodeSmells(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            var codeSmells = await _largeFileOptimizer.AnalyzeCodeSmellsAsync(filePath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalCodeSmells"] = codeSmells.Count,
                    ["blockerSmells"] = codeSmells.Count(cs => cs.Severity == CodeSmellSeverity.Blocker),
                    ["criticalSmells"] = codeSmells.Count(cs => cs.Severity == CodeSmellSeverity.Critical),
                    ["majorSmells"] = codeSmells.Count(cs => cs.Severity == CodeSmellSeverity.Major),
                    ["codeSmells"] = codeSmells.Take(20).Select(cs => new
                    {
                        smellType = cs.SmellType,
                        description = cs.Description,
                        severity = cs.Severity.ToString(),
                        startLine = cs.StartLine,
                        endLine = cs.EndLine,
                        impactScore = cs.ImpactScore,
                        suggestion = cs.Suggestion,
                        refactoringPatterns = cs.RefactoringPatterns.Take(2).Select(rp => new
                        {
                            patternName = rp.PatternName,
                            description = rp.Description,
                            applicability = rp.Applicability
                        }).ToList()
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing code smells: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetOptimizationRecommendations(JsonDocument arguments, CancellationToken ct)
    {
        if (_largeFileOptimizer == null)
        {
            return new ToolCallResult { Success = false, Error = "Large file optimizer service not available" };
        }

        var filePath = GetStringArgument(arguments, "filePath");
        if (string.IsNullOrEmpty(filePath))
        {
            return new ToolCallResult { Success = false, Error = "filePath parameter is required" };
        }

        try
        {
            // First analyze code smells
            var codeSmells = await _largeFileOptimizer.AnalyzeCodeSmellsAsync(filePath, ct);

            // Then suggest refactoring patterns
            var suggestions = await _largeFileOptimizer.SuggestRefactoringPatternsAsync(filePath, codeSmells, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalRecommendations"] = suggestions.Count,
                    ["highConfidenceRecommendations"] = suggestions.Count(s => s.Confidence > 0.8),
                    ["lowEffortRecommendations"] = suggestions.Count(s => s.EstimatedEffortHours <= 2),
                    ["recommendations"] = suggestions.Take(10).Select(s => new
                    {
                        title = s.Title,
                        description = s.Description,
                        refactoringType = s.Type.ToString(),
                        filePath = s.FilePath,
                        startLine = s.StartLine,
                        endLine = s.EndLine,
                        confidence = s.Confidence,
                        estimatedEffort = s.EstimatedEffortHours,
}

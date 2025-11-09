using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Architecture;

namespace MCPsharp.Services;

/// <summary>
/// Architecture validation tool execution methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    private async Task<ToolCallResult> ExecuteValidateArchitecture(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                // Parse architecture definition from JSON
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var result = await _architectureValidator.ValidateArchitectureAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["isValid"] = result.IsValid,
                    ["compliancePercentage"] = result.CompliancePercentage,
                    ["totalTypesAnalyzed"] = result.TotalTypesAnalyzed,
                    ["compliantTypes"] = result.CompliantTypes,
                    ["violations"] = result.Violations,
                    ["warnings"] = result.Warnings,
                    ["layerStatistics"] = result.LayerStatistics,
                    ["analysisDuration"] = result.AnalysisDuration.TotalMilliseconds,
                    ["analyzedFiles"] = result.AnalyzedFiles
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error validating architecture: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectLayerViolations(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var violations = await _architectureValidator.DetectLayerViolationsAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["violations"] = violations,
                    ["totalViolations"] = violations.Count,
                    ["criticalViolations"] = violations.Count(v => v.Severity == ViolationSeverity.Critical),
                    ["majorViolations"] = violations.Count(v => v.Severity == ViolationSeverity.Major),
                    ["minorViolations"] = violations.Count(v => v.Severity == ViolationSeverity.Minor)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error detecting layer violations: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeDependencies(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var analysis = await _architectureValidator.AnalyzeDependenciesAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["nodes"] = analysis.Nodes,
                    ["edges"] = analysis.Edges,
                    ["circularDependencies"] = analysis.CircularDependencies,
                    ["layerDependencies"] = analysis.LayerDependencies,
                    ["dependencyMetrics"] = analysis.DependencyMetrics,
                    ["analysisDuration"] = analysis.AnalysisDuration.TotalMilliseconds
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error analyzing dependencies: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetArchitectureReport(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var report = await _architectureValidator.GetArchitectureReportAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["summary"] = report.Summary,
                    ["validationResult"] = report.ValidationResult,
                    ["dependencyAnalysis"] = report.DependencyAnalysis,
                    ["recommendations"] = report.Recommendations,
                    ["architectureUsed"] = report.ArchitectureUsed,
                    ["generatedAt"] = report.GeneratedAt,
                    ["projectPath"] = report.ProjectPath
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error generating architecture report: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteDefineCustomArchitecture(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var definitionElement = arguments.RootElement.GetProperty("definition");
            var definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.GetRawText());

            if (definition == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid architecture definition provided"
                };
            }

            var result = await _architectureValidator.DefineCustomArchitectureAsync(definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["architecture"] = result
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error defining custom architecture: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCircularDependencies(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var circularDependencies = await _architectureValidator.AnalyzeCircularDependenciesAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["circularDependencies"] = circularDependencies,
                    ["totalCycles"] = circularDependencies.Count,
                    ["criticalCycles"] = circularDependencies.Count(c => c.Severity == ViolationSeverity.Critical),
                    ["majorCycles"] = circularDependencies.Count(c => c.Severity == ViolationSeverity.Major)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error analyzing circular dependencies: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGenerateArchitectureDiagram(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var projectPath = GetProjectPathFromArguments(arguments);
            var definitionElement = arguments.RootElement.GetPropertyOrNull("definition");
            MCPsharp.Models.Architecture.ArchitectureDefinition? definition = null;

            if (definitionElement?.ValueKind != JsonValueKind.Null)
            {
                definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.Value.GetRawText());
            }

            var diagram = await _architectureValidator.GenerateArchitectureDiagramAsync(projectPath, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["diagram"] = diagram
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error generating architecture diagram: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetArchitectureRecommendations(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var violationsElement = arguments.RootElement.GetProperty("violations");
            var violations = JsonSerializer.Deserialize<List<LayerViolation>>(violationsElement.GetRawText());

            if (violations == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Invalid violations list provided"
                };
            }

            var recommendations = await _architectureValidator.GetRecommendationsAsync(violations, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["recommendations"] = recommendations,
                    ["totalRecommendations"] = recommendations.Count,
                    ["highPriorityRecommendations"] = recommendations.Count(r => r.Priority == RecommendationPriority.High || r.Priority == RecommendationPriority.Critical),
                    ["effortScore"] = recommendations.Sum(r => r.Effort),
                    ["impactScore"] = recommendations.Sum(r => r.Impact)
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting architecture recommendations: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteCheckTypeCompliance(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var typeName = arguments.RootElement.GetProperty("typeName").GetString();
            var definitionElement = arguments.RootElement.GetProperty("definition");
            var definition = JsonSerializer.Deserialize<MCPsharp.Models.Architecture.ArchitectureDefinition>(definitionElement.GetRawText());

            if (string.IsNullOrEmpty(typeName) || definition == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Both typeName and definition are required"
                };
            }

            var compliance = await _architectureValidator.CheckTypeComplianceAsync(typeName, definition, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["typeName"] = compliance.TypeName,
                    ["layer"] = compliance.Layer,
                    ["isCompliant"] = compliance.IsCompliant,
                    ["violations"] = compliance.Violations,
                    ["warnings"] = compliance.Warnings,
                    ["dependencies"] = compliance.Dependencies
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error checking type compliance: {ex.Message}"
            };
        }
    }

    private async Task<ToolCallResult> ExecuteGetPredefinedArchitectures(JsonDocument arguments, CancellationToken ct)
    {
        await EnsureWorkspaceInitializedAsync();

        if (_architectureValidator == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Architecture validation service is not available"
            };
        }

        try
        {
            var architectures = await _architectureValidator.GetPredefinedArchitecturesAsync(ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["architectures"] = architectures,
                    ["totalArchitectures"] = architectures.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Error getting predefined architectures: {ex.Message}"
            };
        }
    }
}

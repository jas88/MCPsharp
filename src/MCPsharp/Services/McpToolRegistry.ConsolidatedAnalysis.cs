using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services;

/// <summary>
/// Handlers for the unified analysis tools (analyze_symbol, analyze_type, analyze_file,
/// analyze_project, analyze_architecture, analyze_dependencies, analyze_quality).
/// </summary>
public partial class McpToolRegistry
{
    private const string UnifiedAnalysisServiceName = "Unified analysis service";

    /// <summary>
    /// Common precondition for the analysis tools: the service exists and a project is open.
    /// </summary>
    private ToolCallResult? CheckUnifiedAnalysisAvailable()
    {
        if (_unifiedAnalysis == null)
        {
            return ServiceUnavailable(UnifiedAnalysisServiceName);
        }

        return _projectContext.GetProjectContext()?.RootPath == null ? ToolError(NoProjectOpenError) : null;
    }

    private async Task<ToolCallResult> ExecuteAnalyzeSymbol(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        var symbolName = GetStringArgument(arguments, "symbol_name");
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            return ToolError("Missing required parameter 'symbol_name'");
        }

        if (!TryGetEnumArgument(arguments, "scope", AnalysisScope.All, out var scope, out var scopeError))
        {
            return scopeError!;
        }

        await EnsureWorkspaceInitializedAsync();
        var response = await _unifiedAnalysis!.AnalyzeSymbolAsync(new SymbolAnalysisRequest
        {
            SymbolName = symbolName,
            Context = GetStringArgument(arguments, "context"),
            Scope = scope,
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteAnalyzeType(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        var typeName = GetStringArgument(arguments, "type_name");
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return ToolError("Missing required parameter 'type_name'");
        }

        if (!TryGetEnumArgument(arguments, "scope", AnalysisScope.All, out var scope, out var scopeError))
        {
            return scopeError!;
        }

        await EnsureWorkspaceInitializedAsync();
        var response = await _unifiedAnalysis!.AnalyzeTypeAsync(new TypeAnalysisRequest
        {
            TypeName = typeName,
            Context = GetStringArgument(arguments, "context"),
            Scope = scope,
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteAnalyzeFile(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        var pathError = ResolveRequiredProjectPath(arguments, "file_path", out var fullPath);
        if (pathError != null)
        {
            return pathError;
        }

        var analysisTypes = GetEnumListArgument<FileAnalysisType>(arguments, "analysis_types");
        var response = await _unifiedAnalysis!.AnalyzeFileAsync(new FileAnalysisRequest
        {
            FilePath = fullPath,
            AnalysisTypes = analysisTypes.Count > 0 ? analysisTypes : new List<FileAnalysisType> { FileAnalysisType.All },
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteAnalyzeProject(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        var projectPath = _projectContext.GetProjectContext()!.RootPath!;
        if (GetStringArgument(arguments, "project_path") != null)
        {
            var pathError = ResolveRequiredProjectPath(arguments, "project_path", out projectPath);
            if (pathError != null)
            {
                return pathError;
            }
        }

        var analysisTypes = GetEnumListArgument<ProjectAnalysisType>(arguments, "analysis_types");
        var response = await _unifiedAnalysis!.AnalyzeProjectAsync(new ProjectAnalysisRequest
        {
            ProjectPath = projectPath,
            AnalysisTypes = analysisTypes.Count > 0 ? analysisTypes : new List<ProjectAnalysisType> { ProjectAnalysisType.All },
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteAnalyzeArchitecture(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        if (!TryGetEnumArgument(arguments, "scope", AnalysisScope.All, out var scope, out var scopeError))
        {
            return scopeError!;
        }

        await EnsureWorkspaceInitializedAsync();
        var response = await _unifiedAnalysis!.AnalyzeArchitectureAsync(new ArchitectureAnalysisRequest
        {
            Scope = scope,
            ProjectPath = _projectContext.GetProjectContext()!.RootPath,
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteAnalyzeDependenciesUnified(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        if (!TryGetEnumArgument(arguments, "scope", AnalysisScope.Dependencies, out var scope, out var scopeError))
        {
            return scopeError!;
        }

        await EnsureWorkspaceInitializedAsync();
        var response = await _unifiedAnalysis!.AnalyzeDependenciesAsync(new DependencyAnalysisRequest
        {
            Scope = scope,
            Depth = GetIntArgument(arguments, "depth") ?? 2,
            IncludeCircular = GetBoolArgument(arguments, "include_circular") ?? true,
            TypeName = GetStringArgument(arguments, "type_name"),
            ProjectPath = _projectContext.GetProjectContext()!.RootPath,
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteAnalyzeQuality(JsonDocument arguments, CancellationToken ct)
    {
        var unavailable = CheckUnifiedAnalysisAvailable();
        if (unavailable != null)
        {
            return unavailable;
        }

        if (!TryGetEnumArgument(arguments, "scope", AnalysisScope.All, out var scope, out var scopeError))
        {
            return scopeError!;
        }

        string? filePath = null;
        if (GetStringArgument(arguments, "file_path") != null)
        {
            var pathError = ResolveRequiredProjectPath(arguments, "file_path", out var resolved);
            if (pathError != null)
            {
                return pathError;
            }

            filePath = resolved;
        }

        var metrics = GetEnumListArgument<AnalysisQualityMetric>(arguments, "metrics");
        await EnsureWorkspaceInitializedAsync();
        var response = await _unifiedAnalysis!.AnalyzeQualityAsync(new QualityAnalysisRequest
        {
            Scope = scope,
            Metrics = metrics.Count > 0 ? metrics : new List<AnalysisQualityMetric> { AnalysisQualityMetric.All },
            FilePath = filePath,
            ProjectPath = _projectContext.GetProjectContext()!.RootPath,
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }
}

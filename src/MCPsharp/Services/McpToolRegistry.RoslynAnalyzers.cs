using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Models.Refactoring;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services;

/// <summary>
/// Roslyn analyzer loading/running tools and the extract_method refactoring tool.
/// </summary>
public partial class McpToolRegistry
{
    private const string RoslynAnalyzerServiceName = "Roslyn analyzer service";

    /// <summary>
    /// Register the Roslyn analyzer tools when an analyzer service was supplied.
    /// </summary>
    private void RegisterRoslynAnalyzerTools()
    {
        if (_roslynAnalyzerService == null)
        {
            return;
        }

        _tools.Add(new McpTool
        {
            Name = "load_roslyn_analyzers",
            Description = "Load Roslyn analyzers from an analyzer assembly (.dll) or from every assembly in a directory.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("path", "string", "Path to an analyzer assembly or a directory containing analyzer assemblies", required: true))
        });

        _tools.Add(new McpTool
        {
            Name = "run_roslyn_analyzers",
            Description = "Run loaded Roslyn analyzers (auto-loading defaults if none are loaded) against a file or directory and report issues.",
            InputSchema = JsonSchemaHelper.CreateSchema(
                Prop("target_path", "string", "File or directory to analyze (relative to the open project root, or absolute)", required: true),
                Prop("analyzer_ids", "array", "Specific analyzer IDs to run (default: all loaded analyzers)", items: StringItems))
        });

        _tools.Add(new McpTool
        {
            Name = "list_roslyn_analyzers",
            Description = "List the loaded Roslyn analyzers and their rules.",
            InputSchema = JsonSchemaHelper.CreateSchema()
        });
    }

    private static object DescribeAnalyzer(IAnalyzer analyzer, bool includeRules) => new
    {
        id = analyzer.Id,
        name = analyzer.Name,
        description = analyzer.Description,
        version = analyzer.Version.ToString(),
        author = analyzer.Author,
        supportedExtensions = analyzer.SupportedExtensions.ToArray(),
        isEnabled = analyzer.IsEnabled,
        rules = includeRules
            ? analyzer.GetRules().Select(r => new
            {
                id = r.Id,
                title = r.Title,
                category = r.Category.ToString(),
                severity = r.DefaultSeverity.ToString()
            }).ToList()
            : null
    };

    /// <summary>
    /// Resolve a path argument relative to the open project (if any); absolute paths are used as given.
    /// Analyzer assemblies typically live outside the project (e.g. the NuGet cache), so no containment is enforced.
    /// </summary>
    private string ResolveAgainstProject(string path)
    {
        var root = _projectContext.GetProjectContext()?.RootPath;
        return Path.GetFullPath(Path.IsPathRooted(path) || root == null ? path : Path.Combine(root, path));
    }

    private async Task<ToolCallResult> ExecuteLoadRoslynAnalyzers(JsonDocument arguments, CancellationToken ct)
    {
        if (_roslynAnalyzerService == null)
        {
            return ServiceUnavailable(RoslynAnalyzerServiceName);
        }

        var rawPath = GetStringArgument(arguments, "path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return ToolError("Missing required parameter 'path'");
        }

        var path = ResolveAgainstProject(rawPath);
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            return ToolError($"Path does not exist: {rawPath}");
        }

        var analyzers = isDirectory
            ? await _roslynAnalyzerService.LoadAnalyzersFromDirectoryAsync(path, ct)
            : await _roslynAnalyzerService.LoadAnalyzersAsync(path, ct);

        return new ToolCallResult
        {
            Success = true,
            Result = new
            {
                analyzersLoaded = analyzers.Length,
                analyzers = analyzers.Select(a => DescribeAnalyzer(a, includeRules: false)).ToList()
            }
        };
    }

    private async Task<ToolCallResult> ExecuteRunRoslynAnalyzers(JsonDocument arguments, CancellationToken ct)
    {
        if (_roslynAnalyzerService == null)
        {
            return ServiceUnavailable(RoslynAnalyzerServiceName);
        }

        var rawPath = GetStringArgument(arguments, "target_path");
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return ToolError("Missing required parameter 'target_path'");
        }

        var targetPath = ResolveAgainstProject(rawPath);
        if (!Directory.Exists(targetPath) && !File.Exists(targetPath))
        {
            return ToolError($"Target path does not exist: {rawPath}");
        }

        var analyzerIds = GetStringListArgument(arguments, "analyzer_ids");
        var result = await _roslynAnalyzerService.RunAnalyzersAsync(
            targetPath, analyzerIds is { Count: > 0 } ? analyzerIds : null, ct);

        if (!result.Success)
        {
            return ToolError(result.ErrorMessage ?? "Roslyn analyzer run failed");
        }

        // Cache the most recent successful run so generate_analysis_report can use it
        _lastAnalysisResult = result;

        return new ToolCallResult
        {
            Success = true,
            Result = new
            {
                analyzersRun = result.AnalyzersRun.ToArray(),
                totalIssuesFound = result.TotalIssuesFound,
                durationSeconds = (result.EndTime - result.StartTime).TotalSeconds,
                issues = result.Results.SelectMany(r => r.Issues).Select(i => new
                {
                    ruleId = i.RuleId,
                    analyzerId = i.AnalyzerId,
                    severity = i.Severity.ToString(),
                    message = i.Title,
                    filePath = i.FilePath,
                    line = i.LineNumber,
                    column = i.ColumnNumber
                }).ToList()
            }
        };
    }

    private ToolCallResult ExecuteListRoslynAnalyzers()
    {
        if (_roslynAnalyzerService == null)
        {
            return ServiceUnavailable(RoslynAnalyzerServiceName);
        }

        var analyzers = _roslynAnalyzerService.GetLoadedAnalyzers();
        return new ToolCallResult
        {
            Success = true,
            Result = new
            {
                totalAnalyzers = analyzers.Length,
                analyzers = analyzers.Select(a => DescribeAnalyzer(a, includeRules: true)).ToList()
            }
        };
    }

    // ===== Refactoring =====

    private async Task<ToolCallResult> ExecuteExtractMethod(JsonDocument arguments, CancellationToken ct)
    {
        if (_workspace == null)
        {
            return ServiceUnavailable("Roslyn workspace");
        }

        var pathError = ResolveRequiredProjectPath(arguments, "file_path", out var filePath);
        if (pathError != null)
        {
            return pathError;
        }

        var startLine = GetIntArgument(arguments, "start_line");
        var endLine = GetIntArgument(arguments, "end_line");
        if (startLine == null || endLine == null)
        {
            return ToolError("Missing required parameters 'start_line' and 'end_line'");
        }

        await EnsureWorkspaceInitializedAsync();

        var service = new ExtractMethodService(
            _workspace,
            _loggerFactory?.CreateLogger<ExtractMethodService>() ?? NullLogger<ExtractMethodService>.Instance);

        var result = await service.ExtractMethodAsync(new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = startLine.Value,
            EndLine = endLine.Value,
            MethodName = GetStringArgument(arguments, "method_name"),
            Accessibility = GetStringArgument(arguments, "accessibility") ?? "private",
            Preview = GetBoolArgument(arguments, "preview") ?? true
        }, ct);

        return result.Success
            ? new ToolCallResult { Success = true, Result = result }
            : ToolError(result.Error?.Message ?? "Method extraction failed");
    }
}

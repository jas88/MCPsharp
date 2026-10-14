using System.Collections.Immutable;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

public static partial class AnalyzerTools
{
    private static Task<ToolCallResult> ExecuteListAnalyzers(JsonDocument arguments, IAnalyzerHost analyzerHost)
    {
        try
        {
            var includeBuiltin = arguments.RootElement.TryGetProperty("include_builtin", out var builtinProp) && builtinProp.GetBoolean();
            var includeExternal = arguments.RootElement.TryGetProperty("include_external", out var externalProp) && externalProp.GetBoolean();
            var category = arguments.RootElement.TryGetProperty("category", out var categoryProp) ? categoryProp.GetString() : null;
            var extension = arguments.RootElement.TryGetProperty("extension", out var extProp) ? extProp.GetString() : null;

            var allAnalyzers = analyzerHost.GetLoadedAnalyzers();
            var filteredAnalyzers = allAnalyzers.Where(a =>
            {
                if (!includeBuiltin && a.IsBuiltIn)
                    return false;
                if (!includeExternal && !a.IsBuiltIn)
                    return false;
                if (!string.IsNullOrEmpty(category) && !a.Rules.Any(r => r.Category.ToString() == category))
                    return false;
                if (!string.IsNullOrEmpty(extension) && !a.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    return false;
                return true;
            });

            var result = new
            {
                analyzers = filteredAnalyzers.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    description = a.Description,
                    version = a.Version.ToString(),
                    author = a.Author,
                    is_builtin = a.IsBuiltIn,
                    is_enabled = a.IsEnabled,
                    supported_extensions = a.SupportedExtensions,
                    rules = a.Rules.Select(r => new
                    {
                        id = r.Id,
                        title = r.Title,
                        description = r.Description,
                        category = r.Category.ToString(),
                        default_severity = r.DefaultSeverity.ToString(),
                        is_enabled_by_default = r.IsEnabledByDefault,
                        tags = r.Tags
                    })
                })
            };

            var toolResult = new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(result)
            };

            return Task.FromResult(toolResult);
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreateErrorResult(ex));
        }
    }

    private static async Task<ToolCallResult> ExecuteRunAnalysis(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzerId = arguments.RootElement.GetProperty("analyzer_id").GetString() ?? string.Empty;
            var filesElement = arguments.RootElement.GetProperty("files");
            var files = filesElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray();

            var rules = ImmutableArray<string>.Empty;
            if (arguments.RootElement.TryGetProperty("rules", out var rulesElement))
            {
                rules = rulesElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray();
            }

            AnalyzerConfiguration? configuration = null;
            if (arguments.RootElement.TryGetProperty("configuration", out var configElement))
            {
                configuration = JsonSerializer.Deserialize<AnalyzerConfiguration>(configElement.GetRawText());
            }

            var includeDisabledRules = arguments.RootElement.TryGetProperty("include_disabled_rules", out var disabledProp) && disabledProp.GetBoolean();
            var generateFixes = arguments.RootElement.TryGetProperty("generate_fixes", out var fixesProp) && fixesProp.GetBoolean();

            var request = new AnalysisRequest
            {
                AnalyzerId = analyzerId,
                Files = files,
                Rules = rules,
                Configuration = configuration,
                IncludeDisabledRules = includeDisabledRules,
                GenerateFixes = generateFixes
            };

            var result = await analyzerHost.RunAnalysisAsync(request, cancellationToken);

            var response = new
            {
                session_id = result.SessionId,
                analyzer_id = result.AnalyzerId,
                success = result.Success,
                error_message = result.ErrorMessage,
                start_time = result.StartTime,
                end_time = result.EndTime,
                statistics = result.Statistics,
                results = result.Results.Select(r => new
                {
                    file_path = r.FilePath,
                    success = r.Success,
                    error_message = r.ErrorMessage,
                    issues = r.Issues.Select(i => new
                    {
                        id = i.Id,
                        rule_id = i.RuleId,
                        title = i.Title,
                        description = i.Description,
                        file_path = i.FilePath,
                        line_number = i.LineNumber,
                        column_number = i.ColumnNumber,
                        end_line_number = i.EndLineNumber,
                        end_column_number = i.EndColumnNumber,
                        severity = i.Severity.ToString(),
                        confidence = i.Confidence.ToString(),
                        category = i.Category.ToString(),
                        help_link = i.HelpLink,
                        properties = i.Properties
                    })
                })
            };

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(response)
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult(ex);
        }
    }

    private static async Task<ToolCallResult> ExecuteLoadAnalyzer(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var assemblyPath = arguments.RootElement.GetProperty("assembly_path").GetString() ?? string.Empty;
            var autoEnable = !arguments.RootElement.TryGetProperty("auto_enable", out var enableProp) || enableProp.GetBoolean();

            var result = await analyzerHost.LoadAnalyzerAsync(assemblyPath, cancellationToken);

            if (result.Success && autoEnable)
            {
                await analyzerHost.SetAnalyzerEnabledAsync(result.AnalyzerId, true, cancellationToken);
            }

            var response = new
            {
                success = result.Success,
                analyzer_id = result.AnalyzerId,
                error_message = result.ErrorMessage,
                security_validation = result.SecurityValidation != null ? new
                {
                    is_valid = result.SecurityValidation.IsValid,
                    is_signed = result.SecurityValidation.IsSigned,
                    is_trusted = result.SecurityValidation.IsTrusted,
                    signer = result.SecurityValidation.Signer,
                    warnings = result.SecurityValidation.Warnings
                } : null,
                warnings = result.Warnings,
                loaded_at = result.LoadedAt
            };

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(response)
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult(ex);
        }
    }

    private static async Task<ToolCallResult> ExecuteUnloadAnalyzer(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzerId = arguments.RootElement.GetProperty("analyzer_id").GetString() ?? string.Empty;

            var success = await analyzerHost.UnloadAnalyzerAsync(analyzerId, cancellationToken);

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(new
                {
                    analyzer_id = analyzerId,
                    success = success,
                    unloaded_at = DateTime.UtcNow
                })
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult(ex);
        }
    }

    private static async Task<ToolCallResult> ExecuteConfigureAnalyzer(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzerId = arguments.RootElement.GetProperty("analyzer_id").GetString() ?? string.Empty;

            // Handle enabling/disabling
            if (arguments.RootElement.TryGetProperty("is_enabled", out var enabledProp))
            {
                await analyzerHost.SetAnalyzerEnabledAsync(analyzerId, enabledProp.GetBoolean(), cancellationToken);
            }

            // Handle configuration
            if (arguments.RootElement.TryGetProperty("configuration", out var configElement))
            {
                var configuration = JsonSerializer.Deserialize<AnalyzerConfiguration>(configElement.GetRawText());
                if (configuration != null)
                {
                    await analyzerHost.ConfigureAnalyzerAsync(analyzerId, configuration, cancellationToken);
                }
            }

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(new
                {
                    analyzer_id = analyzerId,
                    configured_at = DateTime.UtcNow
                })
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult(ex);
        }
    }

    private static async Task<ToolCallResult> ExecuteGetAnalyzerHealth(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzerId = arguments.RootElement.TryGetProperty("analyzer_id", out var idProp) ? idProp.GetString() : null;

            var healthStatuses = await analyzerHost.GetHealthStatusAsync(cancellationToken);
            var filteredStatuses = string.IsNullOrEmpty(analyzerId)
                ? healthStatuses
                : healthStatuses.Where(h => h.AnalyzerId == analyzerId).ToImmutableArray();

            var result = new
            {
                analyzers = filteredStatuses.Select(h => new
                {
                    analyzer_id = h.AnalyzerId,
                    is_healthy = h.IsHealthy,
                    is_loaded = h.IsLoaded,
                    is_enabled = h.IsEnabled,
                    last_activity = h.LastActivity,
                    uptime = h.Uptime.ToString(),
                    analyses_run = h.AnalysesRun,
                    errors_encountered = h.ErrorMessagesEncountered,
                    warnings = h.Warnings,
                    last_error = h.LastError,
                    metrics = h.Metrics
                })
            };

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            return CreateErrorResult(ex);
        }
    }
}

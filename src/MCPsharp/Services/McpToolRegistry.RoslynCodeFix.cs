using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// McpToolRegistry partial class for the Roslyn analyzer configuration and report tools
/// </summary>
public partial class McpToolRegistry
{
    private AnalyzerConfigurationManager? _analyzerConfigManager;
    private AnalysisReportGenerator? _reportGenerator;

    /// <summary>
    /// Execute configure_analyzer tool
    /// </summary>
    private async Task<ToolCallResult> ExecuteConfigureAnalyzer(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            // Initialize configuration manager if needed
            _analyzerConfigManager ??= new AnalyzerConfigurationManager();

            var root = arguments.RootElement;

            if (!root.TryGetProperty("analyzer_id", out var analyzerIdElement))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Missing required parameter 'analyzer_id'"
                };
            }

            var analyzerId = analyzerIdElement.GetString();
            if (string.IsNullOrWhiteSpace(analyzerId))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Parameter 'analyzer_id' cannot be empty"
                };
            }

            // Check if enabled state is being set
            if (root.TryGetProperty("enabled", out var enabledElement))
            {
                var enabled = enabledElement.GetBoolean();
                var success = await _analyzerConfigManager.SetAnalyzerEnabledAsync(analyzerId, enabled, ct);

                return new ToolCallResult
                {
                    Success = success,
                    Result = new
                    {
                        analyzerId,
                        enabled,
                        message = $"Analyzer {analyzerId} {(enabled ? "enabled" : "disabled")}"
                    },
                    Error = success ? null : $"Failed to set analyzer {analyzerId} enabled state"
                };
            }

            // Check if rule severity is being set
            if (root.TryGetProperty("rule_id", out var ruleIdElement) &&
                root.TryGetProperty("severity", out var severityElement))
            {
                var ruleId = ruleIdElement.GetString();
                var severityStr = severityElement.GetString();

                if (string.IsNullOrWhiteSpace(ruleId) || string.IsNullOrWhiteSpace(severityStr))
                {
                    return new ToolCallResult
                    {
                        Success = false,
                        Error = "Both 'rule_id' and 'severity' must be provided and non-empty"
                    };
                }

                if (!Enum.TryParse<IssueSeverity>(severityStr, true, out var severity))
                {
                    return new ToolCallResult
                    {
                        Success = false,
                        Error = $"Invalid severity value: {severityStr}. Valid values: Info, Warning, Error, Critical"
                    };
                }

                var success = await _analyzerConfigManager.SetRuleSeverityAsync(analyzerId, ruleId, severity, ct);

                return new ToolCallResult
                {
                    Success = success,
                    Result = new
                    {
                        analyzerId,
                        ruleId,
                        severity = severity.ToString(),
                        message = $"Rule {ruleId} severity set to {severity}"
                    },
                    Error = success ? null : $"Failed to set rule {ruleId} severity"
                };
            }

            return new ToolCallResult
            {
                Success = false,
                Error = "No valid configuration operation specified. Provide 'enabled' or 'rule_id'+'severity'"
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteConfigureAnalyzer));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to configure analyzer: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Execute get_analyzer_config tool
    /// </summary>
    private Task<ToolCallResult> ExecuteGetAnalyzerConfig()
    {
        try
        {
            // Initialize configuration manager if needed
            _analyzerConfigManager ??= new AnalyzerConfigurationManager();

            var config = _analyzerConfigManager.GetConfiguration();

            return Task.FromResult(new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    version = config.Version,
                    global = new
                    {
                        parallelExecution = config.Global.ParallelExecution,
                        maxFileSize = config.Global.MaxFileSize,
                        defaultSeverity = config.Global.DefaultSeverity.ToString(),
                        enabledByDefault = config.Global.EnabledByDefault
                    },
                    analyzers = config.Analyzers.Select(kvp => new
                    {
                        id = kvp.Key,
                        enabled = kvp.Value.Enabled,
                        ruleSeverityOverrides = kvp.Value.RuleSeverityOverrides
                            .Select(r => new { ruleId = r.Key, severity = r.Value.ToString() })
                            .ToList(),
                        properties = kvp.Value.Properties
                    }).ToList()
                }
            });
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetAnalyzerConfig));
            return Task.FromResult(new ToolCallResult
            {
                Success = false,
                Error = $"Failed to get analyzer configuration: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Execute reset_analyzer_config tool
    /// </summary>
    private async Task<ToolCallResult> ExecuteResetAnalyzerConfig(CancellationToken ct)
    {
        try
        {
            // Initialize configuration manager if needed
            _analyzerConfigManager ??= new AnalyzerConfigurationManager();

            var success = await _analyzerConfigManager.ResetToDefaultsAsync(ct);

            return new ToolCallResult
            {
                Success = success,
                Result = new
                {
                    message = "Analyzer configuration reset to defaults"
                },
                Error = success ? null : "Failed to reset analyzer configuration"
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteResetAnalyzerConfig));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to reset analyzer configuration: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Execute generate_analysis_report tool
    /// </summary>
    private async Task<ToolCallResult> ExecuteGenerateAnalysisReport(JsonDocument arguments, CancellationToken ct)
    {
        if (_roslynAnalyzerService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Roslyn Analyzer service is not available"
            };
        }

        try
        {
            // Initialize report generator if needed
            _reportGenerator ??= new AnalysisReportGenerator();

            var root = arguments.RootElement;

            // Get format
            if (!root.TryGetProperty("format", out var formatElement))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Missing required parameter 'format' (html, markdown, json, or csv)"
                };
            }

            var formatStr = formatElement.GetString();
            if (string.IsNullOrWhiteSpace(formatStr) ||
                !Enum.TryParse<ReportFormat>(formatStr, true, out var format))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Invalid format: {formatStr}. Valid values: html, markdown, json, csv"
                };
            }

            // Get output path
            if (!root.TryGetProperty("output_path", out var outputPathElement))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Missing required parameter 'output_path'"
                };
            }

            var outputPath = outputPathElement.GetString();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Parameter 'output_path' cannot be empty"
                };
            }

            // Get analysis results (either from recent run or run new analysis)
            var results = _lastAnalysisResult?.Results ?? System.Collections.Immutable.ImmutableArray<AnalysisResult>.Empty;

            if (results.IsEmpty)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "No analysis results available. Run analyzers first using 'run_roslyn_analyzers'"
                };
            }

            // Generate report
            var reportResult = await _reportGenerator.GenerateReportAsync(results, format, outputPath, null, ct);

            var statistics = reportResult.Statistics != null
                ? new
                {
                    totalFiles = reportResult.Statistics.TotalFiles,
                    totalIssues = reportResult.Statistics.TotalIssues,
                    errors = reportResult.Statistics.ErrorCount,
                    warnings = reportResult.Statistics.WarningCount,
                    info = reportResult.Statistics.InfoCount,
                    critical = reportResult.Statistics.CriticalCount
                }
                : null;

            return new ToolCallResult
            {
                Success = reportResult.Success,
                Result = reportResult.Success ? new
                {
                    filePath = reportResult.FilePath,
                    format = reportResult.Format.ToString(),
                    generatedAt = reportResult.GeneratedAt,
                    statistics = statistics
                } : null,
                Error = reportResult.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGenerateAnalysisReport));
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to generate analysis report: {ex.Message}"
            };
        }
    }

    // Most recent successful analyzer run, reused by generate_analysis_report
    private AnalyzerRunResult? _lastAnalysisResult;
}

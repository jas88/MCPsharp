using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// McpToolRegistry partial class for Roslyn Code Fix and Configuration tools
/// </summary>
public partial class McpToolRegistry
{
    private RoslynCodeFixLoader? _codeFixLoader;
    private readonly List<RoslynCodeFixAdapter> _loadedCodeFixProviders = new();
    private AnalyzerConfigurationManager? _analyzerConfigManager;
    private AnalysisReportGenerator? _reportGenerator;

    /// <summary>
    /// Execute apply_roslyn_fixes tool
    /// </summary>
    private async Task<ToolCallResult> ExecuteApplyRoslynFixes(JsonDocument arguments, CancellationToken ct)
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
            var root = arguments.RootElement;

            // Get diagnostic IDs to fix
            if (!root.TryGetProperty("diagnostic_ids", out var diagnosticIdsElement) ||
                diagnosticIdsElement.ValueKind != JsonValueKind.Array)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Missing required parameter 'diagnostic_ids' (array of strings)"
                };
            }

            var diagnosticIds = diagnosticIdsElement.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();

            if (diagnosticIds.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "No valid diagnostic IDs provided"
                };
            }

            // Get target path
            if (!root.TryGetProperty("target_path", out var targetPathElement))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Missing required parameter 'target_path'"
                };
            }

            var targetPath = targetPathElement.GetString();
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Parameter 'target_path' cannot be empty"
                };
            }

            if (!File.Exists(targetPath))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"File not found: {targetPath}"
                };
            }

            // Get preview mode (default true for safety)
            var preview = true;
            if (root.TryGetProperty("preview", out var previewElement))
            {
                preview = previewElement.GetBoolean();
            }

            // Initialize code fix loader if needed
            if (_codeFixLoader == null && _loggerFactory != null)
            {
                var logger = _loggerFactory.CreateLogger<RoslynCodeFixLoader>();
                _codeFixLoader = new RoslynCodeFixLoader(logger, _loggerFactory);
            }

            // Ensure we have code fix providers loaded
            if (_loadedCodeFixProviders.Count == 0 && _codeFixLoader != null)
            {
                // Try to load from the same assemblies as analyzers
                var analyzerAssemblies = _roslynAnalyzerService.GetLoadedAnalyzers()
                    .Select(a => a.GetType().Assembly.Location)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct()
                    .ToList();

                foreach (var assembly in analyzerAssemblies)
                {
                    var providers = await _codeFixLoader.LoadCodeFixProvidersFromAssemblyAsync(assembly, ct);
                    _loadedCodeFixProviders.AddRange(providers);
                }
            }

            // Find applicable code fix providers
            var applicableProviders = _loadedCodeFixProviders
                .Where(p => diagnosticIds.Any(id => p.CanFix(id)))
                .ToList();

            if (applicableProviders.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"No code fix providers found for diagnostic IDs: {string.Join(", ", diagnosticIds)}"
                };
            }

            // Read file content
            var fileContent = await File.ReadAllTextAsync(targetPath, ct);

            // Run analyzers to get the issues
            var analysisResult = await _roslynAnalyzerService.RunAnalyzersAsync(targetPath, null, ct);
            if (!analysisResult.Success)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Failed to analyze file: {analysisResult.ErrorMessage}"
                };
            }

            // Find issues matching the diagnostic IDs
            var issues = analysisResult.Results
                .SelectMany(r => r.Issues)
                .Where(i => diagnosticIds.Contains(i.RuleId))
                .ToList();

            if (issues.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        message = "No issues found matching the specified diagnostic IDs",
                        diagnosticIds = diagnosticIds,
                        filePath = targetPath
                    }
                };
            }

            // Get fixes for each issue
            var allFixes = new List<(AnalyzerIssue Issue, AnalyzerFix Fix, RoslynCodeFixAdapter Provider)>();
            foreach (var issue in issues)
            {
                foreach (var provider in applicableProviders.Where(p => p.CanFix(issue.RuleId)))
                {
                    var fixes = await provider.GetFixesAsync(issue, fileContent, ct);
                    foreach (AnalyzerFix fix in fixes)
                    {
                        allFixes.Add((Issue: issue, Fix: fix, Provider: provider));
                    }
                }
            }

            if (allFixes.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        message = "No fixes available for the found issues",
                        issuesFound = issues.Count,
                        filePath = targetPath
                    }
                };
            }

            if (preview)
            {
                // Return preview of fixes without applying
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        preview = true,
                        filePath = targetPath,
                        issuesFound = issues.Count,
                        fixesAvailable = allFixes.Count,
                        fixes = allFixes.Select(f => new
                        {
                            issueId = f.Issue.Id,
                            ruleId = f.Issue.RuleId,
                            fixTitle = f.Fix.Title,
                            fixDescription = f.Fix.Description,
                            provider = f.Provider.Name,
                            editsCount = f.Fix.Edits.Length,
                            affectedFiles = f.Fix.AffectedFiles.ToArray()
                        }).ToList()
                    }
                };
            }
            else
            {
                // Apply fixes
                var appliedFixes = new List<object>();
                var modifiedFiles = new HashSet<string>();

                foreach (var (issue, fix, provider) in allFixes)
                {
                    var applyResult = await provider.ApplyFixAsync(fix, fileContent, ct);
                    if (applyResult.Success)
                    {
                        appliedFixes.Add(new
                        {
                            issueId = issue.Id,
                            ruleId = issue.RuleId,
                            fixTitle = fix.Title,
                            success = true
                        });

                        foreach (var file in applyResult.ModifiedFiles)
                        {
                            modifiedFiles.Add(file);
                        }

                        // Update file content for next fix
                        if (applyResult.ModifiedFiles.Contains(targetPath))
                        {
                            fileContent = await File.ReadAllTextAsync(targetPath, ct);
                        }
                    }
                    else
                    {
                        appliedFixes.Add(new
                        {
                            issueId = issue.Id,
                            ruleId = issue.RuleId,
                            fixTitle = fix.Title,
                            success = false,
                            error = applyResult.ErrorMessage
                        });
                    }
                }

                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        preview = false,
                        filePath = targetPath,
                        issuesFound = issues.Count,
                        fixesApplied = appliedFixes.Count(f => f.GetType().GetProperty("success")?.GetValue(f) as bool? == true),
                        fixesFailed = appliedFixes.Count(f => f.GetType().GetProperty("success")?.GetValue(f) as bool? == false),
                        modifiedFiles = modifiedFiles.ToList(),
                        fixes = appliedFixes
                    }
                };
            }
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to apply Roslyn fixes: {ex.Message}"
            };
        }
    }

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
    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<ToolCallResult> ExecuteGetAnalyzerConfig(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            // Initialize configuration manager if needed
            _analyzerConfigManager ??= new AnalyzerConfigurationManager();

            var config = _analyzerConfigManager.GetConfiguration();

            return new ToolCallResult
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
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to get analyzer configuration: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Execute reset_analyzer_config tool
    /// </summary>
    private async Task<ToolCallResult> ExecuteResetAnalyzerConfig(JsonDocument arguments, CancellationToken ct)
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
            var results = analysisResult?.Results ?? System.Collections.Immutable.ImmutableArray<AnalysisResult>.Empty;

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

            return new ToolCallResult
            {
                Success = reportResult.Success,
                Result = reportResult.Success ? new
                {
                    filePath = reportResult.FilePath,
                    format = reportResult.Format.ToString(),
                    generatedAt = reportResult.GeneratedAt,
                    statistics = reportResult.Statistics != null ? new
                    {
                        totalFiles = reportResult.Statistics.TotalFiles,
                        totalIssues = reportResult.Statistics.TotalIssues,
                        errors = reportResult.Statistics.ErrorCount,
                        warnings = reportResult.Statistics.WarningCount,
                        info = reportResult.Statistics.InfoCount,
                        critical = reportResult.Statistics.CriticalCount
                    } : null
                } : null,
                Error = reportResult.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"Failed to generate analysis report: {ex.Message}"
            };
        }
    }

    // Field to store analysis result for caching (currently not populated; TODO: implement result caching)
    #pragma warning disable CS0649 // Field is never assigned - placeholder for future result caching implementation
    private AnalyzerRunResult? analysisResult;
    #pragma warning restore CS0649
}

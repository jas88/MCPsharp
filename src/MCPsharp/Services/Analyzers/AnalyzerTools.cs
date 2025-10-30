using System.Text.Json;
using System.Collections.Immutable;
using System.Linq;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// MCP tool implementations for analyzer functionality
/// </summary>
public static class AnalyzerTools
{
    /// <summary>
    /// Get all analyzer MCP tools
    /// </summary>
    public static List<McpTool> GetAnalyzerTools()
    {
        return new List<McpTool>
        {
            new McpTool
            {
                Name = "analyzer_list",
                Description = "List all available analyzers, with optional filtering",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.ListAnalyzersSchema)
            },
            new McpTool
            {
                Name = "analyzer_run",
                Description = "Run analysis on specified files using an analyzer",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.RunAnalysisSchema)
            },
            new McpTool
            {
                Name = "analyzer_get_fixes",
                Description = "Get available fixes for specific analyzer issues",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.GetFixesSchema)
            },
            new McpTool
            {
                Name = "analyzer_apply_fixes",
                Description = "Apply fixes for analyzer issues with conflict resolution and rollback",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.ApplyFixesSchema)
            },
            new McpTool
            {
                Name = "analyzer_load",
                Description = "Load an analyzer from an assembly file with security validation",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.LoadAnalyzerSchema)
            },
            new McpTool
            {
                Name = "analyzer_unload",
                Description = "Unload an analyzer and clean up resources",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.UnloadAnalyzerSchema)
            },
            new McpTool
            {
                Name = "analyzer_configure",
                Description = "Configure analyzer settings and rule preferences",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.ConfigureAnalyzerSchema)
            },
            new McpTool
            {
                Name = "analyzer_get_health",
                Description = "Get health status and metrics for analyzers",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.GetAnalyzerHealthSchema)
            },
            new McpTool
            {
                Name = "analyzer_get_fix_history",
                Description = "Get history of applied fixes with rollback information",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.GetFixHistorySchema)
            },
            new McpTool
            {
                Name = "analyzer_rollback_fixes",
                Description = "Rollback previously applied fixes using backups",
                InputSchema = JsonSerializer.SerializeToDocument(AnalyzerSchemas.RollbackFixesSchema)
            }
        };
    }

    /// <summary>
    /// Execute analyzer tool
    /// </summary>
    public static async Task<ToolCallResult> ExecuteAnalyzerTool(
        string toolName,
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        IFixEngine fixEngine,
        CancellationToken cancellationToken = default)
    {
        return toolName switch
        {
            "analyzer_list" => await ExecuteListAnalyzers(arguments, analyzerHost),
            "analyzer_run" => await ExecuteRunAnalysis(arguments, analyzerHost, cancellationToken),
            "analyzer_get_fixes" => await ExecuteGetFixes(arguments, analyzerHost, cancellationToken),
            "analyzer_apply_fixes" => await ExecuteApplyFixes(arguments, analyzerHost, cancellationToken),
            "analyzer_load" => await ExecuteLoadAnalyzer(arguments, analyzerHost, cancellationToken),
            "analyzer_unload" => await ExecuteUnloadAnalyzer(arguments, analyzerHost, cancellationToken),
            "analyzer_configure" => await ExecuteConfigureAnalyzer(arguments, analyzerHost, cancellationToken),
            "analyzer_get_health" => await ExecuteGetAnalyzerHealth(arguments, analyzerHost, cancellationToken),
            "analyzer_get_fix_history" => await ExecuteGetFixHistory(arguments, fixEngine, cancellationToken),
            "analyzer_rollback_fixes" => await ExecuteRollbackFixes(arguments, fixEngine, cancellationToken),
            _ => new ToolCallResult
            {
                Success = false,
                Error = $"Unknown analyzer tool: {toolName}"
            }
        };
    }

    private static async Task<ToolCallResult> ExecuteListAnalyzers(JsonDocument arguments, IAnalyzerHost analyzerHost)
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
                if (!includeBuiltin && a.IsBuiltIn) return false;
                if (!includeExternal && !a.IsBuiltIn) return false;
                if (!string.IsNullOrEmpty(category) && !a.Rules.Any(r => r.Category.ToString() == category)) return false;
                if (!string.IsNullOrEmpty(extension) && !a.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;
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

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(result)
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<ToolCallResult> ExecuteGetFixes(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzerId = arguments.RootElement.GetProperty("analyzer_id").GetString() ?? string.Empty;
            var issueIdsElement = arguments.RootElement.GetProperty("issue_ids");
            var issueIds = issueIdsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray();

            var fixes = await analyzerHost.GetFixesAsync(analyzerId, issueIds, cancellationToken);

            var result = new
            {
                analyzer_id = analyzerId,
                fixes = fixes.Select(f => new
                {
                    id = f.Id,
                    rule_id = f.RuleId,
                    title = f.Title,
                    description = f.Description,
                    confidence = f.Confidence.ToString(),
                    is_interactive = f.IsInteractive,
                    is_batchable = f.IsBatchable,
                    required_inputs = f.RequiredInputs,
                    affected_files = f.AffectedFiles,
                    edits = f.Edits.Select(e => new
                    {
                        file_path = e.FilePath,
                        start_line = e.StartLine,
                        start_column = e.StartColumn,
                        end_line = e.EndLine,
                        end_column = e.EndColumn,
                        new_text = e.NewText
                    })
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<ToolCallResult> ExecuteApplyFixes(
        JsonDocument arguments,
        IAnalyzerHost analyzerHost,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzerId = arguments.RootElement.GetProperty("analyzer_id").GetString() ?? string.Empty;
            var issueIdsElement = arguments.RootElement.GetProperty("issue_ids");
            var issueIds = issueIdsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray();

            var fixIds = ImmutableArray<string>.Empty;
            if (arguments.RootElement.TryGetProperty("fix_ids", out var fixIdsElement))
            {
                fixIds = fixIdsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToImmutableArray();
            }

            var previewOnly = arguments.RootElement.TryGetProperty("preview_only", out var previewProp) && previewProp.GetBoolean();
            var resolveConflicts = arguments.RootElement.TryGetProperty("resolve_conflicts", out var resolveProp) && resolveProp.GetBoolean();
            var conflictStrategy = arguments.RootElement.TryGetProperty("conflict_strategy", out var strategyProp)
                ? Enum.Parse<ConflictResolutionStrategy>(strategyProp.GetString()!)
                : ConflictResolutionStrategy.PreferNewer;

            var inputs = new Dictionary<string, object>();
            if (arguments.RootElement.TryGetProperty("inputs", out var inputsElement))
            {
                inputs = JsonSerializer.Deserialize<Dictionary<string, object>>(inputsElement.GetRawText()) ?? new Dictionary<string, object>();
            }

            var request = new ApplyFixRequest
            {
                AnalyzerId = analyzerId,
                IssueIds = issueIds,
                FixIds = fixIds,
                PreviewOnly = previewOnly,
                ResolveConflicts = resolveConflicts,
                Inputs = inputs
            };

            var result = await analyzerHost.ApplyFixesAsync(request, cancellationToken);

            var response = new
            {
                session_id = result.SessionId,
                analyzer_id = result.AnalyzerId,
                success = result.Success,
                error_message = result.ErrorMessage,
                start_time = result.StartTime,
                end_time = result.EndTime,
                modified_files = result.ModifiedFiles,
                conflicts = result.Conflicts,
                statistics = result.Statistics,
                results = result.Results.Select(r => new
                {
                    id = r.Id,
                    fix_id = r.FixId,
                    success = r.Success,
                    error_message = r.ErrorMessage,
                    modified_files = r.ModifiedFiles,
                    conflicts = r.Conflicts,
                    applied_at = r.AppliedAt,
                    metrics = r.Metrics
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
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
            var force = arguments.RootElement.TryGetProperty("force", out var forceProp) && forceProp.GetBoolean();

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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<ToolCallResult> ExecuteGetFixHistory(
        JsonDocument arguments,
        IFixEngine fixEngine,
        CancellationToken cancellationToken)
    {
        try
        {
            var maxSessions = arguments.RootElement.TryGetProperty("max_sessions", out var maxProp) ? maxProp.GetInt32() : 50;

            var sessions = await fixEngine.GetFixHistoryAsync(maxSessions, cancellationToken);

            var result = new
            {
                sessions = sessions.Select(s => new
                {
                    session_id = s.SessionId,
                    analyzer_id = s.AnalyzerId,
                    start_time = s.StartTime,
                    end_time = s.EndTime,
                    success = s.Success,
                    modified_files = s.ModifiedFiles,
                    applied_fixes = s.AppliedFixes.Length,
                    metadata = s.Metadata
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
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private static async Task<ToolCallResult> ExecuteRollbackFixes(
        JsonDocument arguments,
        IFixEngine fixEngine,
        CancellationToken cancellationToken)
    {
        try
        {
            var sessionId = arguments.RootElement.GetProperty("session_id").GetString() ?? string.Empty;
            var force = arguments.RootElement.TryGetProperty("force", out var forceProp) && forceProp.GetBoolean();

            var result = await fixEngine.RollbackFixesAsync(sessionId, cancellationToken);

            var response = new
            {
                session_id = result.SessionId,
                success = result.Success,
                error_message = result.ErrorMessage,
                restored_files = result.RestoredFiles,
                failed_files = result.FailedFiles,
                rolled_back_at = result.RolledBackAt,
                duration = result.Duration.ToString()
            };

            return new ToolCallResult
            {
                Success = true,
                Result = JsonSerializer.Serialize(response)
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}
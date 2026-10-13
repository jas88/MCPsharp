using System.Text.Json;
using System.Collections.Immutable;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

public static partial class AnalyzerTools
{
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
            return CreateErrorResult(ex);
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
            return CreateErrorResult(ex);
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
            return CreateErrorResult(ex);
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

            var result = await fixEngine.RollbackFixesAsync(sessionId, cancellationToken);

            var response = new
            {
                session_id = result.SessionId,
                success = result.Success,
                error_message = result.ErrorMessage,
                total_files = result.TotalFiles,
                successful_rollbacks = result.SuccessfulRollbacks,
                failed_rollbacks = result.FailedRollbacks,
                failed_files = result.FailedFiles,
                file_results = result.FileResults.Select(fr => new
                {
                    file_path = fr.FilePath,
                    success = fr.Success,
                    error_message = fr.ErrorMessage,
                    operation_type = fr.OperationType.ToString()
                }).ToArray(),
                rolled_back_at = result.RolledBackAt,
                duration = result.ElapsedTime.ToString(),
                cleanup_completed = result.CleanupCompleted,
                summary = result.Summary,
                warnings = result.Warnings
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
}

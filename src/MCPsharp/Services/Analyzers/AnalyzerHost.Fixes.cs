using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

public partial class AnalyzerHost
{
    public Task<ImmutableArray<AnalyzerFix>> GetFixesAsync(string analyzerId, ImmutableArray<string> issueIds, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return Task.FromResult(ImmutableArray<AnalyzerFix>.Empty);
            }

            var fixes = new List<AnalyzerFix>();

            foreach (var issueId in issueIds)
            {
                // This is a simplified approach - in practice, we'd need to map issue IDs to rule IDs
                // For now, return all available fixes
                var analyzerFixes = analyzer.GetFixes("ALL");
                fixes.AddRange(analyzerFixes);
            }

            return Task.FromResult(fixes.DistinctBy(f => f.Id).ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorGettingFixes(_logger, ex, analyzerId);
            return Task.FromResult(ImmutableArray<AnalyzerFix>.Empty);
        }
    }

    public async Task<FixSessionResult> ApplyFixesAsync(ApplyFixRequest request, CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            LogStartingFixSession(_logger, sessionId, request.AnalyzerId);

            var analyzer = _registry.GetAnalyzer(request.AnalyzerId);
            if (analyzer == null)
            {
                return new FixSessionResult
                {
                    SessionId = sessionId,
                    AnalyzerId = request.AnalyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = $"Analyzer not found: {request.AnalyzerId}"
                };
            }

            // Get fixes for issues
            var fixes = await GetFixesAsync(request.AnalyzerId, request.IssueIds, cancellationToken);
            var selectedFixes = fixes.Where(f => request.FixIds.Contains(f.Id) || request.FixIds.IsEmpty).ToImmutableArray();

            if (!selectedFixes.Any())
            {
                return new FixSessionResult
                {
                    SessionId = sessionId,
                    AnalyzerId = request.AnalyzerId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    Success = false,
                    ErrorMessage = "No fixes found for the specified issues"
                };
            }

            // Apply fixes using the fix engine
            var fixOptions = new FixApplicationOptions
            {
                CreateBackup = true,
                ResolveConflicts = request.ResolveConflicts,
                PreviewOnly = request.PreviewOnly
            };

            var issues = request.IssueIds.Select(id => new AnalyzerIssue
            {
                Id = id,
                RuleId = "UNKNOWN", // Would need to resolve this properly
                AnalyzerId = request.AnalyzerId,
                FilePath = "UNKNOWN" // Would need to resolve this properly
            }).ToImmutableArray();

            var applicationResult = await _fixEngine.ApplyFixesAsync(issues, selectedFixes, fixOptions, cancellationToken);

            return new FixSessionResult
            {
                SessionId = sessionId,
                AnalyzerId = request.AnalyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = applicationResult.Success,
                Results = applicationResult.AppliedFixes.Select(f => new FixResult
                {
                    Id = f.Id,
                    FixId = f.FixId,
                    AnalyzerId = f.AnalyzerId,
                    Success = f.Success,
                    ErrorMessage = f.ErrorMessage,
                    ModifiedFiles = f.ModifiedFiles,
                    Conflicts = f.Conflicts,
                    AppliedAt = f.AppliedAt,
                    Metrics = f.Metrics
                }).ToImmutableArray(),
                ModifiedFiles = applicationResult.ModifiedFiles,
                Conflicts = applicationResult.ResolvedConflicts.SelectMany(c => c.ConflictingFixIds).ToImmutableArray(),
                Statistics = new Dictionary<string, object>
                {
                    ["FixesApplied"] = applicationResult.AppliedFixes.Count(f => f.Success),
                    ["FilesModified"] = applicationResult.ModifiedFiles.Length,
                    ["ConflictsResolved"] = applicationResult.ResolvedConflicts.Length
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            LogErrorInFixSession(_logger, ex, sessionId);
            return new FixSessionResult
            {
                SessionId = sessionId,
                AnalyzerId = request.AnalyzerId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

using System.Collections.Concurrent;
using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers.Fixes;

/// <summary>
/// Implementation of fix engine for applying analyzer fixes with preview, conflict resolution, and rollback
/// </summary>
public partial class FixEngine : IFixEngine
{
    private readonly ILogger<FixEngine> _logger;
    private readonly FileOperationsService _fileOperations;
    private readonly ConcurrentDictionary<string, FixSession> _sessions;
    private readonly string _backupDirectory;

    public FixEngine(
        ILogger<FixEngine> logger,
        FileOperationsService fileOperations)
    {
        _logger = logger;
        _fileOperations = fileOperations;
        _sessions = new ConcurrentDictionary<string, FixSession>();
        _backupDirectory = Path.Combine(Path.GetTempPath(), "MCPsharp", "FixBackups");

        Directory.CreateDirectory(_backupDirectory);
    }

    public async Task<FixPreviewResult> PreviewFixesAsync(
        ImmutableArray<AnalyzerIssue> issues,
        ImmutableArray<AnalyzerFix> fixes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogPreviewingFixes(_logger, fixes.Length, issues.Length);

            var previews = new List<FixPreview>();
            var fileContents = new Dictionary<string, string>();

            // Load file contents
            foreach (var filePath in issues.Select(i => i.FilePath).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    fileContents[filePath] = content;
                }
                catch (Exception ex)
                {
                    LogErrorReadingFileForPreview(_logger, ex, filePath);
                    return new FixPreviewResult
                    {
                        Success = false,
                        ErrorMessage = $"Error reading file: {filePath}",
                        Warnings = ImmutableArray.Create($"Could not read {filePath} for preview")
                    };
                }
            }

            // Generate previews for each fix
            foreach (var fix in fixes)
            {
                try
                {
                    var preview = GeneratePreview(fix, issues, fileContents);
                    previews.Add(preview);
                }
                catch (Exception ex)
                {
                    LogErrorGeneratingPreview(_logger, ex, fix.Id);
                }
            }

            // Detect conflicts
            var conflicts = DetectFixConflicts(previews, cancellationToken);

            return new FixPreviewResult
            {
                Success = true,
                Previews = previews.ToImmutableArray(),
                Conflicts = conflicts.ToImmutableArray(),
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogErrorPreviewingFixes(_logger, ex);
            return new FixPreviewResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<FixApplicationResult> ApplyFixesAsync(
        ImmutableArray<AnalyzerIssue> issues,
        ImmutableArray<AnalyzerFix> fixes,
        FixApplicationOptions options,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        try
        {
            LogApplyingFixes(_logger, fixes.Length, sessionId);

            // Generate preview first
            var previewResult = await PreviewFixesAsync(issues, fixes, cancellationToken);
            if (!previewResult.Success)
            {
                return new FixApplicationResult
                {
                    SessionId = sessionId,
                    Success = false,
                    ErrorMessage = $"Preview failed: {previewResult.ErrorMessage}",
                    AppliedAt = DateTime.UtcNow,
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Detect and resolve conflicts
            var conflicts = previewResult.Conflicts;
            if (options.ResolveConflicts && !conflicts.IsEmpty)
            {
                var resolutionResult = await ResolveConflictsAsync(conflicts, options.ConflictStrategy, cancellationToken);
                if (!resolutionResult.Success)
                {
                    return new FixApplicationResult
                    {
                        SessionId = sessionId,
                        Success = false,
                        ErrorMessage = $"Conflict resolution failed: {resolutionResult.ErrorMessage}",
                        AppliedAt = DateTime.UtcNow,
                        Duration = DateTime.UtcNow - startTime
                    };
                }
                conflicts = resolutionResult.Resolutions.SelectMany(r => conflicts.Where(c => c.Id == r.ConflictId)).ToImmutableArray();
            }

            // Create backups if requested
            var backupPaths = new Dictionary<string, string>();
            if (options.CreateBackup)
            {
                backupPaths = await CreateBackupsAsync(previewResult.Previews.Select(p => p.FilePath).Distinct(), cancellationToken);
            }

            // Apply fixes
            var appliedFixes = new List<FixResult>();
            var modifiedFiles = new HashSet<string>();
            var failedFiles = new HashSet<string>();
            var warnings = new List<string>();

            foreach (var fix in fixes)
            {
                try
                {
                    var result = await ApplySingleFixAsync(fix, cancellationToken);
                    appliedFixes.Add(result);

                    if (result.Success)
                    {
                        modifiedFiles.UnionWith(result.ModifiedFiles);
                    }
                    else
                    {
                        failedFiles.UnionWith(result.ModifiedFiles);
                        if (options.StopOnFirstError)
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogErrorApplyingFix(_logger, ex, fix.Id);
                    failedFiles.Add(fix.Edits.FirstOrDefault()?.FilePath ?? "Unknown");
                    warnings.Add($"Failed to apply fix {fix.Id}: {ex.Message}");

                    if (options.StopOnFirstError)
                    {
                        break;
                    }
                }
            }

            // Validate fixes if requested
            var validationWarnings = new List<string>();
            if (options.ValidateAfterApply)
            {
                var validationResult = await ValidateFixesAsync(sessionId, cancellationToken);
                if (!validationResult.IsValid)
                {
                    validationWarnings.AddRange(validationResult.Errors.Select(e => $"Validation error in {e.FilePath}: {e.Description}"));
                }
                validationWarnings.AddRange(validationResult.Warnings.Select(w => $"Validation warning in {w.FilePath}: {w.Description}"));
            }

            warnings.AddRange(validationWarnings);

            // Create session record
            var session = new FixSession
            {
                SessionId = sessionId,
                AnalyzerId = fixes.FirstOrDefault()?.AnalyzerId ?? "Unknown",
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = failedFiles.Count == 0,
                ModifiedFiles = modifiedFiles.ToImmutableArray(),
                AppliedFixes = appliedFixes.ToImmutableArray(),
                Metadata = ImmutableDictionary<string, object>.Empty
                    .Add("BackupPaths", backupPaths)
                    .Add("ConflictsResolved", conflicts.Length)
                    .Add("ValidationWarnings", validationWarnings.Count)
            };

            _sessions[sessionId] = session;

            return new FixApplicationResult
            {
                SessionId = sessionId,
                Success = failedFiles.Count == 0,
                AppliedFixes = appliedFixes.ToImmutableArray(),
                ModifiedFiles = modifiedFiles.ToImmutableArray(),
                ResolvedConflicts = conflicts,
                FailedFiles = failedFiles.ToImmutableArray(),
                Warnings = warnings.ToImmutableArray(),
                AppliedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            LogErrorApplyingFixesInSession(_logger, ex, sessionId);
            return new FixApplicationResult
            {
                SessionId = sessionId,
                Success = false,
                ErrorMessage = ex.Message,
                AppliedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }
}

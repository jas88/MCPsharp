using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers.Fixes;

/// <summary>
/// Implementation of fix engine for applying analyzer fixes with preview, conflict resolution, and rollback
/// </summary>
public class FixEngine : IFixEngine
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
            _logger.LogInformation("Previewing {FixCount} fixes for {IssueCount} issues", fixes.Length, issues.Length);

            var previews = new List<FixPreview>();
            var conflicts = new List<FixConflict>();
            var fileContents = new Dictionary<string, string>();

            // Load file contents
            foreach (var issue in issues)
            {
                if (!fileContents.ContainsKey(issue.FilePath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(issue.FilePath, cancellationToken);
                        fileContents[issue.FilePath] = content;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading file for preview: {FilePath}", issue.FilePath);
                        return new FixPreviewResult
                        {
                            Success = false,
                            ErrorMessage = $"Error reading file: {issue.FilePath}",
                            Warnings = ImmutableArray.Create($"Could not read {issue.FilePath} for preview")
                        };
                    }
                }
            }

            // Generate previews for each fix
            foreach (var fix in fixes)
            {
                try
                {
                    var preview = await GeneratePreviewAsync(fix, issues, fileContents, cancellationToken);
                    previews.Add(preview);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating preview for fix: {FixId}", fix.Id);
                }
            }

            // Detect conflicts
            var conflictEnumerable = DetectFixConflictsAsync(previews, cancellationToken);
            conflicts = new List<FixConflict>();
            await foreach (var conflict in conflictEnumerable.WithCancellation(cancellationToken))
            {
                conflicts.Add(conflict);
            }

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
            _logger.LogError(ex, "Error previewing fixes");
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
            _logger.LogInformation("Applying {FixCount} fixes in session {SessionId}", fixes.Length, sessionId);

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
            if (options.ResolveConflicts && conflicts.Any())
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
                    var result = await ApplySingleFixAsync(fix, issues, options, cancellationToken);
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
                    _logger.LogError(ex, "Error applying fix: {FixId}", fix.Id);
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
                Success = !failedFiles.Any(),
                ModifiedFiles = modifiedFiles.ToImmutableArray(),
                AppliedFixes = appliedFixes.ToImmutableArray(),
                Metadata = new Dictionary<string, object>
                {
                    ["BackupPaths"] = backupPaths,
                    ["ConflictsResolved"] = conflicts.Count,
                    ["ValidationWarnings"] = validationWarnings.Count
                }.ToImmutableDictionary()
            };

            _sessions[sessionId] = session;

            return new FixApplicationResult
            {
                SessionId = sessionId,
                Success = !failedFiles.Any(),
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
            _logger.LogError(ex, "Error applying fixes in session {SessionId}", sessionId);
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

    public async Task<ConflictDetectionResult> DetectConflictsAsync(ImmutableArray<AnalyzerFix> fixes, CancellationToken cancellationToken = default)
    {
        try
        {
            var conflicts = new List<FixConflict>();

            // Group fixes by file
            var fixesByFile = fixes.GroupBy(f => f.Edits.FirstOrDefault()?.FilePath ?? string.Empty)
                                  .Where(g => !string.IsNullOrEmpty(g.Key))
                                  .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var (filePath, fileFixes) in fixesByFile)
            {
                var fileConflicts = await DetectFileConflictsAsync(filePath, fileFixes, cancellationToken);
                conflicts.AddRange(fileConflicts);
            }

            return new ConflictDetectionResult
            {
                Success = true,
                Conflicts = conflicts.ToImmutableArray(),
                DetectedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting conflicts");
            return new ConflictDetectionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DetectedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<ConflictResolutionResult> ResolveConflictsAsync(
        ImmutableArray<FixConflict> conflicts,
        ConflictResolutionStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var resolutions = new List<ConflictResolution>();
            var unresolvedConflicts = new List<FixConflict>();

            foreach (var conflict in conflicts)
            {
                try
                {
                    var resolution = await ResolveSingleConflictAsync(conflict, strategy, cancellationToken);
                    if (resolution != null)
                    {
                        resolutions.Add(resolution);
                    }
                    else
                    {
                        unresolvedConflicts.Add(conflict);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error resolving conflict: {ConflictId}", conflict.Id);
                    unresolvedConflicts.Add(conflict);
                }
            }

            return new ConflictResolutionResult
            {
                Success = !unresolvedConflicts.Any(),
                Resolutions = resolutions.ToImmutableArray(),
                UnresolvedConflicts = unresolvedConflicts.ToImmutableArray(),
                ResolvedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving conflicts");
            return new ConflictResolutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ResolvedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<RollbackResult> RollbackFixesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return new RollbackResult
                {
                    Success = false,
                    ErrorMessage = $"Session {sessionId} not found",
                    SessionId = sessionId,
                    RolledBackAt = DateTime.UtcNow
                };
            }

            _logger.LogInformation("Rolling back fixes for session {SessionId}", sessionId);

            var restoredFiles = new List<string>();
            var failedFiles = new List<string>();

            // Restore from backups
            if (session.Metadata.TryGetValue("BackupPaths", out var backupPathsObj) &&
                backupPathsObj is Dictionary<string, string> backupPaths)
            {
                foreach (var (originalPath, backupPath) in backupPaths)
                {
                    try
                    {
                        if (File.Exists(backupPath))
                        {
                            await File.CopyAsync(backupPath, originalPath, true, cancellationToken);
                            restoredFiles.Add(originalPath);
                            File.Delete(backupPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error restoring file: {FilePath}", originalPath);
                        failedFiles.Add(originalPath);
                    }
                }
            }

            // Remove session
            _sessions.TryRemove(sessionId, out _);

            return new RollbackResult
            {
                Success = !failedFiles.Any(),
                SessionId = sessionId,
                RestoredFiles = restoredFiles.ToImmutableArray(),
                FailedFiles = failedFiles.ToImmutableArray(),
                RolledBackAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - session.StartTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back session {SessionId}", sessionId);
            return new RollbackResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                SessionId = sessionId,
                RolledBackAt = DateTime.UtcNow
            };
        }
    }

    public async Task<Models.Analyzers.ValidationResult> ValidateFixesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return new Models.Analyzers.ValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Session {sessionId} not found",
                    SessionId = sessionId
                };
            }

            var errors = new List<ValidationError>();
            var warnings = new List<ValidationWarning>();

            // Validate each modified file
            foreach (var filePath in session.ModifiedFiles)
            {
                try
                {
                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);

                    // Basic syntax validation (for C# files)
                    if (Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        var syntaxErrors = await ValidateCSharpSyntaxAsync(content);
                        errors.AddRange(syntaxErrors);
                    }

                    // Check for obvious issues
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        warnings.Add(new ValidationWarning
                        {
                            FilePath = filePath,
                            Description = "File is empty after applying fixes",
                            FixId = "Validation"
                        });
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError
                    {
                        FilePath = filePath,
                        Description = $"Error reading file for validation: {ex.Message}",
                        FixId = "Validation",
                        Severity = Models.Analyzers.ValidationSeverity.Error
                    });
                }
            }

            return new Models.Analyzers.ValidationResult
            {
                IsValid = !errors.Any(e => e.Severity >= Models.Analyzers.ValidationSeverity.Error),
                SessionId = sessionId,
                Errors = errors.ToImmutableArray(),
                Warnings = warnings.ToImmutableArray(),
                ValidatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating fixes for session {SessionId}", sessionId);
            return new Models.Analyzers.ValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                SessionId = sessionId,
                ValidatedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<ImmutableArray<FixSession>> GetFixHistoryAsync(int maxSessions = 50, CancellationToken cancellationToken = default)
    {
        return _sessions.Values
            .OrderByDescending(s => s.StartTime)
            .Take(maxSessions)
            .ToImmutableArray();
    }

    public async Task<FixStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = _sessions.Values.ToList();
        var now = DateTime.UtcNow;
        var periodStart = now.AddDays(-30); // Last 30 days

        var recentSessions = sessions.Where(s => s.StartTime >= periodStart).ToList();

        return new FixStatistics
        {
            TotalSessions = sessions.Count,
            SuccessfulSessions = sessions.Count(s => s.Success),
            FailedSessions = sessions.Count(s => !s.Success),
            TotalFixes = sessions.Sum(s => s.AppliedFixes.Length),
            SuccessfulFixes = sessions.Sum(s => s.AppliedFixes.Count(f => f.Success)),
            FailedFixes = sessions.Sum(s => s.AppliedFixes.Count(f => !f.Success)),
            ConflictsDetected = 0, // Would need to track this
            ConflictsResolved = 0, // Would need to track this
            MostFixedFiles = recentSessions
                .SelectMany(s => s.ModifiedFiles)
                .GroupBy(f => f)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => g.Key)
                .ToImmutableArray(),
            MostActiveAnalyzers = recentSessions
                .GroupBy(s => s.AnalyzerId)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Key)
                .ToImmutableArray(),
            PeriodStart = periodStart,
            PeriodEnd = now
        };
    }

    private async Task<FixPreview> GeneratePreviewAsync(
        AnalyzerFix fix,
        ImmutableArray<AnalyzerIssue> issues,
        Dictionary<string, string> fileContents,
        CancellationToken cancellationToken)
    {
        var issue = issues.FirstOrDefault(i => fix.RuleId == i.RuleId);
        var filePath = fix.Edits.FirstOrDefault()?.FilePath ?? issue?.FilePath ?? string.Empty;

        if (!fileContents.TryGetValue(filePath, out var originalContent))
        {
            throw new InvalidOperationException($"File content not available for preview: {filePath}");
        }

        var modifiedContent = originalContent;

        // Apply edits to generate preview
        foreach (var edit in fix.Edits.OrderByDescending(e => e.StartLine))
        {
            modifiedContent = ApplyTextEdit(modifiedContent, edit);
        }

        return new FixPreview
        {
            FixId = fix.Id,
            IssueId = issue?.Id ?? string.Empty,
            FilePath = filePath,
            OriginalContent = originalContent,
            ModifiedContent = modifiedContent,
            Edits = fix.Edits,
            Description = fix.Description,
            Confidence = fix.Confidence
        };
    }

    private async Task<IAsyncEnumerable<FixConflict>> DetectFixConflictsAsync(
        List<FixPreview> previews,
        CancellationToken cancellationToken)
    {
        var conflicts = new List<FixConflict>();

        // Group previews by file
        var previewsByFile = previews.GroupBy(p => p.FilePath).ToList();

        foreach (var fileGroup in previewsByFile)
        {
            var fileConflicts = await DetectFileConflictsAsync(fileGroup.Key, fileGroup.ToList(), cancellationToken);
            conflicts.AddRange(fileConflicts);
        }

        return conflicts.ToAsyncEnumerable();
    }

    private async Task<List<FixConflict>> DetectFileConflictsAsync(
        string filePath,
        List<FixPreview> previews,
        CancellationToken cancellationToken)
    {
        var conflicts = new List<FixConflict>();

        // Check for overlapping edits
        var allEdits = previews.SelectMany(p => p.Edits.Select(e => (Edit: e, FixId: p.FixId))).ToList();

        for (int i = 0; i < allEdits.Count; i++)
        {
            for (int j = i + 1; j < allEdits.Count; j++)
            {
                var edit1 = allEdits[i];
                var edit2 = allEdits[j];

                if (EditsOverlap(edit1.Edit, edit2.Edit))
                {
                    conflicts.Add(new FixConflict
                    {
                        FilePath = filePath,
                        ConflictingFixIds = ImmutableArray.Create(edit1.FixId, edit2.FixId),
                        ConflictType = ConflictType.OverlappingEdits,
                        Description = $"Edits from fixes {edit1.FixId} and {edit2.FixId} overlap in {filePath}",
                        ConflictingEdits = ImmutableArray.Create(edit1.Edit, edit2.Edit)
                    });
                }
            }
        }

        return conflicts;
    }

    private bool EditsOverlap(TextEdit edit1, TextEdit edit2)
    {
        var pos1 = (edit1.StartLine, edit1.StartColumn);
        var pos2 = (edit2.StartLine, edit2.StartColumn);
        var end1 = (edit1.EndLine, edit1.EndColumn);
        var end2 = (edit2.EndLine, edit2.EndColumn);

        return !(end1 < pos2 || end2 < pos1);
    }

    private async Task<ConflictResolution?> ResolveSingleConflictAsync(
        FixConflict conflict,
        ConflictResolutionStrategy strategy,
        CancellationToken cancellationToken)
    {
        return strategy switch
        {
            ConflictResolutionStrategy.PreferOlder => new ConflictResolution
            {
                ConflictId = conflict.Id,
                Strategy = strategy,
                PreferredFixId = conflict.ConflictingFixIds.First(),
                Rationale = "Preferred older fix"
            },
            ConflictResolutionStrategy.PreferNewer => new ConflictResolution
            {
                ConflictId = conflict.Id,
                Strategy = strategy,
                PreferredFixId = conflict.ConflictingFixIds.Last(),
                Rationale = "Preferred newer fix"
            },
            ConflictResolutionStrategy.SkipAll => new ConflictResolution
            {
                ConflictId = conflict.Id,
                Strategy = strategy,
                Rationale = "Skipping all conflicting fixes"
            },
            _ => null
        };
    }

    private async Task<Dictionary<string, string>> CreateBackupsAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken)
    {
        var backupPaths = new Dictionary<string, string>();

        foreach (var filePath in filePaths)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var backupPath = Path.Combine(_backupDirectory, $"{Guid.NewGuid()}{Path.GetExtension(filePath)}");
                    await using var sourceStream = File.OpenRead(filePath);
                    await using var destinationStream = File.Create(backupPath);
                    await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                    backupPaths[filePath] = backupPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating backup for file: {FilePath}", filePath);
            }
        }

        return backupPaths;
    }

    private async Task<FixResult> ApplySingleFixAsync(
        AnalyzerFix fix,
        ImmutableArray<AnalyzerIssue> issues,
        FixApplicationOptions options,
        CancellationToken cancellationToken)
    {
        var modifiedFiles = new List<string>();

        foreach (var edit in fix.Edits)
        {
            try
            {
                var result = await _fileOperations.EditFileAsync(new FileEditRequest
                {
                    FilePath = edit.FilePath,
                    Edits = new[] { new Models.TextEdit
                    {
                        StartLine = edit.StartLine,
                        StartColumn = edit.StartColumn,
                        EndLine = edit.EndLine,
                        EndColumn = edit.EndColumn,
                        NewText = edit.NewText
                    }},
                    CreateBackup = false // We handle backup at session level
                }, cancellationToken);

                if (result.Success)
                {
                    modifiedFiles.Add(edit.FilePath);
                }
                else
                {
                    return new FixResult
                    {
                        Success = false,
                        FixId = fix.Id,
                        AnalyzerId = fix.AnalyzerId,
                        ErrorMessage = result.ErrorMessage,
                        ModifiedFiles = modifiedFiles.ToImmutableArray()
                    };
                }
            }
            catch (Exception ex)
            {
                return new FixResult
                {
                    Success = false,
                    FixId = fix.Id,
                    AnalyzerId = fix.AnalyzerId,
                    ErrorMessage = ex.Message,
                    ModifiedFiles = modifiedFiles.ToImmutableArray()
                };
            }
        }

        return new FixResult
        {
            Success = true,
            FixId = fix.Id,
            AnalyzerId = fix.AnalyzerId,
            ModifiedFiles = modifiedFiles.ToImmutableArray(),
            AppliedAt = DateTime.UtcNow
        };
    }

    private string ApplyTextEdit(string content, TextEdit edit)
    {
        var lines = content.Split('\n');

        if (edit.StartLine >= lines.Length || edit.EndLine >= lines.Length)
            return content;

        var startLine = edit.StartLine;
        var endLine = edit.EndLine;

        if (startLine == endLine)
        {
            var line = lines[startLine];
            lines[startLine] = line.Substring(0, edit.StartColumn) +
                              edit.NewText +
                              line.Substring(Math.Min(edit.EndColumn, line.Length));
        }
        else
        {
            lines[startLine] = lines[startLine].Substring(0, edit.StartColumn) + edit.NewText;
            var newLines = new List<string>(lines.Take(startLine + 1));
            if (endLine + 1 < lines.Length)
            {
                newLines.Add(lines[endLine].Substring(Math.Min(edit.EndColumn, lines[endLine].Length)));
                newLines.AddRange(lines.Skip(endLine + 1));
            }
            lines = newLines.ToArray();
        }

        return string.Join('\n', lines);
    }

    private async Task<List<ValidationError>> ValidateCSharpSyntaxAsync(string content)
    {
        var errors = new List<ValidationError>();

        // Basic syntax validation - check for common issues
        if (content.Count(c => c == '{') != content.Count(c => c == '}'))
        {
            errors.Add(new ValidationError
            {
                FilePath = "unknown",
                Description = "Mismatched braces detected",
                FixId = "Validation",
                Severity = Models.Analyzers.ValidationSeverity.Error
            });
        }

        if (content.Count(c => c == '(') != content.Count(c => c == ')'))
        {
            errors.Add(new ValidationError
            {
                FilePath = "unknown",
                Description = "Mismatched parentheses detected",
                FixId = "Validation",
                Severity = Models.Analyzers.ValidationSeverity.Error
            });
        }

        return errors;
    }
}
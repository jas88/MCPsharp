using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Models.BulkEdit;

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

    public Task<ConflictDetectionResult> DetectConflictsAsync(ImmutableArray<AnalyzerFix> fixes, CancellationToken cancellationToken = default)
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
                cancellationToken.ThrowIfCancellationRequested();

                // Convert AnalyzerFix objects to FixPreview objects
                var fixPreviews = fileFixes.Select(ConvertToFixPreview).ToList();
                var fileConflicts = DetectFileConflicts(filePath, fixPreviews);
                conflicts.AddRange(fileConflicts);
            }

            return Task.FromResult(new ConflictDetectionResult
            {
                Success = true,
                Conflicts = conflicts.ToImmutableArray(),
                DetectedAt = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled<ConflictDetectionResult>(cancellationToken);
        }
        catch (Exception ex)
        {
            LogErrorDetectingConflicts(_logger, ex);
            return Task.FromResult(new ConflictDetectionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                DetectedAt = DateTime.UtcNow
            });
        }
    }

    public Task<ConflictResolutionResult> ResolveConflictsAsync(
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
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var resolution = ResolveSingleConflict(conflict, strategy);
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
                    LogErrorResolvingConflict(_logger, ex, conflict.Id);
                    unresolvedConflicts.Add(conflict);
                }
            }

            return Task.FromResult(new ConflictResolutionResult
            {
                Success = unresolvedConflicts.Count == 0,
                Resolutions = resolutions.ToImmutableArray(),
                UnresolvedConflicts = unresolvedConflicts.ToImmutableArray(),
                ResolvedAt = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            return Task.FromCanceled<ConflictResolutionResult>(cancellationToken);
        }
        catch (Exception ex)
        {
            LogErrorResolvingConflicts(_logger, ex);
            return Task.FromResult(new ConflictResolutionResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                ResolvedAt = DateTime.UtcNow
            });
        }
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public async Task<Models.Analyzers.RollbackResult> RollbackFixesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return new Models.Analyzers.RollbackResult
                {
                    Success = false,
                    SessionId = sessionId,
                    ElapsedTime = TimeSpan.Zero,
                    Summary = $"Session {sessionId} not found"
                };
            }

            LogRollingBackFixes(_logger, sessionId);

            var successfulRollbacks = 0;
            var failedRollbacks = 0;
            var fileResults = new List<MCPsharp.Models.BulkEdit.FileRollbackResult>();
            var errors = new List<MCPsharp.Models.BulkEdit.RollbackError>();

            // Restore from backups
            if (session.Metadata.TryGetValue("BackupPaths", out var backupPathsObj) &&
                backupPathsObj is Dictionary<string, string> backupPaths)
            {
                foreach (var (originalPath, backupPath) in backupPaths)
                {
                    var startTime = DateTime.UtcNow;
                    try
                    {
                        if (File.Exists(backupPath))
                        {
                            File.Copy(backupPath, originalPath, true);
                            File.Delete(backupPath);

                            successfulRollbacks++;
                            fileResults.Add(new Models.BulkEdit.FileRollbackResult
                            {
                                FilePath = originalPath,
                                Success = true,
                                OperationType = Models.BulkEdit.RollbackOperationType.RestoredFromBackup,
                                ProcessingTime = DateTime.UtcNow - startTime
                            });
                        }
                        else
                        {
                            failedRollbacks++;
                            errors.Add(new MCPsharp.Models.BulkEdit.RollbackError
                            {
                                FilePath = originalPath,
                                ErrorMessage = "Backup file not found",
                                IsRecoverable = false,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        failedRollbacks++;
                        LogErrorRestoringFile(_logger, ex, originalPath);
                        errors.Add(new MCPsharp.Models.BulkEdit.RollbackError
                        {
                            FilePath = originalPath,
                            ErrorMessage = ex.Message,
                            ExceptionDetails = ex.StackTrace,
                            IsRecoverable = true,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }

            // Remove session
            _sessions.TryRemove(sessionId, out _);

            return new Models.Analyzers.RollbackResult
            {
                Success = errors.Count == 0,
                SessionId = sessionId,
                TotalFiles = successfulRollbacks + failedRollbacks,
                SuccessfulRollbacks = successfulRollbacks,
                FailedRollbacks = failedRollbacks,
                FileResults = fileResults.Select(r => ConvertToFileRollbackResult(r)).ToImmutableArray(),
                Errors = errors.Select(e => ConvertToRollbackError(e)).ToImmutableArray(),
                ElapsedTime = DateTime.UtcNow - session.StartTime,
                CleanupCompleted = true,
                Summary = $"Successfully rolled back {successfulRollbacks} files, {failedRollbacks} failed"
            };
        }
        catch (Exception ex)
        {
            LogErrorRollingBackSession(_logger, ex, sessionId);
            return new Models.Analyzers.RollbackResult
            {
                Success = false,
                SessionId = sessionId,
                TotalFiles = 0,
                SuccessfulRollbacks = 0,
                FailedRollbacks = 1,
                FileResults = ImmutableArray<Models.Analyzers.FileRollbackResult>.Empty,
                Errors = ImmutableArray.Create(new Models.Analyzers.RollbackError
                {
                    FilePath = "Session",
                    ErrorMessage = ex.Message,
                    ExceptionDetails = ex.StackTrace,
                    IsRecoverable = false,
                    Timestamp = DateTime.UtcNow
                }),
                ElapsedTime = TimeSpan.Zero,
                CleanupCompleted = false,
                Summary = $"Rollback failed: {ex.Message}"
            };
        }
    }
    #pragma warning restore CS1998

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
                        var syntaxErrors = ValidateCSharpSyntax(content);
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
                    LogErrorReadingFileForValidation(_logger, ex, filePath);
                    errors.Add(new ValidationError
                    {
                        FilePath = filePath,
                        Description = $"Error reading file for validation: {ex.Message}",
                        FixId = "Validation",
                        Severity = Models.ValidationSeverity.Error
                    });
                }
            }

            return new Models.Analyzers.ValidationResult
            {
                IsValid = !errors.Any(e => e.Severity >= Models.ValidationSeverity.Error),
                SessionId = sessionId,
                Errors = errors.ToImmutableArray(),
                Warnings = warnings.ToImmutableArray(),
                ValidatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogErrorValidatingFixes(_logger, ex, sessionId);
            return new Models.Analyzers.ValidationResult
            {
                IsValid = false,
                ErrorMessage = ex.Message,
                SessionId = sessionId,
                ValidatedAt = DateTime.UtcNow
            };
        }
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public async Task<ImmutableArray<FixSession>> GetFixHistoryAsync(int maxSessions = 50, CancellationToken cancellationToken = default)
    {
        return _sessions.Values
            .OrderByDescending(s => s.StartTime)
            .Take(maxSessions)
            .ToImmutableArray();
    }
    #pragma warning restore CS1998

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
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
    #pragma warning restore CS1998

    private static FixPreview GeneratePreview(
        AnalyzerFix fix,
        ImmutableArray<AnalyzerIssue> issues,
        Dictionary<string, string> fileContents)
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

    private static List<FixConflict> DetectFixConflicts(
        List<FixPreview> previews,
        CancellationToken cancellationToken)
    {
        var conflicts = new List<FixConflict>();

        // Group previews by file
        var previewsByFile = previews.GroupBy(p => p.FilePath).ToList();

        foreach (var fileGroup in previewsByFile)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileConflicts = DetectFileConflicts(fileGroup.Key, fileGroup.ToList());
            conflicts.AddRange(fileConflicts);
        }

        return conflicts;
    }

    private static List<FixConflict> DetectFileConflicts(
        string filePath,
        List<FixPreview> previews)
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

    private static bool EditsOverlap(TextEdit edit1, TextEdit edit2)
    {
        var pos1 = (edit1.StartLine, edit1.StartColumn);
        var pos2 = (edit2.StartLine, edit2.StartColumn);
        var end1 = (edit1.EndLine, edit1.EndColumn);
        var end2 = (edit2.EndLine, edit2.EndColumn);

        return !(end1.Item1 < pos2.Item1 || (end1.Item1 == pos2.Item1 && end1.Item2 < pos2.Item2) ||
                 end2.Item1 < pos1.Item1 || (end2.Item1 == pos1.Item1 && end2.Item2 < pos1.Item2));
    }

    private static ConflictResolution? ResolveSingleConflict(
        FixConflict conflict,
        ConflictResolutionStrategy strategy)
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
                LogErrorCreatingBackup(_logger, ex, filePath);
            }
        }

        return backupPaths;
    }

    private async Task<FixResult> ApplySingleFixAsync(
        AnalyzerFix fix,
        CancellationToken cancellationToken)
    {
        var modifiedFiles = new List<string>();

        foreach (var edit in fix.Edits)
        {
            try
            {
                var editFilePath = edit.FilePath ?? string.Empty;
                var edits = new[] { new ReplaceEdit
                {
                    StartLine = edit.StartLine,
                    StartColumn = edit.StartColumn,
                    EndLine = edit.EndLine,
                    EndColumn = edit.EndColumn,
                    NewText = edit.NewText
                }};
                var result = await _fileOperations.EditFileAsync(editFilePath, edits, cancellationToken);

                if (result.Success)
                {
                    modifiedFiles.Add(editFilePath);
                }
                else
                {
                    return new FixResult
                    {
                        Success = false,
                        FixId = fix.Id,
                        AnalyzerId = fix.AnalyzerId,
                        ErrorMessage = result.Error,
                        ModifiedFiles = modifiedFiles.ToImmutableArray()
                    };
                }
            }
            catch (Exception ex)
            {
                LogErrorApplyingEdit(_logger, ex, fix.Id, edit.FilePath);
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

    private static string ApplyTextEdit(string content, TextEdit edit)
    {
        var lines = content.Split('\n');

        if (edit.StartLine >= lines.Length || edit.EndLine >= lines.Length)
            return content;

        var startLine = edit.StartLine;
        var endLine = edit.EndLine;

        if (startLine == endLine)
        {
            var line = lines[startLine];
            lines[startLine] = string.Concat(
                line.AsSpan(0, edit.StartColumn),
                edit.NewText,
                line.AsSpan(Math.Min(edit.EndColumn, line.Length)));
        }
        else
        {
            lines[startLine] = string.Concat(lines[startLine].AsSpan(0, edit.StartColumn), edit.NewText);
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

    private static List<ValidationError> ValidateCSharpSyntax(string content)
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
                Severity = Models.ValidationSeverity.Error
            });
        }

        if (content.Count(c => c == '(') != content.Count(c => c == ')'))
        {
            errors.Add(new ValidationError
            {
                FilePath = "unknown",
                Description = "Mismatched parentheses detected",
                FixId = "Validation",
                Severity = Models.ValidationSeverity.Error
            });
        }

        return errors;
    }

    /// <summary>
    /// Converts an AnalyzerFix to a FixPreview
    /// </summary>
    private static FixPreview ConvertToFixPreview(AnalyzerFix fix)
    {
        return new FixPreview
        {
            FixId = fix.Id,
            IssueId = fix.RuleId, // Use RuleId as IssueId for now
            FilePath = fix.Edits.FirstOrDefault()?.FilePath ?? string.Empty,
            OriginalContent = string.Empty, // Not available in AnalyzerFix
            ModifiedContent = string.Empty, // Not available in AnalyzerFix
            Edits = fix.Edits,
            Description = fix.Description,
            Confidence = fix.Confidence
        };
    }

    /// <summary>
    /// Converts MCPsharp.Models.BulkEdit.FileRollbackResult to MCPsharp.Models.Analyzers.FileRollbackResult
    /// </summary>
    private static Models.Analyzers.FileRollbackResult ConvertToFileRollbackResult(MCPsharp.Models.BulkEdit.FileRollbackResult source)
    {
        return new Models.Analyzers.FileRollbackResult
        {
            FilePath = source.FilePath,
            Success = source.Success,
            ErrorMessage = source.ErrorMessage,
            OperationType = source.OperationType,
            RollbackDuration = source.ProcessingTime
        };
    }

    /// <summary>
    /// Converts MCPsharp.Models.BulkEdit.RollbackError to MCPsharp.Models.Analyzers.RollbackError
    /// </summary>
    private static Models.Analyzers.RollbackError ConvertToRollbackError(MCPsharp.Models.BulkEdit.RollbackError source)
    {
        return new Models.Analyzers.RollbackError
        {
            FilePath = source.FilePath,
            ErrorMessage = source.ErrorMessage,
            ExceptionDetails = source.ExceptionDetails,
            IsRecoverable = source.IsRecoverable,
            Timestamp = source.Timestamp
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Previewing {FixCount} fixes for {IssueCount} issues")]
    private static partial void LogPreviewingFixes(ILogger logger, int fixCount, int issueCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error reading file for preview: {FilePath}")]
    private static partial void LogErrorReadingFileForPreview(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error generating preview for fix: {FixId}")]
    private static partial void LogErrorGeneratingPreview(ILogger logger, Exception exception, string fixId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error previewing fixes")]
    private static partial void LogErrorPreviewingFixes(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {FixCount} fixes in session {SessionId}")]
    private static partial void LogApplyingFixes(ILogger logger, int fixCount, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying fix: {FixId}")]
    private static partial void LogErrorApplyingFix(ILogger logger, Exception exception, string fixId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying fixes in session {SessionId}")]
    private static partial void LogErrorApplyingFixesInSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting conflicts")]
    private static partial void LogErrorDetectingConflicts(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resolving conflict: {ConflictId}")]
    private static partial void LogErrorResolvingConflict(ILogger logger, Exception exception, string conflictId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error resolving conflicts")]
    private static partial void LogErrorResolvingConflicts(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rolling back fixes for session {SessionId}")]
    private static partial void LogRollingBackFixes(ILogger logger, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error restoring file: {FilePath}")]
    private static partial void LogErrorRestoringFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error rolling back session {SessionId}")]
    private static partial void LogErrorRollingBackSession(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error reading file for validation: {FilePath}")]
    private static partial void LogErrorReadingFileForValidation(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error validating fixes for session {SessionId}")]
    private static partial void LogErrorValidatingFixes(ILogger logger, Exception exception, string sessionId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error creating backup for file: {FilePath}")]
    private static partial void LogErrorCreatingBackup(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying edit for fix {FixId} to file {FilePath}")]
    private static partial void LogErrorApplyingEdit(ILogger logger, Exception exception, string fixId, string? filePath);
}

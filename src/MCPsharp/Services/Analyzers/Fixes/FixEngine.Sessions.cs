using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers.Fixes;

public partial class FixEngine
{
    public Task<Models.Analyzers.RollbackResult> RollbackFixesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RollbackFixes(sessionId));
    }

    private Models.Analyzers.RollbackResult RollbackFixes(string sessionId)
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

    public Task<ImmutableArray<FixSession>> GetFixHistoryAsync(int maxSessions = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_sessions.Values
            .OrderByDescending(s => s.StartTime)
            .Take(maxSessions)
            .ToImmutableArray());
    }

    public Task<FixStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = _sessions.Values.ToList();
        var now = DateTime.UtcNow;
        var periodStart = now.AddDays(-30); // Last 30 days

        var recentSessions = sessions.Where(s => s.StartTime >= periodStart).ToList();

        return Task.FromResult(new FixStatistics
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
        });
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
}

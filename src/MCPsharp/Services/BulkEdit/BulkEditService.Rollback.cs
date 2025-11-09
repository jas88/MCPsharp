using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Rollback and recovery management for bulk edit operations
/// </summary>
public partial class BulkEditService
{
    /// <inheritdoc />
    public async Task<BulkEditResult> RollbackBulkEditAsync(
        string rollbackId,
        CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        _logger?.LogInformation("Starting rollback operation {OperationId} for rollback session {RollbackId}",
            operationId, rollbackId);

        try
        {
            if (!_rollbackSessions.TryGetValue(rollbackId, out var rollbackInfo))
            {
                var errors = new List<BulkEditError>
                {
                    new BulkEditError
                    {
                        FilePath = "Rollback session",
                        ErrorMessage = $"Rollback session {rollbackId} not found or expired",
                        Timestamp = DateTime.UtcNow
                    }
                };

                return new BulkEditResult
                {
                    Success = false,
                    TotalFiles = 0,
                    ModifiedFiles = 0,
                    SkippedFiles = 0,
                    FailedFiles = 1,
                    FileResults = Array.Empty<FileBulkEditResult>(),
                    Errors = errors,
                    ElapsedTime = DateTime.UtcNow - startTime,
                    Summary = "Rollback session not found or expired",
                    Error = $"Rollback session {rollbackId} not found or expired",
                    OperationId = operationId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }

            if (!rollbackInfo.CanRollback)
            {
                var errors = new List<BulkEditError>
                {
                    new BulkEditError
                    {
                        FilePath = "Rollback session",
                        ErrorMessage = $"Rollback session {rollbackId} is no longer available",
                        Timestamp = DateTime.UtcNow
                    }
                };

                return new BulkEditResult
                {
                    Success = false,
                    TotalFiles = 0,
                    ModifiedFiles = 0,
                    SkippedFiles = 0,
                    FailedFiles = 1,
                    FileResults = Array.Empty<FileBulkEditResult>(),
                    Errors = errors,
                    ElapsedTime = DateTime.UtcNow - startTime,
                    Summary = "Rollback session no longer available",
                    Error = $"Rollback session {rollbackId} is no longer available",
                    OperationId = operationId,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow
                };
            }

            var fileResults = new List<FileBulkEditResult>();
            var successfulRollbacks = 0;

            // Process each file rollback
            var filesToRollback = rollbackInfo.Files ?? new List<RollbackFileInfo>();
            var tasks = filesToRollback.Select(async rollbackFile =>
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessRollbackForFile(rollbackFile, cancellationToken);
                }
                finally
                {
                    _processingSemaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            fileResults.AddRange(results);
            successfulRollbacks = results.Count(r => r.Success);

            var endTime = DateTime.UtcNow;

            var summary = new BulkEditSummary
            {
                TotalFilesMatched = filesToRollback.Count,
                TotalFilesProcessed = fileResults.Count,
                SuccessfulFiles = successfulRollbacks,
                FailedFiles = fileResults.Count(r => !r.Success),
                SkippedFiles = fileResults.Count(r => r.Skipped),
                TotalChangesApplied = successfulRollbacks,
                TotalBytesProcessed = fileResults.Sum(r => r.OriginalSize),
                TotalBytesWritten = fileResults.Where(r => r.Success).Sum(r => r.NewSize),
                BackupsCreated = 0,
                AverageProcessingTime = fileResults.Count > 0
                    ? TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessDuration.Ticks) / fileResults.Count)
                    : TimeSpan.Zero,
                FilesPerSecond = fileResults.Count > 0 && (endTime - startTime).TotalSeconds > 0
                    ? fileResults.Count / (endTime - startTime).TotalSeconds
                    : 0
            };

            // Clean up rollback session after successful rollback
            if (successfulRollbacks == filesToRollback.Count)
            {
                _rollbackSessions.TryRemove(rollbackId, out _);
                try
                {
                    if (Directory.Exists(rollbackInfo.RollbackDirectory))
                    {
                        Directory.Delete(rollbackInfo.RollbackDirectory, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to cleanup rollback directory {Directory}", rollbackInfo.RollbackDirectory);
                }
            }

            var rollbackFileCount = filesToRollback.Count;
            var rollbackErrors = new List<BulkEditError>();
            if (fileResults.Count(r => !r.Success) > 0)
            {
                rollbackErrors.Add(new BulkEditError
                {
                    FilePath = "Multiple files",
                    ErrorMessage = $"Rollback failed for {fileResults.Count(r => !r.Success)} files",
                    Timestamp = DateTime.UtcNow
                });
            }

            return new BulkEditResult
            {
                Success = successfulRollbacks == rollbackFileCount,
                TotalFiles = fileResults.Count,
                ModifiedFiles = successfulRollbacks,
                SkippedFiles = fileResults.Count(r => r.Skipped),
                FailedFiles = fileResults.Count(r => !r.Success),
                FileResults = fileResults,
                Errors = rollbackErrors,
                ElapsedTime = endTime - startTime,
                Summary = summary.ToString(),
                OperationId = operationId,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Rollback operation {OperationId} failed", operationId);
            return CreateErrorResult(operationId, startTime, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RollbackInfo>> GetAvailableRollbacksAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Clean up expired rollbacks first
            await CleanupExpiredRollbacksAsync(cancellationToken);

            return _rollbackSessions.Values
                .Where(r => r.CanRollback)
                .OrderByDescending(r => r.ExpiresAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get available rollbacks");
            return Array.Empty<RollbackInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredRollbacksAsync(
        CancellationToken cancellationToken = default)
    {
        var cleanedCount = 0;
        var expiredRollbacks = _rollbackSessions
            .Where(kvp => !kvp.Value.CanRollback)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var rollbackId in expiredRollbacks)
        {
            if (_rollbackSessions.TryRemove(rollbackId, out var rollbackInfo))
            {
                try
                {
                    if (Directory.Exists(rollbackInfo.RollbackDirectory))
                    {
                        Directory.Delete(rollbackInfo.RollbackDirectory, true);
                    }
                    cleanedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to cleanup expired rollback {RollbackId}", rollbackId);
                }
            }
        }

        if (cleanedCount > 0)
        {
            _logger?.LogInformation("Cleaned up {Count} expired rollback sessions", cleanedCount);
        }

        return cleanedCount;
    }

    private async Task<RollbackInfo> CreateRollbackSession(
        string operationId,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken)
    {
        var rollbackId = Guid.NewGuid().ToString();
        var rollbackDirectory = Path.Combine(_tempDirectory, "Rollbacks", rollbackId);
        Directory.CreateDirectory(rollbackDirectory);

        var rollbackFiles = new List<RollbackFileInfo>();
        var rollbackSize = 0L;

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!File.Exists(filePath))
                    continue;

                var originalChecksum = await ComputeFileChecksum(filePath, cancellationToken);
                var backupPath = Path.Combine(rollbackDirectory, Guid.NewGuid().ToString() + Path.GetExtension(filePath));

                // Create backup
                File.Copy(filePath, backupPath);
                var backupChecksum = await ComputeFileChecksum(backupPath, cancellationToken);

                var backupFileInfo = new System.IO.FileInfo(backupPath);
                rollbackFiles.Add(new RollbackFileInfo
                {
                    OriginalPath = filePath,
                    BackupPath = backupPath,
                    OriginalChecksum = originalChecksum,
                    BackupChecksum = backupChecksum,
                    BackupSize = backupFileInfo.Length,
                    WasCreated = false,
                    WasDeleted = false
                });

                rollbackSize += backupFileInfo.Length;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to create backup for file {FilePath}", filePath);
            }
        }

        var rollbackInfo = new RollbackInfo
        {
            RollbackId = rollbackId,
            OriginalOperationId = operationId,
            OperationType = "BulkEdit",
            ModifiedFiles = files.Select(f => f).ToList(),
            BackupLocations = files.ToDictionary(f => f, f => string.Empty),
            OperationTimestamp = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7), // Rollbacks expire after 7 days
            TotalBackupSize = rollbackSize,
            IsRollbackPossible = true,
            RollbackDirectory = rollbackDirectory,
            Files = rollbackFiles
        };

        _rollbackSessions[rollbackId] = rollbackInfo;
        return rollbackInfo;
    }

    private static async Task<string> ComputeFileChecksum(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<FileBulkEditResult> ProcessRollbackForFile(
        RollbackFileInfo rollbackFile,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var originalSize = 0L;
        var newSize = 0L;

        try
        {
            if (!rollbackFile.BackupExists)
            {
                return new FileBulkEditResult
                {
                    FilePath = rollbackFile.OriginalPath,
                    Success = false,
                    ErrorMessage = "Backup file not found",
                    ChangesApplied = 0,
                    ChangesCount = 0,
                    Skipped = true,
                    SkipReason = "Backup missing",
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false
                };
            }

            // Verify backup integrity
            var backupChecksum = await ComputeFileChecksum(rollbackFile.BackupPath, cancellationToken);
            if (backupChecksum != rollbackFile.BackupChecksum)
            {
                return new FileBulkEditResult
                {
                    FilePath = rollbackFile.OriginalPath,
                    Success = false,
                    ErrorMessage = "Backup file integrity check failed",
                    ChangesCount = 0,
                    ChangesApplied = 0,
                    Skipped = true,
                    SkipReason = "Backup corrupted",
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false
                };
            }

            // Get original file size
            if (File.Exists(rollbackFile.OriginalPath))
            {
                originalSize = new System.IO.FileInfo(rollbackFile.OriginalPath).Length;
            }

            // Restore from backup
            File.Copy(rollbackFile.BackupPath, rollbackFile.OriginalPath, true);
            newSize = new System.IO.FileInfo(rollbackFile.OriginalPath).Length;

            return new FileBulkEditResult
            {
                FilePath = rollbackFile.OriginalPath,
                Success = true,
                ChangesCount = 1,
                ChangesApplied = 1,
                Skipped = false,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to rollback file {FilePath}", rollbackFile.OriginalPath);
            return new FileBulkEditResult
            {
                FilePath = rollbackFile.OriginalPath,
                Success = false,
                ErrorMessage = ex.Message,
                ChangesCount = 0,
                ChangesApplied = 0,
                Skipped = false,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false
            };
        }
    }
}

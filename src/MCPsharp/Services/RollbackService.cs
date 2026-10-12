using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Service for managing backup and rollback operations for bulk edits
/// </summary>
public partial class RollbackService : IRollbackService, IDisposable
{
    private readonly ILogger<RollbackService> _logger;
    private readonly string _rollbackDirectory;
    private readonly ConcurrentDictionary<string, RollbackInfo> _activeRollbacks;
    private readonly SemaphoreSlim _operationSemaphore;
    private bool _disposed;
    private readonly JsonSerializerOptions _jsonOptions;

    // Configuration constants
    private const int DefaultRetentionDays = 7;
    private const int MaxConcurrentOperations = 10;
    private const string MetadataFileName = "rollback-metadata.json";

    public RollbackService(ILogger<RollbackService>? logger = null)
    {
        _logger = logger ?? NullLogger<RollbackService>.Instance;
        _rollbackDirectory = Path.Combine(Path.GetTempPath(), "MCPsharp", "Rollbacks");
        _activeRollbacks = new ConcurrentDictionary<string, RollbackInfo>();
        _operationSemaphore = new SemaphoreSlim(MaxConcurrentOperations, MaxConcurrentOperations);
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        // Ensure rollback directory exists
        Directory.CreateDirectory(_rollbackDirectory);
        
        // Load existing rollback sessions on startup
        _ = Task.Run(LoadExistingRollbacksAsync);
    }

    /// <inheritdoc />
    public async Task<RollbackInfo> CreateBackupAsync(
        string operationId,
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("Operation ID cannot be null or empty", nameof(operationId));

        if (files == null || !files.Any())
            throw new ArgumentException("Files list cannot be null or empty", nameof(files));

        await _operationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var rollbackId = Guid.NewGuid().ToString();
            var sessionDirectory = Path.Combine(_rollbackDirectory, rollbackId);
            Directory.CreateDirectory(sessionDirectory);

            LogCreatingBackupSessionForOperation(_logger, rollbackId, operationId, files.Count);

            var rollbackFiles = new List<RollbackFileInfo>();
            var totalBackupSize = 0L;
            var createdFiles = 0;
            var skippedFiles = 0;

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!File.Exists(filePath))
                    {
                        LogFileNotFoundDuringBackup(_logger, filePath);
                        skippedFiles++;
                        continue;
                    }

                    // Create backup
                    var backupFileName = $"{Guid.NewGuid()}{Path.GetExtension(filePath)}";
                    var backupPath = Path.Combine(sessionDirectory, backupFileName);
                    
                    // Copy file with retry logic
                    await CopyFileWithRetryAsync(filePath, backupPath, cancellationToken);

                    // Calculate checksums
                    var originalChecksum = await ComputeFileChecksumAsync(filePath, cancellationToken);
                    var backupChecksum = await ComputeFileChecksumAsync(backupPath, cancellationToken);

                    // Verify backup integrity immediately
                    if (originalChecksum != backupChecksum)
                    {
                        throw new InvalidOperationException($"Backup integrity check failed for {filePath}");
                    }

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

                    totalBackupSize += backupFileInfo.Length;
                    createdFiles++;

                    LogCreatedBackupFor(_logger, filePath, backupPath);
                }
                catch (Exception ex)
                {
                    // Continue with other files even if one fails
                    LogFailedToCreateBackupFor(_logger, ex, filePath);
                    skippedFiles++;
                }
            }

            var rollbackInfo = new RollbackInfo
            {
                RollbackId = rollbackId,
                OriginalOperationId = operationId,
                OperationType = "BulkEdit",
                ModifiedFiles = rollbackFiles.Select(f => f.OriginalPath).ToList(),
                BackupLocations = rollbackFiles.ToDictionary(f => f.OriginalPath, f => f.BackupPath),
                OperationTimestamp = DateTime.UtcNow,
                RollbackDirectory = sessionDirectory,
                Files = rollbackFiles,
                ExpiresAt = DateTime.UtcNow.AddDays(DefaultRetentionDays),
                TotalBackupSize = totalBackupSize,
                IsRollbackPossible = true
            };

            // Save metadata
            await SaveRollbackMetadataAsync(rollbackInfo, cancellationToken);

            // Add to active rollbacks
            _activeRollbacks[rollbackId] = rollbackInfo;

            LogBackupSessionCreatedSuccessfullyFiles(_logger, rollbackId, createdFiles, files.Count, totalBackupSize);

            return rollbackInfo;
        }
        catch (Exception ex)
        {
            LogFailedToCreateBackupSession(_logger, ex, operationId);
            throw;
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RollbackResult> RollbackAsync(
        string rollbackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rollbackId))
            throw new ArgumentException("Rollback ID cannot be null or empty", nameof(rollbackId));

        await _operationSemaphore.WaitAsync(cancellationToken);
        try
        {
            LogStartingRollbackOperationForSession(_logger, rollbackId);

            var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
            if (rollbackInfo == null)
            {
                return new Models.BulkEdit.RollbackResult
                {
                    Success = false,
                    SessionId = rollbackId,
                    TotalFiles = 0,
                    SuccessfulRollbacks = 0,
                    FailedRollbacks = 0,
                    FileResults = Array.Empty<FileRollbackResult>(),
                    Errors = new List<Models.BulkEdit.RollbackError>(),
                    ElapsedTime = TimeSpan.Zero,
                    CleanupCompleted = false,
                    Summary = $"Rollback session {rollbackId} not found"
                };
            }

            if (!rollbackInfo.CanRollback)
            {
                return new Models.BulkEdit.RollbackResult
                {
                    Success = false,
                    SessionId = rollbackId,
                    TotalFiles = 0,
                    SuccessfulRollbacks = 0,
                    FailedRollbacks = 0,
                    FileResults = Array.Empty<FileRollbackResult>(),
                    Errors = new List<Models.BulkEdit.RollbackError> { new Models.BulkEdit.RollbackError { FilePath = "", ErrorMessage = $"Rollback session {rollbackId} is no longer valid or has expired", IsRecoverable = false, Timestamp = DateTime.UtcNow } },
                    ElapsedTime = TimeSpan.Zero,
                    CleanupCompleted = false,
                    Summary = $"Rollback session {rollbackId} is no longer valid or has expired"
                };
            }

            var startTime = DateTime.UtcNow;
            var fileResults = new List<RollbackFileResult>();
            var successfulRollbacks = 0;
            var failedRollbacks = 0;

            // Process files in parallel with limited concurrency
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
            var tasks = rollbackInfo.Files.Select(async rollbackFile =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessRollbackForFileAsync(rollbackFile, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            fileResults.AddRange(results);

            successfulRollbacks = results.Count(r => r.Success && !r.Skipped);
            failedRollbacks = results.Count(r => !r.Success);

            var endTime = DateTime.UtcNow;
            var overallSuccess = failedRollbacks == 0;

            // If rollback was successful, remove the session
            if (overallSuccess)
            {
                _ = Task.Run(async () => await DeleteRollbackAsync(rollbackId, cancellationToken), cancellationToken);
            }

            LogRollbackOperationCompletedSuccessFiles(_logger, rollbackId, overallSuccess, successfulRollbacks, rollbackInfo.Files.Count);

            // Convert RollbackFileResult to Models.BulkEdit.FileRollbackResult
            var modelFileResults = fileResults.Select(fr => new Models.BulkEdit.FileRollbackResult
            {
                FilePath = fr.FilePath,
                Success = fr.Success,
                OperationType = fr.Success ? Models.BulkEdit.RollbackOperationType.RestoredFromBackup : Models.BulkEdit.RollbackOperationType.NoAction,
                ErrorMessage = fr.Error ?? fr.SkipReason,
                ProcessingTime = fr.ProcessDuration
            }).ToList();

            // Convert to errors list for the result
            var errors = fileResults
                .Where(fr => !fr.Success)
                .Select(fr => new Models.BulkEdit.RollbackError
                {
                    FilePath = fr.FilePath,
                    ErrorMessage = fr.Error ?? "Unknown error",
                    ExceptionDetails = fr.Error,
                    IsRecoverable = false,
                    Timestamp = DateTime.UtcNow
                }).ToList();

            return new Models.BulkEdit.RollbackResult
            {
                Success = overallSuccess,
                SessionId = rollbackId,
                TotalFiles = rollbackInfo.Files.Count,
                SuccessfulRollbacks = successfulRollbacks,
                FailedRollbacks = failedRollbacks,
                FileResults = modelFileResults,
                Errors = errors,
                ElapsedTime = endTime - startTime,
                CleanupCompleted = overallSuccess,
                Summary = overallSuccess
                    ? $"Successfully rolled back {successfulRollbacks} files"
                    : $"Rollback completed with {failedRollbacks} failures"
            };
        }
        catch (Exception ex)
        {
            LogRollbackOperationFailed(_logger, ex, rollbackId);
            return new Models.BulkEdit.RollbackResult
            {
                Success = false,
                SessionId = rollbackId,
                TotalFiles = 0,
                SuccessfulRollbacks = 0,
                FailedRollbacks = 0,
                FileResults = Array.Empty<Models.BulkEdit.FileRollbackResult>(),
                Errors = new List<Models.BulkEdit.RollbackError>
                {
                    new Models.BulkEdit.RollbackError
                    {
                        FilePath = "",
                        ErrorMessage = ex.Message,
                        ExceptionDetails = ex.ToString(),
                        IsRecoverable = false,
                        Timestamp = DateTime.UtcNow
                    }
                },
                ElapsedTime = TimeSpan.Zero,
                CleanupCompleted = false,
                Summary = $"Rollback failed: {ex.Message}"
            };
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RollbackInfo?> GetRollbackInfoAsync(
        string rollbackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rollbackId))
            return null;

        try
        {
            // Check active rollbacks first
            if (_activeRollbacks.TryGetValue(rollbackId, out var activeRollback))
            {
                return activeRollback;
            }

            // Try to load from disk
            var sessionDirectory = Path.Combine(_rollbackDirectory, rollbackId);
            if (!Directory.Exists(sessionDirectory))
                return null;

            var metadataPath = Path.Combine(sessionDirectory, MetadataFileName);
            if (!File.Exists(metadataPath))
                return null;

            var metadataJson = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var rollbackInfo = JsonSerializer.Deserialize<RollbackInfo>(metadataJson, _jsonOptions);
            
            if (rollbackInfo != null)
            {
                // Add to active rollbacks cache
                _activeRollbacks[rollbackId] = rollbackInfo;
            }

            return rollbackInfo;
        }
        catch (Exception ex)
        {
            LogFailedToGetRollbackInfo(_logger, ex, rollbackId);
            return null;
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

            // Load all rollback directories
            var rollbacks = new List<RollbackInfo>();
            
            foreach (var directory in Directory.GetDirectories(_rollbackDirectory))
            {
                var rollbackId = Path.GetFileName(directory);
                var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
                
                if (rollbackInfo != null && rollbackInfo.CanRollback)
                {
                    rollbacks.Add(rollbackInfo);
                }
            }

            return rollbacks.OrderByDescending(r => r.ExpiresAt).ToList();
        }
        catch (Exception ex)
        {
            LogFailedToGetAvailableRollbacks(_logger, ex);
            return Array.Empty<RollbackInfo>();
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredRollbacksAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cleanedCount = 0;
            var allRollbacks = new List<RollbackInfo>();

            // Get all rollback directories
            foreach (var directory in Directory.GetDirectories(_rollbackDirectory))
            {
                var rollbackId = Path.GetFileName(directory);
                var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
                
                if (rollbackInfo != null)
                {
                    allRollbacks.Add(rollbackInfo);
                }
            }

            // Find expired rollbacks
            var expiredRollbacks = allRollbacks
                .Where(r => !r.CanRollback || DateTime.UtcNow >= r.ExpiresAt)
                .ToList();

            foreach (var expiredRollbackId in expiredRollbacks.Select(expiredRollback => expiredRollback.RollbackId))
            {
                try
                {
                    if (await DeleteRollbackAsync(expiredRollbackId, cancellationToken))
                    {
                        cleanedCount++;
                        LogCleanedUpExpiredRollback(_logger, expiredRollbackId);
                    }
                }
                catch (Exception ex)
                {
                    LogFailedToCleanupExpiredRollback(_logger, ex, expiredRollbackId);
                }
            }

            if (cleanedCount > 0)
            {
                LogCleanedUpExpiredRollbackSessions(_logger, cleanedCount);
            }

            return cleanedCount;
        }
        catch (Exception ex)
        {
            LogFailedToCleanupExpiredRollbacks(_logger, ex);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRollbackAsync(
        string rollbackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rollbackId))
            return false;

        try
        {
            var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
            if (rollbackInfo == null)
                return false;

            // Remove from active rollbacks
            _activeRollbacks.TryRemove(rollbackId, out _);

            // Delete the session directory
            var sessionDirectory = rollbackInfo.RollbackDirectory;
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, true);
                LogDeletedRollbackDirectory(_logger, sessionDirectory);
            }

            LogDeletedRollbackSession(_logger, rollbackId);
            return true;
        }
        catch (Exception ex)
        {
            LogFailedToDeleteRollback(_logger, ex, rollbackId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<RollbackVerificationResult> VerifyRollbackIntegrityAsync(
        string rollbackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rollbackId))
            throw new ArgumentException("Rollback ID cannot be null or empty", nameof(rollbackId));

        await _operationSemaphore.WaitAsync(cancellationToken);
        try
        {
            var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
            if (rollbackInfo == null)
            {
                return new RollbackVerificationResult
                {
                    Success = false,
                    FileResults = Array.Empty<FileIntegrityResult>(),
                    VerifiedFiles = 0,
                    CorruptedFiles = 0,
                    MissingFiles = 0,
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow
                };
            }

            var startTime = DateTime.UtcNow;
            var fileResults = new List<FileIntegrityResult>();
            var verifiedFiles = 0;
            var corruptedFiles = 0;
            var missingFiles = 0;

            foreach (var rollbackFile in rollbackInfo.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = new FileIntegrityResult
                {
                    FilePath = rollbackFile.OriginalPath,
                    ExpectedChecksum = rollbackFile.BackupChecksum ?? string.Empty,
                    BackupSize = rollbackFile.BackupSize,
                    IntegrityVerified = false,
                    BackupExists = false
                };

                try
                {
                    // Check if backup file exists
                    if (!File.Exists(rollbackFile.BackupPath))
                    {
                        result.IntegrityVerified = false;
                        result.BackupExists = false;
                        result.Error = "Backup file not found";
                        missingFiles++;
                    }
                    else
                    {
                        result.BackupExists = true;
                        
                        // Calculate actual checksum
                        var actualChecksum = await ComputeFileChecksumAsync(rollbackFile.BackupPath, cancellationToken);
                        result.ActualChecksum = actualChecksum;
                        
                        // Verify integrity
                        if (actualChecksum == rollbackFile.BackupChecksum)
                        {
                            result.IntegrityVerified = true;
                            verifiedFiles++;
                        }
                        else
                        {
                            result.IntegrityVerified = false;
                            result.Error = "Checksum mismatch - backup file may be corrupted";
                            corruptedFiles++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogIntegrityCheckFailed(_logger, ex, result.FilePath);
                    result.IntegrityVerified = false;
                    result.Error = ex.Message;
                    corruptedFiles++;
                }

                fileResults.Add(result);
            }

            var endTime = DateTime.UtcNow;
            var overallSuccess = corruptedFiles == 0 && missingFiles == 0;

            LogIntegrityVerificationForRollbackCompleted(_logger, rollbackId, verifiedFiles, corruptedFiles, missingFiles);

            return new RollbackVerificationResult
            {
                Success = overallSuccess,
                FileResults = fileResults,
                VerifiedFiles = verifiedFiles,
                CorruptedFiles = corruptedFiles,
                MissingFiles = missingFiles,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            LogFailedToVerifyRollbackIntegrity(_logger, ex, rollbackId);
            return new RollbackVerificationResult
            {
                Success = false,
                FileResults = Array.Empty<FileIntegrityResult>(),
                VerifiedFiles = 0,
                CorruptedFiles = 0,
                MissingFiles = 0,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow
            };
        }
        finally
        {
            _operationSemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<RollbackHistory> GetRollbackHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var allRollbacks = new List<RollbackInfo>();
            var activeRollbacks = 0;
            var expiredRollbacks = 0;
            var totalSpaceUsed = 0L;
            var totalBackedUpFiles = 0;
            DateTime? oldestRollback = null;
            DateTime? newestRollback = null;

            // Load all rollback directories
            foreach (var directory in Directory.GetDirectories(_rollbackDirectory))
            {
                var rollbackId = Path.GetFileName(directory);
                var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
                
                if (rollbackInfo != null)
                {
                    allRollbacks.Add(rollbackInfo);
                    
                    if (rollbackInfo.CanRollback)
                        activeRollbacks++;
                    else
                        expiredRollbacks++;
                    
                    totalSpaceUsed += rollbackInfo.RollbackSize;
                    totalBackedUpFiles += rollbackInfo.Files.Count;
                    
                    if (!oldestRollback.HasValue || rollbackInfo.ExpiresAt < oldestRollback.Value)
                        oldestRollback = rollbackInfo.ExpiresAt;
                    
                    if (!newestRollback.HasValue || rollbackInfo.ExpiresAt > newestRollback.Value)
                        newestRollback = rollbackInfo.ExpiresAt;
                }
            }

            return new RollbackHistory
            {
                AllRollbacks = allRollbacks.OrderByDescending(r => r.ExpiresAt).ToList(),
                ActiveRollbacks = activeRollbacks,
                ExpiredRollbacks = expiredRollbacks,
                TotalSpaceUsed = totalSpaceUsed,
                TotalBackedUpFiles = totalBackedUpFiles,
                OldestRollback = oldestRollback,
                NewestRollback = newestRollback,
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogFailedToGetRollbackHistory(_logger, ex);
            return new RollbackHistory
            {
                AllRollbacks = Array.Empty<RollbackInfo>(),
                ActiveRollbacks = 0,
                ExpiredRollbacks = 0,
                TotalSpaceUsed = 0,
                TotalBackedUpFiles = 0,
                GeneratedAt = DateTime.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> CanRollbackAsync(
        string rollbackId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rollbackId))
            return false;

        try
        {
            var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
            return rollbackInfo?.CanRollback ?? false;
        }
        catch (Exception ex)
        {
            LogFailedToCheckIfRollback(_logger, ex, rollbackId);
            return false;
        }
    }

    /// <inheritdoc />
    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    public async Task<long> EstimateBackupSpaceAsync(
        IReadOnlyList<string> files,
        CancellationToken cancellationToken = default)
    {
        if (files == null || !files.Any())
            return 0;

        try
        {
            long totalSize = 0;

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new System.IO.FileInfo(filePath);
                        totalSize += fileInfo.Length;
                    }
                }
                catch (Exception ex)
                {
                    LogFailedToGetSizeFor(_logger, ex, filePath);
                }
            }

            // Add overhead for metadata (approximately 1KB per file + base overhead)
            var metadataOverhead = 1024L * files.Count + 10240L;
            return totalSize + metadataOverhead;
        }
        catch (Exception ex)
        {
            LogFailedToEstimateBackupSpace(_logger, ex);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExportRollbackMetadataAsync(
        string rollbackId,
        string exportPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rollbackId))
            throw new ArgumentException("Rollback ID cannot be null or empty", nameof(rollbackId));

        if (string.IsNullOrWhiteSpace(exportPath))
            throw new ArgumentException("Export path cannot be null or empty", nameof(exportPath));

        try
        {
            var rollbackInfo = await GetRollbackInfoAsync(rollbackId, cancellationToken);
            if (rollbackInfo == null)
                return false;

            // Ensure export directory exists
            var exportDirectory = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(exportDirectory))
            {
                Directory.CreateDirectory(exportDirectory);
            }

            // Serialize and save metadata
            var metadataJson = JsonSerializer.Serialize(rollbackInfo, _jsonOptions);
            await File.WriteAllTextAsync(exportPath, metadataJson, cancellationToken);

            LogExportedRollbackMetadataForTo(_logger, rollbackId, exportPath);
            return true;
        }
        catch (Exception ex)
        {
            LogFailedToExportRollbackMetadata(_logger, ex, rollbackId, exportPath);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<RollbackInfo?> ImportRollbackMetadataAsync(
        string importPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(importPath))
            throw new ArgumentException("Import path cannot be null or empty", nameof(importPath));

        if (!File.Exists(importPath))
            return null;

        try
        {
            var metadataJson = await File.ReadAllTextAsync(importPath, cancellationToken);
            var rollbackInfo = JsonSerializer.Deserialize<RollbackInfo>(metadataJson, _jsonOptions);
            
            if (rollbackInfo != null)
            {
                // Generate a new rollback ID to avoid conflicts
                var newRollbackId = Guid.NewGuid().ToString();
                var newSessionDirectory = Path.Combine(_rollbackDirectory, newRollbackId);
                
                // Update the rollback info with new paths
                var newRollbackInfo = new RollbackInfo
                {
                    RollbackId = newRollbackId,
                    OriginalOperationId = rollbackInfo.OriginalOperationId,
                    OperationType = rollbackInfo.OperationType,
                    ModifiedFiles = rollbackInfo.ModifiedFiles,
                    BackupLocations = rollbackInfo.BackupLocations,
                    OperationTimestamp = rollbackInfo.OperationTimestamp,
                    RollbackDirectory = newSessionDirectory,
                    Files = rollbackInfo.Files,
                    ExpiresAt = DateTime.UtcNow.AddDays(DefaultRetentionDays),
                    TotalBackupSize = rollbackInfo.TotalBackupSize,
                    IsRollbackPossible = true
                };

                // Note: This doesn't copy the actual backup files, just the metadata
                // In a production scenario, you might want to copy the backup files as well
                
                await SaveRollbackMetadataAsync(newRollbackInfo, cancellationToken);
                _activeRollbacks[newRollbackId] = newRollbackInfo;

                LogImportedRollbackMetadataFromAs(_logger, importPath, newRollbackId);
                return newRollbackInfo;
            }

            return null;
        }
        catch (Exception ex)
        {
            LogFailedToImportRollbackMetadata(_logger, ex, importPath);
            return null;
        }
    }

    #region Private Helper Methods

    private async Task LoadExistingRollbacksAsync()
    {
        try
        {
            if (!Directory.Exists(_rollbackDirectory))
                return;

            foreach (var directory in Directory.GetDirectories(_rollbackDirectory))
            {
                try
                {
                    var rollbackId = Path.GetFileName(directory);
                    var metadataPath = Path.Combine(directory, MetadataFileName);
                    
                    if (File.Exists(metadataPath))
                    {
                        var metadataJson = await File.ReadAllTextAsync(metadataPath);
                        var rollbackInfo = JsonSerializer.Deserialize<RollbackInfo>(metadataJson, _jsonOptions);
                        
                        if (rollbackInfo != null && rollbackInfo.CanRollback)
                        {
                            _activeRollbacks[rollbackId] = rollbackInfo;
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogFailedToLoadRollbackFrom(_logger, ex, directory);
                }
            }

            LogLoadedExistingRollbackSessions(_logger, _activeRollbacks.Count);
        }
        catch (Exception ex)
        {
            LogFailedToLoadExistingRollbacks(_logger, ex);
        }
    }

    private async Task SaveRollbackMetadataAsync(RollbackInfo rollbackInfo, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(rollbackInfo.RollbackDirectory, MetadataFileName);
        var metadataJson = JsonSerializer.Serialize(rollbackInfo, _jsonOptions);
        await File.WriteAllTextAsync(metadataPath, metadataJson, cancellationToken);
    }

    private static async Task<string> ComputeFileChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task CopyFileWithRetryAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await Task.Run(() => File.Copy(sourcePath, destinationPath, true), cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                LogFailedToCopyFileTo(_logger, ex, sourcePath, destinationPath, attempt, maxRetries);
                
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
            }
        }

        // Last attempt - let the exception propagate
        await Task.Run(() => File.Copy(sourcePath, destinationPath, true), cancellationToken);
    }

    private async Task<RollbackFileResult> ProcessRollbackForFileAsync(RollbackFileInfo rollbackFile, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var finalSize = 0L;

        try
        {
            // Check if backup exists
            if (!File.Exists(rollbackFile.BackupPath))
            {
                return new RollbackFileResult
                {
                    FilePath = rollbackFile.OriginalPath,
                    Success = false,
                    Skipped = true,
                    SkipReason = "Backup file not found",
                    IntegrityVerified = false,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    FinalSize = finalSize
                };
            }

            // Verify backup integrity
            var backupChecksum = await ComputeFileChecksumAsync(rollbackFile.BackupPath, cancellationToken);
            var integrityVerified = backupChecksum == rollbackFile.BackupChecksum;

            if (!integrityVerified)
            {
                return new RollbackFileResult
                {
                    FilePath = rollbackFile.OriginalPath,
                    Success = false,
                    Error = "Backup file integrity check failed",
                    Skipped = false,
                    IntegrityVerified = false,
                    OriginalChecksum = rollbackFile.OriginalChecksum,
                    BackupChecksum = backupChecksum,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    FinalSize = finalSize
                };
            }

            // Ensure target directory exists
            var targetDirectory = Path.GetDirectoryName(rollbackFile.OriginalPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // Restore from backup
            await CopyFileWithRetryAsync(rollbackFile.BackupPath, rollbackFile.OriginalPath, cancellationToken);

            // Get final file size
            if (File.Exists(rollbackFile.OriginalPath))
            {
                finalSize = new System.IO.FileInfo(rollbackFile.OriginalPath).Length;
            }

            return new RollbackFileResult
            {
                FilePath = rollbackFile.OriginalPath,
                Success = true,
                Skipped = false,
                IntegrityVerified = true,
                OriginalChecksum = rollbackFile.OriginalChecksum,
                BackupChecksum = rollbackFile.BackupChecksum,
                FinalSize = finalSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogFailedToRollbackFile(_logger, ex, rollbackFile.OriginalPath);
            return new RollbackFileResult
            {
                FilePath = rollbackFile.OriginalPath,
                Success = false,
                Error = ex.Message,
                Skipped = false,
                IntegrityVerified = false,
                OriginalChecksum = rollbackFile.OriginalChecksum,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                FinalSize = finalSize
            };
        }
    }

    /// <summary>
    /// Releases the concurrency semaphore owned by this service
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed resources when <paramref name="disposing"/> is true
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _operationSemaphore.Dispose();
        }

        _disposed = true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Integrity verification failed for {FilePath}")]
    private static partial void LogIntegrityCheckFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Creating backup session {RollbackId} for operation {OperationId} with {FileCount} files")]
    private static partial void LogCreatingBackupSessionForOperation(ILogger logger, string rollbackId, string operationId, int fileCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File not found during backup creation: {FilePath}")]
    private static partial void LogFileNotFoundDuringBackup(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Created backup for {FilePath} -> {BackupPath}")]
    private static partial void LogCreatedBackupFor(ILogger logger, string filePath, string backupPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create backup for file {FilePath}")]
    private static partial void LogFailedToCreateBackupFor(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup session {RollbackId} created successfully. Files: {CreatedFiles}/{TotalFiles}, Size: {BackupSize} bytes")]
    private static partial void LogBackupSessionCreatedSuccessfullyFiles(ILogger logger, string rollbackId, int createdFiles, int totalFiles, long backupSize);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to create backup session for operation {OperationId}")]
    private static partial void LogFailedToCreateBackupSession(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting rollback operation for session {RollbackId}")]
    private static partial void LogStartingRollbackOperationForSession(ILogger logger, string rollbackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rollback operation {RollbackId} completed. Success: {Success}, Files: {Successful}/{Total}")]
    private static partial void LogRollbackOperationCompletedSuccessFiles(ILogger logger, string rollbackId, bool success, int successful, int total);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollback operation {RollbackId} failed")]
    private static partial void LogRollbackOperationFailed(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get rollback info for {RollbackId}")]
    private static partial void LogFailedToGetRollbackInfo(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get available rollbacks")]
    private static partial void LogFailedToGetAvailableRollbacks(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Cleaned up expired rollback {RollbackId}")]
    private static partial void LogCleanedUpExpiredRollback(ILogger logger, string rollbackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to cleanup expired rollback {RollbackId}")]
    private static partial void LogFailedToCleanupExpiredRollback(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired rollback sessions")]
    private static partial void LogCleanedUpExpiredRollbackSessions(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to cleanup expired rollbacks")]
    private static partial void LogFailedToCleanupExpiredRollbacks(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted rollback directory {Directory}")]
    private static partial void LogDeletedRollbackDirectory(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted rollback session {RollbackId}")]
    private static partial void LogDeletedRollbackSession(ILogger logger, string rollbackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to delete rollback {RollbackId}")]
    private static partial void LogFailedToDeleteRollback(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Integrity verification for rollback {RollbackId} completed. Verified: {Verified}, Corrupted: {Corrupted}, Missing: {Missing}")]
    private static partial void LogIntegrityVerificationForRollbackCompleted(ILogger logger, string rollbackId, int verified, int corrupted, int missing);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to verify rollback integrity for {RollbackId}")]
    private static partial void LogFailedToVerifyRollbackIntegrity(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get rollback history")]
    private static partial void LogFailedToGetRollbackHistory(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to check if rollback {RollbackId} is possible")]
    private static partial void LogFailedToCheckIfRollback(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get size for file {FilePath}")]
    private static partial void LogFailedToGetSizeFor(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to estimate backup space")]
    private static partial void LogFailedToEstimateBackupSpace(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Exported rollback metadata for {RollbackId} to {ExportPath}")]
    private static partial void LogExportedRollbackMetadataForTo(ILogger logger, string rollbackId, string exportPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to export rollback metadata for {RollbackId} to {ExportPath}")]
    private static partial void LogFailedToExportRollbackMetadata(ILogger logger, Exception exception, string rollbackId, string exportPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Imported rollback metadata from {ImportPath} as {NewRollbackId}")]
    private static partial void LogImportedRollbackMetadataFromAs(ILogger logger, string importPath, string newRollbackId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to import rollback metadata from {ImportPath}")]
    private static partial void LogFailedToImportRollbackMetadata(ILogger logger, Exception exception, string importPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load rollback from directory {Directory}")]
    private static partial void LogFailedToLoadRollbackFrom(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} existing rollback sessions")]
    private static partial void LogLoadedExistingRollbackSessions(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to load existing rollbacks")]
    private static partial void LogFailedToLoadExistingRollbacks(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to copy file {SourcePath} to {DestinationPath} (attempt {Attempt}/{MaxAttempts})")]
    private static partial void LogFailedToCopyFileTo(ILogger logger, Exception exception, string sourcePath, string destinationPath, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rollback file {FilePath}")]
    private static partial void LogFailedToRollbackFile(ILogger logger, Exception exception, string filePath);

    #endregion
}
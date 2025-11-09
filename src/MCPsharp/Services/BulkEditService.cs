using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Service for performing bulk edit operations across multiple files with parallel processing
/// </summary>
public partial class BulkEditService : IBulkEditService
{
    private readonly ILogger<BulkEditService>? _logger;
    private readonly string _tempDirectory;
    private readonly ConcurrentDictionary<string, RollbackInfo> _rollbackSessions;
    private readonly SemaphoreSlim _processingSemaphore;

    public BulkEditService(ILogger<BulkEditService>? logger = null)
    {
        _logger = logger;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MCPsharp", "BulkEdit");
        _rollbackSessions = new ConcurrentDictionary<string, RollbackInfo>();
        _processingSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        // Ensure temp directory exists
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <inheritdoc />
    public async Task<BulkEditResult> BulkReplaceAsync(
        IReadOnlyList<string> files,
        string regexPattern,
        string replacement,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        options ??= new BulkEditOptions();

        _logger?.LogInformation("Starting bulk replace operation {OperationId} with pattern {Pattern}", operationId, regexPattern);

        // Validate regex pattern - this will throw ArgumentException for invalid patterns
        var regex = new Regex(regexPattern, options.RegexOptions);

        try
        {
            // Check cancellation immediately at the start
            cancellationToken.ThrowIfCancellationRequested();

            // Add a small delay to ensure cancellation can be caught during setup
            await Task.Delay(1, cancellationToken);

            // Get files to process - filter out invalid files but don't fail the operation
            var filesToProcess = new List<string>();
            var fileResults = new List<FileBulkEditResult>();

            foreach (var file in files)
            {
                try
                {
                    if (File.Exists(file))
                    {
                        filesToProcess.Add(file);
                    }
                    else
                    {
                        // Add error result for non-existent file but continue processing
                        fileResults.Add(new FileBulkEditResult
                        {
                            FilePath = file,
                            Success = false,
                            ErrorMessage = "File not found",
                            ChangesApplied = 0,
                            ChangesCount = 0,
                            OriginalSize = 0,
                            NewSize = 0,
                            ProcessStartTime = DateTime.UtcNow,
                            ProcessEndTime = DateTime.UtcNow,
                            EditDuration = TimeSpan.Zero,
                            BackupCreated = false,
                            Skipped = true,
                            SkipReason = "File not found"
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Add error result for problematic file but continue processing
                    fileResults.Add(new FileBulkEditResult
                    {
                        FilePath = file,
                        Success = false,
                        ErrorMessage = ex.Message,
                        ChangesApplied = 0,
                        ChangesCount = 0,
                        OriginalSize = 0,
                        NewSize = 0,
                        ProcessStartTime = DateTime.UtcNow,
                        ProcessEndTime = DateTime.UtcNow,
                        EditDuration = TimeSpan.Zero,
                        BackupCreated = false,
                        Skipped = true,
                        SkipReason = $"Error: {ex.Message}"
                    });
                }
            }

            var changesApplied = 0;

            // Create rollback info if backups are enabled
            RollbackInfo? rollbackInfo = null;
            if (options.CreateBackups)
            {
                rollbackInfo = await CreateRollbackSession(operationId, filesToProcess, cancellationToken);
            }

            // Process files in parallel with semaphore throttling
            var tasks = filesToProcess.Select(async filePath =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _processingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessFileForBulkReplace(
                        filePath, regex, replacement, options, rollbackInfo, cancellationToken);
                }
                finally
                {
                    _processingSemaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            fileResults.AddRange(results);

            var endTime = DateTime.UtcNow;
            changesApplied = fileResults.Sum(r => r.ChangesApplied);

            var summary = new BulkEditSummary
            {
                TotalFilesMatched = filesToProcess.Count,
                TotalFilesProcessed = fileResults.Count,
                SuccessfulFiles = fileResults.Count(r => r.Success),
                FailedFiles = fileResults.Count(r => !r.Success),
                SkippedFiles = fileResults.Count(r => r.Skipped),
                TotalChangesApplied = changesApplied,
                TotalBytesProcessed = fileResults.Sum(r => r.OriginalSize),
                TotalBytesWritten = fileResults.Where(r => r.Success).Sum(r => r.NewSize),
                BackupsCreated = rollbackInfo?.Files.Count(f => !string.IsNullOrEmpty(f.BackupPath)) ?? 0,
                AverageProcessingTime = fileResults.Count > 0
                    ? TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessDuration.Ticks) / fileResults.Count)
                    : TimeSpan.Zero,
                FilesPerSecond = fileResults.Count > 0 && (endTime - startTime).TotalSeconds > 0
                    ? fileResults.Count / (endTime - startTime).TotalSeconds
                    : 0
            };

            var errors = new List<BulkEditError>();
            if (summary.FailedFiles > 0 && options.StopOnFirstError)
            {
                errors.Add(new BulkEditError
                {
                    FilePath = "Multiple files",
                    ErrorMessage = $"Operation stopped due to errors in {summary.FailedFiles} files",
                    Timestamp = DateTime.UtcNow
                });
            }

            var result = new BulkEditResult
            {
                Success = summary.FailedFiles == 0 || !options.StopOnFirstError,
                TotalFiles = summary.TotalFilesProcessed,
                ModifiedFiles = summary.SuccessfulFiles,
                SkippedFiles = summary.SkippedFiles,
                FailedFiles = summary.FailedFiles,
                FileResults = fileResults,
                Errors = errors,
                ElapsedTime = endTime - startTime,
                RollbackSessionId = rollbackInfo?.RollbackId ?? string.Empty,
                Summary = summary.ToString(),
                Error = summary.FailedFiles > 0 && options.StopOnFirstError
                    ? $"Operation stopped due to errors in {summary.FailedFiles} files"
                    : null,
                OperationId = operationId,
                StartTime = startTime,
                EndTime = endTime,
                SummaryData = summary,
                RollbackInfo = rollbackInfo
            };

            _logger?.LogInformation(
                "Bulk replace operation {OperationId} completed. Files: {Total}/{Successful}/{Failed}, Changes: {Changes}",
                operationId, summary.TotalFilesProcessed, summary.SuccessfulFiles, summary.FailedFiles, changesApplied);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Bulk replace operation {OperationId} failed", operationId);
            var errors = new List<BulkEditError>
            {
                new BulkEditError
                {
                    FilePath = "Exception occurred",
                    ErrorMessage = ex.Message,
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
                Summary = "Operation failed with exception",
                Error = ex.Message,
                RollbackSessionId = null,
                OperationId = operationId,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public async Task<BulkEditResult> ConditionalEditAsync(
        IReadOnlyList<string> files,
        BulkEditCondition condition,
        IReadOnlyList<TextEdit> edits,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        options ??= new BulkEditOptions();

        _logger?.LogInformation("Starting conditional edit operation {OperationId} with condition type {ConditionType}",
            operationId, condition.ConditionType);

        try
        {
            var filesToProcess = await ResolveFilePatterns(files, options, cancellationToken);
            var fileResults = new List<FileBulkEditResult>();
            var changesApplied = 0;

            // Create rollback info if backups are enabled
            RollbackInfo? rollbackInfo = null;
            if (options.CreateBackups)
            {
                rollbackInfo = await CreateRollbackSession(operationId, filesToProcess, cancellationToken);
            }

            // Process files in parallel
            var tasks = filesToProcess.Select(async filePath =>
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessFileForConditionalEdit(
                        filePath, condition, edits, options, rollbackInfo, cancellationToken);
                }
                finally
                {
                    _processingSemaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            fileResults.AddRange(results);

            var endTime = DateTime.UtcNow;
            changesApplied = fileResults.Sum(r => r.ChangesApplied);

            var summary = new BulkEditSummary
            {
                TotalFilesMatched = filesToProcess.Count,
                TotalFilesProcessed = fileResults.Count,
                SuccessfulFiles = fileResults.Count(r => r.Success),
                FailedFiles = fileResults.Count(r => !r.Success),
                SkippedFiles = fileResults.Count(r => r.Skipped),
                TotalChangesApplied = changesApplied,
                TotalBytesProcessed = fileResults.Sum(r => r.OriginalSize),
                TotalBytesWritten = fileResults.Where(r => r.Success).Sum(r => r.NewSize),
                BackupsCreated = rollbackInfo?.Files.Count(f => !string.IsNullOrEmpty(f.BackupPath)) ?? 0,
                AverageProcessingTime = fileResults.Count > 0
                    ? TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessDuration.Ticks) / fileResults.Count)
                    : TimeSpan.Zero,
                FilesPerSecond = fileResults.Count > 0 && (endTime - startTime).TotalSeconds > 0
                    ? fileResults.Count / (endTime - startTime).TotalSeconds
                    : 0
            };

            var errors = new List<BulkEditError>();
            if (summary.FailedFiles > 0 && options.StopOnFirstError)
            {
                errors.Add(new BulkEditError
                {
                    FilePath = "Multiple files",
                    ErrorMessage = $"Operation stopped due to errors in {summary.FailedFiles} files",
                    Timestamp = DateTime.UtcNow
                });
            }

            return new BulkEditResult
            {
                Success = summary.FailedFiles == 0 || !options.StopOnFirstError,
                TotalFiles = summary.TotalFilesProcessed,
                ModifiedFiles = summary.SuccessfulFiles,
                SkippedFiles = summary.SkippedFiles,
                FailedFiles = summary.FailedFiles,
                FileResults = fileResults,
                Errors = errors,
                ElapsedTime = endTime - startTime,
                Summary = summary.ToString(),
                RollbackSessionId = rollbackInfo?.RollbackId ?? string.Empty,
                OperationId = operationId,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Conditional edit operation {OperationId} failed", operationId);
            return CreateErrorResult(operationId, startTime, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BulkEditResult> BatchRefactorAsync(
        IReadOnlyList<string> files,
        BulkRefactorPattern refactorPattern,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        options ??= new BulkEditOptions();

        _logger?.LogInformation("Starting batch refactor operation {OperationId} with refactor type {RefactorType}",
            operationId, refactorPattern.RefactorType);

        try
        {
            var filesToProcess = await ResolveFilePatterns(files, options, cancellationToken);
            var fileResults = new List<FileBulkEditResult>();
            var changesApplied = 0;

            // Create rollback info if backups are enabled
            RollbackInfo? rollbackInfo = null;
            if (options.CreateBackups)
            {
                rollbackInfo = await CreateRollbackSession(operationId, filesToProcess, cancellationToken);
            }

            // Process files in parallel
            var tasks = filesToProcess.Select(async filePath =>
            {
                await _processingSemaphore.WaitAsync(cancellationToken);
                try
                {
                    return await ProcessFileForBatchRefactor(
                        filePath, refactorPattern, options, rollbackInfo, cancellationToken);
                }
                finally
                {
                    _processingSemaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);
            fileResults.AddRange(results);

            var endTime = DateTime.UtcNow;
            changesApplied = fileResults.Sum(r => r.ChangesApplied);

            var summary = new BulkEditSummary
            {
                TotalFilesMatched = filesToProcess.Count,
                TotalFilesProcessed = fileResults.Count,
                SuccessfulFiles = fileResults.Count(r => r.Success),
                FailedFiles = fileResults.Count(r => !r.Success),
                SkippedFiles = fileResults.Count(r => r.Skipped),
                TotalChangesApplied = changesApplied,
                TotalBytesProcessed = fileResults.Sum(r => r.OriginalSize),
                TotalBytesWritten = fileResults.Where(r => r.Success).Sum(r => r.NewSize),
                BackupsCreated = rollbackInfo?.Files.Count(f => !string.IsNullOrEmpty(f.BackupPath)) ?? 0,
                AverageProcessingTime = fileResults.Count > 0
                    ? TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessDuration.Ticks) / fileResults.Count)
                    : TimeSpan.Zero,
                FilesPerSecond = fileResults.Count > 0 && (endTime - startTime).TotalSeconds > 0
                    ? fileResults.Count / (endTime - startTime).TotalSeconds
                    : 0
            };

            var errors = new List<BulkEditError>();
            if (summary.FailedFiles > 0 && options.StopOnFirstError)
            {
                errors.Add(new BulkEditError
                {
                    FilePath = "Multiple files",
                    ErrorMessage = $"Operation stopped due to errors in {summary.FailedFiles} files",
                    Timestamp = DateTime.UtcNow
                });
            }

            return new BulkEditResult
            {
                Success = summary.FailedFiles == 0 || !options.StopOnFirstError,
                TotalFiles = summary.TotalFilesProcessed,
                ModifiedFiles = summary.SuccessfulFiles,
                SkippedFiles = summary.SkippedFiles,
                FailedFiles = summary.FailedFiles,
                FileResults = fileResults,
                Errors = errors,
                ElapsedTime = endTime - startTime,
                Summary = summary.ToString(),
                RollbackSessionId = rollbackInfo?.RollbackId ?? string.Empty,
                OperationId = operationId,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Batch refactor operation {OperationId} failed", operationId);
            return CreateErrorResult(operationId, startTime, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<BulkEditResult> MultiFileEditAsync(
        IReadOnlyList<MultiFileEditOperation> editOperations,
        BulkEditOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        options ??= new BulkEditOptions();

        _logger?.LogInformation("Starting multi-file edit operation {OperationId} with {OperationCount} operations",
            operationId, editOperations.Count);

        try
        {
            // Sort operations by priority
            var sortedOperations = editOperations.OrderBy(op => op.Priority).ToList();

            // Resolve files for all operations
            var allFiles = new HashSet<string>();
            foreach (var operation in sortedOperations)
            {
                var operationFiles = await ResolveFilePatterns(new[] { operation.FilePattern }, options, cancellationToken);
                foreach (var file in operationFiles)
                {
                    allFiles.Add(file);
                }
            }

            var fileResults = new List<FileBulkEditResult>();
            var changesApplied = 0;

            // Create rollback info if backups are enabled
            RollbackInfo? rollbackInfo = null;
            if (options.CreateBackups)
            {
                rollbackInfo = await CreateRollbackSession(operationId, allFiles.ToList(), cancellationToken);
            }

            // Process each operation in order
            foreach (var operation in sortedOperations)
            {
                var operationFiles = await ResolveFilePatterns(new[] { operation.FilePattern }, options, cancellationToken);

                var tasks = operationFiles.Select(async filePath =>
                {
                    await _processingSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        return await ProcessFileForMultiFileEdit(
                            filePath, operation, options, rollbackInfo, cancellationToken);
                    }
                    finally
                    {
                        _processingSemaphore.Release();
                    }
                });

                var results = await Task.WhenAll(tasks);
                fileResults.AddRange(results);

                if (options.StopOnFirstError && results.Any(r => !r.Success))
                {
                    break;
                }
            }

            var endTime = DateTime.UtcNow;
            changesApplied = fileResults.Sum(r => r.ChangesApplied);

            var summary = new BulkEditSummary
            {
                TotalFilesMatched = allFiles.Count,
                TotalFilesProcessed = fileResults.Count,
                SuccessfulFiles = fileResults.Count(r => r.Success),
                FailedFiles = fileResults.Count(r => !r.Success),
                SkippedFiles = fileResults.Count(r => r.Skipped),
                TotalChangesApplied = changesApplied,
                TotalBytesProcessed = fileResults.Sum(r => r.OriginalSize),
                TotalBytesWritten = fileResults.Where(r => r.Success).Sum(r => r.NewSize),
                BackupsCreated = rollbackInfo?.Files.Count(f => !string.IsNullOrEmpty(f.BackupPath)) ?? 0,
                AverageProcessingTime = fileResults.Count > 0
                    ? TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessDuration.Ticks) / fileResults.Count)
                    : TimeSpan.Zero,
                FilesPerSecond = fileResults.Count > 0 && (endTime - startTime).TotalSeconds > 0
                    ? fileResults.Count / (endTime - startTime).TotalSeconds
                    : 0
            };

            var errors = new List<BulkEditError>();
            if (summary.FailedFiles > 0 && options.StopOnFirstError)
            {
                errors.Add(new BulkEditError
                {
                    FilePath = "Multiple files",
                    ErrorMessage = $"Operation stopped due to errors in {summary.FailedFiles} files",
                    Timestamp = DateTime.UtcNow
                });
            }

            return new BulkEditResult
            {
                Success = summary.FailedFiles == 0 || !options.StopOnFirstError,
                TotalFiles = summary.TotalFilesProcessed,
                ModifiedFiles = summary.SuccessfulFiles,
                SkippedFiles = summary.SkippedFiles,
                FailedFiles = summary.FailedFiles,
                FileResults = fileResults,
                Errors = errors,
                ElapsedTime = endTime - startTime,
                Summary = summary.ToString(),
                RollbackSessionId = rollbackInfo?.RollbackId ?? string.Empty,
                OperationId = operationId,
                StartTime = startTime,
                EndTime = endTime
            };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Multi-file edit operation {OperationId} failed", operationId);
            return CreateErrorResult(operationId, startTime, ex.Message);
        }
    }
}

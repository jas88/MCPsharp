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
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Service for performing bulk edit operations across multiple files with parallel processing
/// </summary>
public sealed partial class BulkEditService : IBulkEditService, IDisposable
{
    private readonly ILogger<BulkEditService> _logger;
    private readonly string _tempDirectory;
    private readonly ConcurrentDictionary<string, RollbackInfo> _rollbackSessions;
    private readonly SemaphoreSlim _processingSemaphore;

    public BulkEditService(ILogger<BulkEditService>? logger = null)
    {
        _logger = logger ?? NullLogger<BulkEditService>.Instance;
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MCPsharp", "BulkEdit");
        _rollbackSessions = new ConcurrentDictionary<string, RollbackInfo>();
        _processingSemaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        // Ensure temp directory exists
        Directory.CreateDirectory(_tempDirectory);
    }

    /// <summary>
    /// Releases the semaphore used to throttle parallel file processing.
    /// </summary>
    public void Dispose()
    {
        _processingSemaphore.Dispose();
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

        LogStartingBulkReplaceOperationWith(_logger, operationId, regexPattern);

        // Validate regex pattern - this will throw ArgumentException for invalid patterns
        var regex = new Regex(regexPattern, options.RegexOptions, TimeSpan.FromSeconds(1));

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
                    LogBulkEditFileFailed(_logger, ex, file);
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

            // Create rollback info if backups are enabled
            var rollbackInfo = await CreateRollbackSessionIfEnabledAsync(operationId, filesToProcess, options, cancellationToken);

            fileResults.AddRange(await ProcessFilesInParallelAsync(
                filesToProcess,
                filePath => ProcessFileForBulkReplace(filePath, regex, replacement, options, rollbackInfo, cancellationToken),
                cancellationToken));

            var result = BuildBulkEditResult(operationId, startTime, filesToProcess.Count, fileResults, options, rollbackInfo);
            if (result.SummaryData is { } summary)
            {
                LogBulkReplaceOperationCompletedFiles(_logger, operationId, summary.TotalFilesProcessed, summary.SuccessfulFiles, summary.FailedFiles, summary.TotalChangesApplied);
            }

            return result;
        }
        catch (Exception ex)
        {
            LogBulkReplaceOperationFailed(_logger, ex, operationId);
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

        LogStartingConditionalEditOperationWith(_logger, operationId, condition.ConditionType);

        try
        {
            var filesToProcess = await ResolveFilePatterns(files, options, cancellationToken);
            var rollbackInfo = await CreateRollbackSessionIfEnabledAsync(operationId, filesToProcess, options, cancellationToken);

            var fileResults = await ProcessFilesInParallelAsync(
                filesToProcess,
                filePath => ProcessFileForConditionalEdit(filePath, condition, edits, options, rollbackInfo, cancellationToken),
                cancellationToken);

            return BuildBulkEditResult(operationId, startTime, filesToProcess.Count, fileResults, options, rollbackInfo);
        }
        catch (Exception ex)
        {
            LogConditionalEditOperationFailed(_logger, ex, operationId);
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

        LogStartingBatchRefactorOperationWith(_logger, operationId, refactorPattern.RefactorType);

        try
        {
            var filesToProcess = await ResolveFilePatterns(files, options, cancellationToken);
            var rollbackInfo = await CreateRollbackSessionIfEnabledAsync(operationId, filesToProcess, options, cancellationToken);

            var fileResults = await ProcessFilesInParallelAsync(
                filesToProcess,
                filePath => ProcessFileForBatchRefactor(filePath, refactorPattern, options, rollbackInfo, cancellationToken),
                cancellationToken);

            return BuildBulkEditResult(operationId, startTime, filesToProcess.Count, fileResults, options, rollbackInfo);
        }
        catch (Exception ex)
        {
            LogBatchRefactorOperationFailed(_logger, ex, operationId);
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

        LogStartingMultiFileEditOperation(_logger, operationId, editOperations.Count);

        try
        {
            // Sort operations by priority and resolve each operation's files once
            var resolvedOperations = new List<(MultiFileEditOperation Operation, IReadOnlyList<string> Files)>();
            var allFiles = new List<string>();
            var seenFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in editOperations.OrderBy(op => op.Priority))
            {
                var operationFiles = await ResolveFilePatterns(new[] { operation.FilePattern }, options, cancellationToken);
                resolvedOperations.Add((operation, operationFiles));
                allFiles.AddRange(operationFiles.Where(seenFiles.Add));
            }

            // Back up every affected file once, before any operation touches it
            var rollbackInfo = await CreateRollbackSessionIfEnabledAsync(operationId, allFiles, options, cancellationToken);

            // Process each operation in priority order
            var fileResults = new List<FileBulkEditResult>();
            foreach (var (operation, operationFiles) in resolvedOperations)
            {
                var results = await ProcessFilesInParallelAsync(
                    operationFiles,
                    filePath => ProcessFileForMultiFileEdit(filePath, operation, options, rollbackInfo, cancellationToken),
                    cancellationToken);
                fileResults.AddRange(results);

                if (options.StopOnFirstError && results.Exists(r => !r.Success))
                {
                    break;
                }
            }

            return BuildBulkEditResult(operationId, startTime, allFiles.Count, fileResults, options, rollbackInfo);
        }
        catch (Exception ex)
        {
            LogMultiFileEditOperationFailed(_logger, ex, operationId);
            return CreateErrorResult(operationId, startTime, ex.Message);
        }
    }

    /// <summary>
    /// Creates and records a rollback session for <paramref name="files"/> when backups are
    /// enabled and the operation will actually modify files (i.e. not in preview mode).
    /// </summary>
    private async Task<RollbackInfo?> CreateRollbackSessionIfEnabledAsync(
        string operationId,
        IReadOnlyList<string> files,
        BulkEditOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.CreateBackups || options.PreviewMode)
        {
            return null;
        }

        return await CreateRollbackSession(operationId, files, "BulkEdit", cancellationToken);
    }

    /// <summary>
    /// Runs <paramref name="processFile"/> over each file in parallel, throttled by the shared semaphore.
    /// </summary>
    private async Task<List<FileBulkEditResult>> ProcessFilesInParallelAsync(
        IReadOnlyList<string> files,
        Func<string, Task<FileBulkEditResult>> processFile,
        CancellationToken cancellationToken)
    {
        var tasks = files.Select(async filePath =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _processingSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await processFile(filePath);
            }
            finally
            {
                _processingSemaphore.Release();
            }
        });

        return [.. await Task.WhenAll(tasks)];
    }

    /// <summary>
    /// Builds the final <see cref="BulkEditResult"/> for a completed bulk operation, computing the
    /// summary statistics and attaching the rollback session (if any).
    /// </summary>
    private static BulkEditResult BuildBulkEditResult(
        string operationId,
        DateTime startTime,
        int totalFilesMatched,
        List<FileBulkEditResult> fileResults,
        BulkEditOptions options,
        RollbackInfo? rollbackInfo)
    {
        var endTime = DateTime.UtcNow;
        var elapsedSeconds = (endTime - startTime).TotalSeconds;

        var summary = new BulkEditSummary
        {
            TotalFilesMatched = totalFilesMatched,
            TotalFilesProcessed = fileResults.Count,
            SuccessfulFiles = fileResults.Count(r => r.Success),
            FailedFiles = fileResults.Count(r => !r.Success),
            SkippedFiles = fileResults.Count(r => r.Skipped),
            TotalChangesApplied = fileResults.Sum(r => r.ChangesApplied),
            TotalBytesProcessed = fileResults.Sum(r => r.OriginalSize),
            TotalBytesWritten = fileResults.Where(r => r.Success).Sum(r => r.NewSize),
            BackupsCreated = rollbackInfo?.Files.Count(f => !string.IsNullOrEmpty(f.BackupPath)) ?? 0,
            AverageProcessingTime = fileResults.Count > 0
                ? TimeSpan.FromTicks(fileResults.Sum(r => r.ProcessDuration.Ticks) / fileResults.Count)
                : TimeSpan.Zero,
            FilesPerSecond = fileResults.Count > 0 && elapsedSeconds > 0
                ? fileResults.Count / elapsedSeconds
                : 0
        };

        var stoppedOnError = summary.FailedFiles > 0 && options.StopOnFirstError;
        var errors = new List<BulkEditError>();
        string? error = null;
        if (stoppedOnError)
        {
            error = $"Operation stopped due to errors in {summary.FailedFiles} files";
            errors.Add(new BulkEditError
            {
                FilePath = "Multiple files",
                ErrorMessage = error,
                Timestamp = DateTime.UtcNow
            });
        }

        return new BulkEditResult
        {
            Success = !stoppedOnError,
            TotalFiles = summary.TotalFilesProcessed,
            ModifiedFiles = summary.SuccessfulFiles,
            SkippedFiles = summary.SkippedFiles,
            FailedFiles = summary.FailedFiles,
            FileResults = fileResults,
            Errors = errors,
            ElapsedTime = endTime - startTime,
            RollbackSessionId = rollbackInfo?.RollbackId ?? string.Empty,
            Summary = summary.ToString(),
            Error = error,
            OperationId = operationId,
            StartTime = startTime,
            EndTime = endTime,
            SummaryData = summary,
            RollbackInfo = rollbackInfo
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting bulk replace operation {OperationId} with pattern {Pattern}")]
    private static partial void LogStartingBulkReplaceOperationWith(ILogger logger, string operationId, string pattern);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bulk replace operation {OperationId} completed. Files: {Total}/{Successful}/{Failed}, Changes: {Changes}")]
    private static partial void LogBulkReplaceOperationCompletedFiles(ILogger logger, string operationId, int total, int successful, int failed, int changes);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bulk replace operation {OperationId} failed")]
    private static partial void LogBulkReplaceOperationFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting conditional edit operation {OperationId} with condition type {ConditionType}")]
    private static partial void LogStartingConditionalEditOperationWith(ILogger logger, string operationId, BulkConditionType conditionType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Conditional edit operation {OperationId} failed")]
    private static partial void LogConditionalEditOperationFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch refactor operation {OperationId} with refactor type {RefactorType}")]
    private static partial void LogStartingBatchRefactorOperationWith(ILogger logger, string operationId, BulkRefactorType refactorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Batch refactor operation {OperationId} failed")]
    private static partial void LogBatchRefactorOperationFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting multi-file edit operation {OperationId} with {OperationCount} operations")]
    private static partial void LogStartingMultiFileEditOperation(ILogger logger, string operationId, int operationCount);

    [LoggerMessage(Level = LogLevel.Error, Message = "Multi-file edit operation {OperationId} failed")]
    private static partial void LogMultiFileEditOperationFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bulk edit failed for file {FilePath}")]
    private static partial void LogBulkEditFileFailed(ILogger logger, Exception exception, string filePath);
}

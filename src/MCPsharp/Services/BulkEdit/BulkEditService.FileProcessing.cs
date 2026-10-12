using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// File processing methods for different bulk edit operation types
/// </summary>
public partial class BulkEditService
{
    private async Task<FileBulkEditResult> ProcessFileForBulkReplace(
        string filePath,
        Regex regex,
        string replacement,
        BulkEditOptions options,
        RollbackInfo? rollbackInfo,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var originalSize = 0L;
        var newSize = 0L;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Read file
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            originalSize = content.Length;

            cancellationToken.ThrowIfCancellationRequested();

            // Apply regex replacement
            var newContent = regex.Replace(content, replacement);
            newSize = newContent.Length;

            // Check if any changes were made
            if (content == newContent)
            {
                return new FileBulkEditResult
                {
                    FilePath = filePath,
                    Success = true,
                    ChangesApplied = 0,
                    ChangesCount = 0,
                    Skipped = true,
                    SkipReason = "No matches found",
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false
                };
            }

            // In preview mode, just report what would change
            if (options.PreviewMode)
            {
                var changes = new List<FileChange>();
                var matches = regex.Matches(content);
                foreach (Match match in matches)
                {
                    changes.Add(new FileChange
                    {
                        ChangeType = FileChangeType.Replace,
                        StartLine = GetLineNumber(content, match.Index),
                        StartColumn = GetColumnNumber(content, match.Index),
                        EndLine = GetLineNumber(content, match.Index + match.Length),
                        EndColumn = GetColumnNumber(content, match.Index + match.Length),
                        OriginalText = match.Value,
                        NewText = match.Result(replacement)
                    });
                }

                return new FileBulkEditResult
                {
                    FilePath = filePath,
                    Success = true,
                    ChangesApplied = changes.Count,
                    ChangesCount = changes.Count,
                    Changes = changes,
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false, // No backup in preview mode
                    Skipped = false
                };
            }

            // Create backup if needed and not already created
            var backupPath = rollbackInfo?.Files.FirstOrDefault(f => f.OriginalPath == filePath)?.BackupPath;
            if (options.CreateBackups && backupPath == null)
            {
                backupPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(filePath, backupPath);
            }

            // Write changes
            await File.WriteAllTextAsync(filePath, newContent, cancellationToken);

            var matchCount = regex.Count(content);
            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = true,
                ChangesApplied = matchCount,
                ChangesCount = matchCount,
                OriginalSize = originalSize,
                NewSize = newSize,
                BackupPath = backupPath,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = !string.IsNullOrEmpty(backupPath),
                Skipped = false
            };
        }
        catch (Exception ex)
        {
            LogBulkReplaceFileFailed(BulkEditLog, ex, filePath);
            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message,
                ChangesApplied = 0,
                ChangesCount = 0,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false,
                Skipped = false
            };
        }
    }

    private async Task<FileBulkEditResult> ProcessFileForConditionalEdit(
        string filePath,
        BulkEditCondition condition,
        IReadOnlyList<TextEdit> edits,
        BulkEditOptions options,
        RollbackInfo? rollbackInfo,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var originalSize = 0L;
        var newSize = 0L;

        try
        {
            // Read file
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            originalSize = content.Length;

            // Check condition
            var conditionMet = CheckCondition(content, condition, filePath);

            if (!conditionMet)
            {
                return new FileBulkEditResult
                {
                    FilePath = filePath,
                    Success = false,
                    ChangesApplied = 0,
                    ChangesCount = 0,
                    Skipped = true,
                    SkipReason = "Condition not met",
                    OriginalSize = originalSize,
                    NewSize = originalSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false
                };
            }

            // Apply edits (in preview mode, just track changes)
            var newContent = content;
            var changes = new List<FileChange>();

            if (!options.PreviewMode)
            {
                // Create backup if needed
                var backupPath = rollbackInfo?.Files.FirstOrDefault(f => f.OriginalPath == filePath)?.BackupPath;
                if (options.CreateBackups && backupPath == null)
                {
                    backupPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                    File.Copy(filePath, backupPath);
                }
            }

            // Apply each edit at its line/column position
            newContent = ApplyTextEdits(newContent, edits, changes, cancellationToken);

            newSize = newContent.Length;

            if (!options.PreviewMode)
            {
                await File.WriteAllTextAsync(filePath, newContent, cancellationToken);
            }

            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = true,
                ChangesApplied = changes.Count,
                ChangesCount = changes.Count,
                Changes = changes,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false,
                Skipped = false
            };
        }
        catch (Exception ex)
        {
            LogConditionalEditFileFailed(BulkEditLog, ex, filePath);
            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message,
                ChangesApplied = 0,
                ChangesCount = 0,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false,
                Skipped = false
            };
        }
    }

    private async Task<FileBulkEditResult> ProcessFileForBatchRefactor(
        string filePath,
        BulkRefactorPattern refactorPattern,
        BulkEditOptions options,
        RollbackInfo? rollbackInfo,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var originalSize = 0L;
        var newSize = 0L;

        try
        {
            // Read file
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            originalSize = content.Length;

            // Apply refactoring based on type
            var newContent = ApplyRefactoring(content, refactorPattern);
            newSize = newContent.Length;

            // Check if any changes were made
            if (content == newContent)
            {
                return new FileBulkEditResult
                {
                    FilePath = filePath,
                    Success = true,
                    ChangesApplied = 0,
                    ChangesCount = 0,
                    Skipped = true,
                    SkipReason = "No refactoring needed",
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false
                };
            }

            if (options.PreviewMode)
            {
                return new FileBulkEditResult
                {
                    FilePath = filePath,
                    Success = true,
                    ChangesApplied = 1,
                    ChangesCount = 1,
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false,
                    Skipped = false
                };
            }

            // Create backup if needed
            var backupPath = rollbackInfo?.Files.FirstOrDefault(f => f.OriginalPath == filePath)?.BackupPath;
            if (options.CreateBackups && backupPath == null)
            {
                backupPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(filePath, backupPath);
            }

            // Write changes
            await File.WriteAllTextAsync(filePath, newContent, cancellationToken);

            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = true,
                ChangesApplied = 1,
                ChangesCount = 1,
                OriginalSize = originalSize,
                NewSize = newSize,
                BackupPath = backupPath,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = !string.IsNullOrEmpty(backupPath),
                Skipped = false
            };
        }
        catch (Exception ex)
        {
            LogBatchRefactorFileFailed(BulkEditLog, ex, filePath);
            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message,
                ChangesApplied = 0,
                ChangesCount = 0,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false,
                Skipped = false
            };
        }
    }

    private async Task<FileBulkEditResult> ProcessFileForMultiFileEdit(
        string filePath,
        MultiFileEditOperation operation,
        BulkEditOptions options,
        RollbackInfo? rollbackInfo,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var originalSize = 0L;
        var newSize = 0L;

        try
        {
            // Read file
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            originalSize = content.Length;

            // Apply edits from the operation
            var newContent = content;
            var changes = new List<FileChange>();

            newContent = ApplyTextEdits(newContent, operation.Edits, changes, cancellationToken);

            newSize = newContent.Length;

            if (options.PreviewMode)
            {
                return new FileBulkEditResult
                {
                    FilePath = filePath,
                    Success = true,
                    ChangesApplied = operation.Edits.Count,
                    ChangesCount = operation.Edits.Count,
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    ProcessStartTime = startTime,
                    ProcessEndTime = DateTime.UtcNow,
                    EditDuration = DateTime.UtcNow - startTime,
                    BackupCreated = false,
                    Skipped = false
                };
            }

            // Create backup if needed
            var backupPath = rollbackInfo?.Files.FirstOrDefault(f => f.OriginalPath == filePath)?.BackupPath;
            if (options.CreateBackups && backupPath == null)
            {
                backupPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                File.Copy(filePath, backupPath);
            }

            // Write changes
            await File.WriteAllTextAsync(filePath, newContent, cancellationToken);

            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = true,
                ChangesApplied = operation.Edits.Count,
                ChangesCount = operation.Edits.Count,
                OriginalSize = originalSize,
                NewSize = newSize,
                BackupPath = backupPath,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = !string.IsNullOrEmpty(backupPath),
                Skipped = false
            };
        }
        catch (Exception ex)
        {
            LogMultiFileEditFileFailed(BulkEditLog, ex, filePath);
            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = false,
                ErrorMessage = ex.Message,
                ChangesApplied = 0,
                ChangesCount = 0,
                OriginalSize = originalSize,
                NewSize = newSize,
                ProcessStartTime = startTime,
                ProcessEndTime = DateTime.UtcNow,
                EditDuration = DateTime.UtcNow - startTime,
                BackupCreated = false,
                Skipped = false
            };
        }
    }
}

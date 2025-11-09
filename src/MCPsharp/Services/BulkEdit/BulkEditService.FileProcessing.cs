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
                    BackupCreated = false,
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

            return new FileBulkEditResult
            {
                FilePath = filePath,
                Success = true,
                ChangesApplied = regex.Matches(content).Count,
                ChangesCount = regex.Matches(content).Count,
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
            _logger?.LogError(ex, "Failed to process file {FilePath} for bulk replace", filePath);
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
            var conditionMet = await CheckCondition(content, condition, filePath);

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

            // Apply each edit
            foreach (var edit in edits)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (edit)
                {
                    case ReplaceEdit replaceEdit:
                        if (replaceEdit.StartLine == 1 && replaceEdit.StartColumn == 1)
                        {
                            newContent = replaceEdit.NewText + newContent;
                        }
                        else
                        {
                            newContent = replaceEdit.NewText + newContent;
                        }
                        changes.Add(new FileChange
                        {
                            ChangeType = FileChangeType.Insert,
                            StartLine = replaceEdit.StartLine,
                            StartColumn = replaceEdit.StartColumn,
                            EndLine = replaceEdit.EndLine,
                            EndColumn = replaceEdit.EndColumn,
                            NewText = replaceEdit.NewText
                        });
                        break;
                    case InsertEdit insertEdit:
                        newContent = insertEdit.NewText + newContent;
                        changes.Add(new FileChange
                        {
                            ChangeType = FileChangeType.Insert,
                            StartLine = insertEdit.StartLine,
                            StartColumn = insertEdit.StartColumn,
                            EndLine = insertEdit.StartLine,
                            EndColumn = insertEdit.StartColumn,
                            NewText = insertEdit.NewText
                        });
                        break;
                    case DeleteEdit deleteEdit:
                        var lines = newContent.Split('\n').ToList();
                        if (deleteEdit.StartLine <= lines.Count)
                        {
                            lines.RemoveAt(deleteEdit.StartLine - 1);
                            newContent = string.Join('\n', lines);
                        }
                        changes.Add(new FileChange
                        {
                            ChangeType = FileChangeType.Delete,
                            StartLine = deleteEdit.StartLine,
                            StartColumn = deleteEdit.StartColumn,
                            EndLine = deleteEdit.EndLine,
                            EndColumn = deleteEdit.EndColumn
                        });
                        break;
                }
            }

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
            _logger?.LogError(ex, "Failed to process file {FilePath} for conditional edit", filePath);
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
            var newContent = await ApplyRefactoring(content, refactorPattern, filePath, cancellationToken);
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
            _logger?.LogError(ex, "Failed to process file {FilePath} for batch refactor", filePath);
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

            // Apply edits
            var newContent = content;

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
            _logger?.LogError(ex, "Failed to process file {FilePath} for multi-file edit", filePath);
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

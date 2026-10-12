using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Helper methods for file resolution, condition checking, and utility functions
/// </summary>
public sealed partial class BulkEditService
{
    /// <summary>
    /// Timeout applied to user-supplied regular expressions evaluated by bulk edit helpers.
    /// </summary>
    private static readonly TimeSpan BulkEditRegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Non-null logger for source-generated log methods (falls back to a no-op logger).
    /// </summary>
    private ILogger BulkEditLog => (ILogger?)_logger ?? NullLogger.Instance;

    private Task<IReadOnlyList<string>> ResolveFilePatterns(
        IReadOnlyList<string> patterns,
        BulkEditOptions options,
        CancellationToken cancellationToken)
    {
        var allFiles = new HashSet<string>();

        foreach (var pattern in patterns)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(pattern))
            {
                allFiles.Add(Path.GetFullPath(pattern));
                continue;
            }

            if (Directory.Exists(pattern))
            {
                var files = Directory.GetFiles(pattern, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (options.IncludeHiddenFiles || !IsHiddenFile(file))
                    {
                        allFiles.Add(Path.GetFullPath(file));
                    }
                }
                continue;
            }

            // Use glob pattern matching
            try
            {
                var matcher = new Matcher();
                matcher.AddInclude(pattern);

                if (options.ExcludedFiles != null)
                {
                    foreach (var excludePattern in options.ExcludedFiles)
                    {
                        matcher.AddExclude(excludePattern);
                    }
                }

                // Determine the root directory for the pattern
                var rootDir = DetermineRootDirectory(pattern);
                var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(rootDir)));

                foreach (var file in result.Files)
                {
                    var fullPath = Path.Combine(rootDir, file.Path);
                    if (File.Exists(fullPath) &&
                        (options.IncludeHiddenFiles || !IsHiddenFile(fullPath)))
                    {
                        allFiles.Add(Path.GetFullPath(fullPath));
                    }
                }
            }
            catch (Exception ex)
            {
                LogResolvePatternFailed(BulkEditLog, ex, pattern);
            }
        }

        // Filter by file size if specified
        if (options.MaxFileSize > 0)
        {
            var filteredFiles = new List<string>();
            foreach (var file in allFiles)
            {
                try
                {
                    var fileInfo = new System.IO.FileInfo(file);
                    if (fileInfo.Length <= options.MaxFileSize)
                    {
                        filteredFiles.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    LogCheckFileSizeFailed(BulkEditLog, ex, file);
                }
            }
            return Task.FromResult<IReadOnlyList<string>>(filteredFiles);
        }

        return Task.FromResult<IReadOnlyList<string>>(allFiles.ToList());
    }

    private static string DetermineRootDirectory(string pattern)
    {
        if (Path.IsPathRooted(pattern))
        {
            var rootedPath = Path.GetFullPath(pattern);
            if (File.Exists(rootedPath) || Directory.Exists(rootedPath))
            {
                return rootedPath;
            }

            // For rooted patterns with wildcards, find the deepest existing directory
            var directory = Path.GetDirectoryName(rootedPath);
            while (directory != null && !Directory.Exists(directory))
            {
                directory = Path.GetDirectoryName(directory);
            }
            return directory ?? Directory.GetCurrentDirectory();
        }

        return Directory.GetCurrentDirectory();
    }

    private static bool IsHiddenFile(string filePath)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (OperatingSystem.IsWindows())
            {
                return (fileInfo.Attributes & FileAttributes.Hidden) != 0;
            }

            // On Unix-like systems, files starting with . are considered hidden
            return Path.GetFileName(filePath).StartsWith('.');
        }
        catch (IOException)
        {
            // Attributes unavailable (file removed or locked): treat as not hidden
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static bool CheckCondition(string content, BulkEditCondition condition, string filePath)
    {
        var result = condition.ConditionType switch
        {
            BulkConditionType.FileContains => content.Contains(condition.Pattern, StringComparison.Ordinal),
            BulkConditionType.FileMatches => Regex.IsMatch(content, condition.Pattern, RegexOptions.None, BulkEditRegexTimeout),
            BulkConditionType.FileSize => CheckFileSizeCondition(filePath, condition),
            BulkConditionType.FileModifiedAfter => CheckFileModifiedCondition(filePath, condition),
            BulkConditionType.FileExtension => CheckFileExtensionCondition(filePath, condition),
            BulkConditionType.FileInDirectory => CheckFileInDirectoryCondition(filePath, condition),
            _ => false
        };

        return condition.Negate ? !result : result;
    }

    private static bool CheckFileSizeCondition(string filePath, BulkEditCondition condition)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (condition.Parameters?.TryGetValue("minSize", out var minSizeObj) == true &&
                long.TryParse(minSizeObj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var minSize) &&
                fileInfo.Length < minSize)
            {
                return false;
            }

            if (condition.Parameters?.TryGetValue("maxSize", out var maxSizeObj) == true &&
                long.TryParse(maxSizeObj.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSize) &&
                fileInfo.Length > maxSize)
            {
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            // File missing or inaccessible: condition cannot be met
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CheckFileModifiedCondition(string filePath, BulkEditCondition condition)
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(filePath);
            if (condition.Parameters?.TryGetValue("date", out var dateObj) == true &&
                DateTime.TryParse(dateObj.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return fileInfo.LastWriteTime > date;
            }
            return false;
        }
        catch (IOException)
        {
            // File missing or inaccessible: condition cannot be met
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CheckFileExtensionCondition(string filePath, BulkEditCondition condition)
    {
        var extension = Path.GetExtension(filePath);
        return extension.Equals(condition.Pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CheckFileInDirectoryCondition(string filePath, BulkEditCondition condition)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) &&
               directory.Contains(condition.Pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Apply a text-level refactoring. Symbol renames are applied as whole-word replacements;
    /// other refactoring types require semantic analysis and leave the content unchanged.
    /// </summary>
    private static string ApplyRefactoring(string content, BulkRefactorPattern refactorPattern)
    {
        if (refactorPattern.RefactorType != BulkRefactorType.RenameSymbol ||
            string.IsNullOrEmpty(refactorPattern.TargetPattern))
        {
            return content;
        }

        var wholeWord = $@"\b{Regex.Escape(refactorPattern.TargetPattern)}\b";
        return Regex.Replace(
            content,
            wholeWord,
            refactorPattern.ReplacementPattern.Replace("$", "$$", StringComparison.Ordinal),
            RegexOptions.None,
            BulkEditRegexTimeout);
    }

    /// <summary>
    /// Apply line/column (0-indexed) text edits to content. Edits are applied from the end of the
    /// document backwards so earlier positions stay valid. Positions beyond the end of a line or of
    /// the document are clamped to the nearest valid position (LSP semantics).
    /// </summary>
    private static string ApplyTextEdits(
        string content,
        IEnumerable<TextEdit> edits,
        List<FileChange> changes,
        CancellationToken cancellationToken)
    {
        var editList = edits.ToList();
        var lines = content.Split('\n').ToList();

        foreach (var edit in editList.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (startLine, startColumn) = ClampPosition(lines, edit.StartLine, edit.StartColumn);
            var (endLine, endColumn) = edit is InsertEdit
                ? (startLine, startColumn)
                : ClampPosition(lines, edit.EndLine, edit.EndColumn);

            if (endLine < startLine || (endLine == startLine && endColumn < startColumn))
            {
                (endLine, endColumn) = (startLine, startColumn);
            }

            var newText = edit is DeleteEdit ? string.Empty : edit.NewText;
            var merged = lines[startLine][..startColumn] + newText + lines[endLine][endColumn..];

            lines.RemoveRange(startLine, endLine - startLine + 1);
            lines.InsertRange(startLine, merged.Split('\n'));
        }

        changes.AddRange(editList.Select(ConvertTextEditToFileChange));
        return string.Join('\n', lines);
    }

    private static (int Line, int Column) ClampPosition(List<string> lines, int line, int column)
    {
        if (line >= lines.Count)
        {
            var last = lines.Count - 1;
            return (last, lines[last].Length);
        }

        var clampedLine = Math.Max(0, line);
        return (clampedLine, Math.Clamp(column, 0, lines[clampedLine].Length));
    }

    private static FileChange ConvertTextEditToFileChange(TextEdit textEdit)
    {
        return textEdit switch
        {
            ReplaceEdit replace => new FileChange
            {
                ChangeType = FileChangeType.Replace,
                StartLine = replace.StartLine,
                StartColumn = replace.StartColumn,
                EndLine = replace.EndLine,
                EndColumn = replace.EndColumn,
                NewText = replace.NewText
            },
            InsertEdit insert => new FileChange
            {
                ChangeType = FileChangeType.Insert,
                StartLine = insert.StartLine,
                StartColumn = insert.StartColumn,
                EndLine = insert.StartLine,
                EndColumn = insert.StartColumn,
                NewText = insert.NewText
            },
            DeleteEdit delete => new FileChange
            {
                ChangeType = FileChangeType.Delete,
                StartLine = delete.StartLine,
                StartColumn = delete.StartColumn,
                EndLine = delete.EndLine,
                EndColumn = delete.EndColumn
            },
            _ => throw new ArgumentException($"Unknown edit type: {textEdit.GetType()}")
        };
    }

    private static string GenerateDiff(List<FileChange> changes)
    {
        // Simple diff generation - in a real implementation, you'd use a proper diff library
        var diffLines = new List<string> { "--- Original", "+++ Modified" };

        foreach (var change in changes.Take(10)) // Limit preview size
        {
            diffLines.Add($"@@ -{change.StartLine},{change.EndLine - change.StartLine + 1} " +
                         $"+{change.StartLine},{change.EndLine - change.StartLine + 1} @@");

            if (!string.IsNullOrEmpty(change.OriginalText))
            {
                diffLines.Add($"-{change.OriginalText}");
            }

            if (!string.IsNullOrEmpty(change.NewText))
            {
                diffLines.Add($"+{change.NewText}");
            }
        }

        if (changes.Count > 10)
        {
            diffLines.Add($"... and {changes.Count - 10} more changes");
        }

        return string.Join("\n", diffLines);
    }

    private static int GetLineNumber(string content, int index)
    {
        var lineCount = 1;
        for (int i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineCount++;
            }
        }
        return lineCount - 1; // 0-indexed
    }

    private static int GetColumnNumber(string content, int index)
    {
        var column = 0;
        for (int i = index - 1; i >= 0; i--)
        {
            if (content[i] == '\n')
            {
                break;
            }
            column++;
        }
        return column;
    }

    /// <summary>
    /// Render bulk edit statistics as a human-readable summary line.
    /// </summary>
    private static string FormatBulkEditSummary(BulkEditSummary summary)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"Processed {summary.TotalFilesProcessed} of {summary.TotalFilesMatched} files: " +
            $"{summary.SuccessfulFiles} succeeded, {summary.FailedFiles} failed, {summary.SkippedFiles} skipped, " +
            $"{summary.TotalChangesApplied} changes applied");
    }

    /// <summary>
    /// Render preview statistics as a human-readable summary line.
    /// </summary>
    private static string FormatPreviewSummary(PreviewSummary summary)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{summary.FilesToChange} of {summary.TotalFiles} files would change ({summary.FilesToSkip} skipped): " +
            $"{summary.TotalChanges} changes, +{summary.LinesAdded}/-{summary.LinesRemoved} lines, " +
            $"{summary.SizeChange:+#;-#;0} characters");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to resolve pattern {Pattern}")]
    private static partial void LogResolvePatternFailed(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to check file size for {FilePath}")]
    private static partial void LogCheckFileSizeFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generating preview {PreviewId} for bulk edit operation")]
    private static partial void LogGeneratingPreview(ILogger logger, string previewId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to generate preview {PreviewId}")]
    private static partial void LogPreviewFailed(ILogger logger, Exception exception, string previewId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to generate preview for file {FilePath}")]
    private static partial void LogFilePreviewFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for bulk replace")]
    private static partial void LogBulkReplaceFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for conditional edit")]
    private static partial void LogConditionalEditFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for batch refactor")]
    private static partial void LogBatchRefactorFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to process file {FilePath} for multi-file edit")]
    private static partial void LogMultiFileEditFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid file pattern {Pattern}")]
    private static partial void LogInvalidFilePattern(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid regex pattern {Pattern}")]
    private static partial void LogInvalidRegexPattern(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bulk edit validation failed")]
    private static partial void LogBulkEditValidationFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to estimate impact")]
    private static partial void LogEstimateImpactFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to analyze file {FilePath}")]
    private static partial void LogAnalyzeFileFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get file statistics")]
    private static partial void LogFileStatisticsFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting rollback operation {OperationId} for rollback session {RollbackId}")]
    private static partial void LogStartingRollback(ILogger logger, string operationId, string rollbackId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to cleanup rollback directory {Directory}")]
    private static partial void LogRollbackDirectoryCleanupFailed(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rollback operation {OperationId} failed")]
    private static partial void LogRollbackFailed(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to get available rollbacks")]
    private static partial void LogGetRollbacksFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to cleanup expired rollback {RollbackId}")]
    private static partial void LogExpiredRollbackCleanupFailed(ILogger logger, Exception exception, string rollbackId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} expired rollback sessions")]
    private static partial void LogExpiredRollbacksCleaned(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create backup for file {FilePath}")]
    private static partial void LogBackupFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rollback file {FilePath}")]
    private static partial void LogRollbackFileFailed(ILogger logger, Exception exception, string filePath);

    private static BulkEditResult CreateErrorResult(string operationId, DateTime startTime, string error)
    {
        var errors = new List<BulkEditError>
        {
            new BulkEditError
            {
                FilePath = "Error occurred",
                ErrorMessage = error,
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
            Summary = $"Operation failed: {error}",
            Error = error,
            OperationId = operationId,
            StartTime = startTime,
            EndTime = DateTime.UtcNow
        };
    }
}

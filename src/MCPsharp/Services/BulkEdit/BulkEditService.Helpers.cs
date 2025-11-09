using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Helper methods for file resolution, condition checking, and utility functions
/// </summary>
public partial class BulkEditService
{
    private async Task<IReadOnlyList<string>> ResolveFilePatterns(
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
                _logger?.LogWarning(ex, "Failed to resolve pattern {Pattern}", pattern);
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
                    _logger?.LogWarning(ex, "Failed to check file size for {FilePath}", file);
                }
            }
            return filteredFiles;
        }

        return allFiles.ToList();
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
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckCondition(string content, BulkEditCondition condition, string filePath)
    {
        var result = condition.ConditionType switch
        {
            BulkConditionType.FileContains => content.Contains(condition.Pattern),
            BulkConditionType.FileMatches => Regex.IsMatch(content, condition.Pattern),
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
                long.TryParse(minSizeObj.ToString(), out var minSize))
            {
                if (fileInfo.Length < minSize) return false;
            }

            if (condition.Parameters?.TryGetValue("maxSize", out var maxSizeObj) == true &&
                long.TryParse(maxSizeObj.ToString(), out var maxSize))
            {
                if (fileInfo.Length > maxSize) return false;
            }

            return true;
        }
        catch
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
                DateTime.TryParse(dateObj.ToString(), out var date))
            {
                return fileInfo.LastWriteTime > date;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool CheckFileExtensionCondition(string filePath, BulkEditCondition condition)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension.Equals(condition.Pattern.ToLowerInvariant());
    }

    private static bool CheckFileInDirectoryCondition(string filePath, BulkEditCondition condition)
    {
        var directory = Path.GetDirectoryName(filePath);
        return !string.IsNullOrEmpty(directory) &&
               directory.Contains(condition.Pattern, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ApplyRefactoring(
        string content,
        BulkRefactorPattern refactorPattern,
        string filePath,
        CancellationToken cancellationToken)
    {
        // This would implement specific refactoring logic based on the pattern type
        // For now, return the original content
        return content;
    }

    private FileChange ConvertTextEditToFileChange(TextEdit textEdit)
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

    private static string GenerateDiff(string originalContent, IReadOnlyList<FileChange> changes)
    {
        // Simple diff generation - in a real implementation, you'd use a proper diff library
        var diffLines = new List<string>();
        diffLines.Add("--- Original");
        diffLines.Add("+++ Modified");

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

    private BulkEditResult CreateErrorResult(string operationId, DateTime startTime, string error)
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

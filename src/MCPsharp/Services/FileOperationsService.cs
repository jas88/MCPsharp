using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for file operations within a project context
/// </summary>
public class FileOperationsService
{
    private readonly string _rootPath;

    public FileOperationsService(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root path does not exist: {rootPath}");
        }

        _rootPath = Path.GetFullPath(rootPath);
    }

    /// <summary>
    /// List files in the project, optionally filtered by glob pattern
    /// </summary>
    /// <param name="pattern">Glob pattern (e.g., "**/*.cs", "src/**/*.csproj")</param>
    /// <param name="includeHidden">Whether to include hidden files</param>
    public FileListResult ListFiles(string? pattern = null, bool includeHidden = false)
    {
        var files = new List<Models.FileInfo>();

        if (pattern != null)
        {
            // Use glob pattern matching
            var matcher = new Matcher();
            matcher.AddInclude(pattern);

            var result = matcher.Execute(new DirectoryInfoWrapper(new DirectoryInfo(_rootPath)));

            foreach (var file in result.Files)
            {
                var fullPath = Path.Combine(_rootPath, file.Path);
                var fileInfo = new System.IO.FileInfo(fullPath);

                if (!includeHidden && IsHidden(fileInfo))
                    continue;

                files.Add(new Models.FileInfo
                {
                    Path = fullPath,
                    RelativePath = file.Path,
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    IsHidden = IsHidden(fileInfo)
                });
            }
        }
        else
        {
            // List all files recursively
            foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
            {
                var fileInfo = new System.IO.FileInfo(file);

                if (!includeHidden && IsHidden(fileInfo))
                    continue;

                files.Add(new Models.FileInfo
                {
                    Path = file,
                    RelativePath = Path.GetRelativePath(_rootPath, file),
                    Size = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    IsHidden = IsHidden(fileInfo)
                });
            }
        }

        return new FileListResult
        {
            Files = files,
            TotalFiles = files.Count,
            Pattern = pattern
        };
    }

    /// <summary>
    /// Read a file's contents as text
    /// </summary>
    public async Task<FileReadResult> ReadFileAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);

        if (!ValidatePath(fullPath))
        {
            return new FileReadResult
            {
                Success = false,
                Error = "Path is outside project root",
                Path = relativePath
            };
        }

        if (!File.Exists(fullPath))
        {
            return new FileReadResult
            {
                Success = false,
                Error = "File not found",
                Path = relativePath
            };
        }

        try
        {
            // Detect encoding by reading BOM
            var encoding = DetectEncoding(fullPath);
            var content = await File.ReadAllTextAsync(fullPath, encoding, ct);
            var lineCount = content.Split('\n').Length;

            var fileInfo = new System.IO.FileInfo(fullPath);

            return new FileReadResult
            {
                Success = true,
                Path = relativePath,
                Content = content,
                Encoding = encoding.WebName,
                LineCount = lineCount,
                Size = fileInfo.Length
            };
        }
        catch (Exception ex)
        {
            return new FileReadResult
            {
                Success = false,
                Error = $"Failed to read file: {ex.Message}",
                Path = relativePath
            };
        }
    }

    /// <summary>
    /// Write content to a file (creates if doesn't exist, overwrites if exists)
    /// </summary>
    public async Task<FileWriteResult> WriteFileAsync(
        string relativePath,
        string content,
        bool createDirectories = true,
        CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);

        if (!ValidatePath(fullPath))
        {
            return new FileWriteResult
            {
                Success = false,
                Error = "Path is outside project root",
                Path = relativePath
            };
        }

        try
        {
            var existed = File.Exists(fullPath);

            if (createDirectories)
            {
                var directory = Path.GetDirectoryName(fullPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, ct);

            var fileInfo = new System.IO.FileInfo(fullPath);

            return new FileWriteResult
            {
                Success = true,
                Path = relativePath,
                BytesWritten = fileInfo.Length,
                Created = !existed
            };
        }
        catch (Exception ex)
        {
            return new FileWriteResult
            {
                Success = false,
                Error = $"Failed to write file: {ex.Message}",
                Path = relativePath
            };
        }
    }

    /// <summary>
    /// Apply multiple text edits to a file
    /// </summary>
    public async Task<FileEditResult> EditFileAsync(
        string relativePath,
        IEnumerable<TextEdit> edits,
        CancellationToken ct = default)
    {
        var fullPath = GetFullPath(relativePath);

        if (!ValidatePath(fullPath))
        {
            return new FileEditResult
            {
                Success = false,
                Error = "Path is outside project root",
                Path = relativePath
            };
        }

        if (!File.Exists(fullPath))
        {
            return new FileEditResult
            {
                Success = false,
                Error = "File not found",
                Path = relativePath
            };
        }

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, ct);
            var lines = content.Split('\n').ToList();

            // Sort edits by position (descending) so we can apply them without affecting offsets
            var sortedEdits = edits.OrderByDescending(e => GetEditStartPosition(e)).ToList();

            foreach (var edit in sortedEdits)
            {
                ApplyEdit(lines, edit);
            }

            var newContent = string.Join('\n', lines);
            await File.WriteAllTextAsync(fullPath, newContent, Encoding.UTF8, ct);

            return new FileEditResult
            {
                Success = true,
                Path = relativePath,
                EditsApplied = sortedEdits.Count,
                NewContent = newContent
            };
        }
        catch (Exception ex)
        {
            return new FileEditResult
            {
                Success = false,
                Error = $"Failed to edit file: {ex.Message}",
                Path = relativePath
            };
        }
    }

    private void ApplyEdit(List<string> lines, TextEdit edit)
    {
        switch (edit)
        {
            case ReplaceEdit replace:
                ApplyReplaceEdit(lines, replace);
                break;
            case InsertEdit insert:
                ApplyInsertEdit(lines, insert);
                break;
            case DeleteEdit delete:
                ApplyDeleteEdit(lines, delete);
                break;
            default:
                throw new ArgumentException($"Unknown edit type: {edit.GetType().Name}");
        }
    }

    private void ApplyReplaceEdit(List<string> lines, ReplaceEdit edit)
    {
        if (edit.StartLine == edit.EndLine)
        {
            // Single line replace
            var line = lines[edit.StartLine];
            var before = line[..edit.StartColumn];
            var after = line[edit.EndColumn..];
            lines[edit.StartLine] = before + edit.NewText + after;
        }
        else
        {
            // Multi-line replace
            var firstLine = lines[edit.StartLine][..edit.StartColumn];
            var lastLine = lines[edit.EndLine][edit.EndColumn..];

            // Remove lines in between
            lines.RemoveRange(edit.StartLine, edit.EndLine - edit.StartLine + 1);

            // Insert new content
            var newLines = edit.NewText.Split('\n');
            if (newLines.Length == 1)
            {
                lines.Insert(edit.StartLine, firstLine + newLines[0] + lastLine);
            }
            else
            {
                lines.Insert(edit.StartLine, firstLine + newLines[0]);
                for (int i = 1; i < newLines.Length - 1; i++)
                {
                    lines.Insert(edit.StartLine + i, newLines[i]);
                }
                lines.Insert(edit.StartLine + newLines.Length - 1, newLines[^1] + lastLine);
            }
        }
    }

    private void ApplyInsertEdit(List<string> lines, InsertEdit edit)
    {
        var line = lines[edit.StartLine];
        var before = line[..edit.StartColumn];
        var after = line[edit.StartColumn..];

        var newLines = edit.NewText.Split('\n');
        if (newLines.Length == 1)
        {
            lines[edit.StartLine] = before + newLines[0] + after;
        }
        else
        {
            lines[edit.StartLine] = before + newLines[0];
            for (int i = 1; i < newLines.Length - 1; i++)
            {
                lines.Insert(edit.StartLine + i, newLines[i]);
            }
            lines.Insert(edit.StartLine + newLines.Length - 1, newLines[^1] + after);
        }
    }

    private void ApplyDeleteEdit(List<string> lines, DeleteEdit edit)
    {
        if (edit.StartLine == edit.EndLine)
        {
            // Single line delete
            var line = lines[edit.StartLine];
            var before = line[..edit.StartColumn];
            var after = line[edit.EndColumn..];
            lines[edit.StartLine] = before + after;
        }
        else
        {
            // Multi-line delete
            var firstLine = lines[edit.StartLine][..edit.StartColumn];
            var lastLine = lines[edit.EndLine][edit.EndColumn..];

            lines.RemoveRange(edit.StartLine, edit.EndLine - edit.StartLine + 1);
            lines.Insert(edit.StartLine, firstLine + lastLine);
        }
    }

    
    private (int line, int column) GetEditStartPosition(TextEdit edit)
    {
        return edit switch
        {
            ReplaceEdit replace => (replace.StartLine, replace.StartColumn),
            InsertEdit insert => (insert.Line, insert.Column),
            DeleteEdit delete => (delete.StartLine, delete.StartColumn),
            _ => throw new ArgumentException($"Unknown edit type: {edit.GetType().Name}")
        };
    }

    private string GetFullPath(string relativePath)
    {
        // Handle both relative and absolute paths
        if (Path.IsPathRooted(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        return Path.GetFullPath(Path.Combine(_rootPath, relativePath));
    }

    private bool ValidatePath(string fullPath)
    {
        // Ensure path is within project root
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static Encoding DetectEncoding(string path)
    {
        // Read first few bytes to check for BOM
        using var reader = new StreamReader(path, true);
        reader.Peek(); // Trigger BOM detection
        return reader.CurrentEncoding;
    }

    private static bool IsHidden(System.IO.FileInfo fileInfo)
    {
        return fileInfo.Attributes.HasFlag(FileAttributes.Hidden) ||
               fileInfo.Name.StartsWith('.');
    }
}

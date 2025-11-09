using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services;

/// <summary>
/// Service for file operations within a project context
/// </summary>
public class FileOperationsService
{
    private readonly string _rootPath;
    private readonly RoslynWorkspace? _workspace;
    private readonly ILogger<FileOperationsService>? _logger;

    public FileOperationsService(string rootPath, RoslynWorkspace? workspace = null, ILogger<FileOperationsService>? logger = null)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root path does not exist: {rootPath}");
        }

        _rootPath = Path.GetFullPath(rootPath);
        _workspace = workspace;
        _logger = logger;
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

            // Update Roslyn workspace incrementally if this is a C# file
            if (_workspace != null && Path.GetExtension(relativePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    // AddDocumentAsync handles both new and existing documents
                    await _workspace.AddDocumentAsync(fullPath);
                }
                catch (Exception workspaceEx)
                {
                    // Log workspace update error but don't fail the file operation
                    _logger?.LogWarning("Failed to update Roslyn workspace for {RelativePath}: {ErrorMessage}", relativePath, workspaceEx.Message);
                }
            }

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

            // Update Roslyn workspace incrementally if this is a C# file
            if (_workspace != null && Path.GetExtension(relativePath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _workspace.UpdateDocumentAsync(fullPath, newContent, ct);
                }
                catch (Exception workspaceEx)
                {
                    // Log workspace update error but don't fail the file operation
                    _logger?.LogWarning("Failed to update Roslyn workspace for {RelativePath}: {ErrorMessage}", relativePath, workspaceEx.Message);
                }
            }

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
            // Provide more detailed error message for validation errors
            var errorMessage = ex.Message;
            
            if (ex is ArgumentOutOfRangeException || ex is ArgumentException)
            {
                // Add context for validation errors
                errorMessage = $"Invalid edit position: {ex.Message} " +
                             "Remember: MCPsharp uses 0-based indexing for both lines and columns. " +
                             "Column 0 is before the first character.";
            }
            else
            {
                errorMessage = $"Failed to edit file: {ex.Message}";
            }

            return new FileEditResult
            {
                Success = false,
                Error = errorMessage,
                Path = relativePath
            };
        }
    }

    /// <summary>
    /// Validate that edit positions are within bounds
    /// </summary>
    private void ValidateEditPosition(List<string> lines, TextEdit edit)
    {
        var (startLine, startColumn, endLine, endColumn) = GetEditBounds(edit);
        
        // Validate line indices
        if (startLine < 0 || startLine >= lines.Count)
            throw new ArgumentOutOfRangeException(nameof(startLine), 
                $"StartLine {startLine} is out of range. File has {lines.Count} lines (0-{lines.Count - 1}).");
                
        if (endLine < 0 || endLine >= lines.Count)
            throw new ArgumentOutOfRangeException(nameof(endLine), 
                $"EndLine {endLine} is out of range. File has {lines.Count} lines (0-{lines.Count - 1}).");
        
        // Get line lengths for validation
        var startLineLength = lines[startLine].Length;
        var endLineLength = lines[endLine].Length;
        
        // Validate column indices
        if (startColumn < 0 || startColumn > startLineLength)
            throw new ArgumentOutOfRangeException(nameof(startColumn),
                $"StartColumn {startColumn} is out of range for line {startLine}. " +
                $"Line {startLine} has {startLineLength} characters. Valid positions: 0-{startLineLength - 1} for replacements, 0-{startLineLength} for insertions. " +
                $"Remember: MCPsharp uses 0-based indexing where column 0 is before the first character.");

        if (endColumn < 0 || endColumn > endLineLength)
            throw new ArgumentOutOfRangeException(nameof(endColumn),
                $"EndColumn {endColumn} is out of range for line {endLine}. " +
                $"Line {endLine} has {endLineLength} characters. Valid positions: 0-{endLineLength - 1} for replacements, 0-{endLineLength} for insertions. " +
                $"Remember: MCPsharp uses 0-based indexing where column 0 is before the first character.");
        
        // Validate that start position comes before end position
        if (startLine > endLine || (startLine == endLine && startColumn > endColumn))
            throw new ArgumentException($"Invalid edit range: start position ({startLine},{startColumn}) " +
                $"is after end position ({endLine},{endColumn}).");
    }
    
    /// <summary>
    /// Extract line and column bounds from any edit type
    /// </summary>
    private static (int startLine, int startColumn, int endLine, int endColumn) GetEditBounds(TextEdit edit)
    {
        return edit switch
        {
            ReplaceEdit replace => (replace.StartLine, replace.StartColumn, replace.EndLine, replace.EndColumn),
            InsertEdit insert => (insert.StartLine, insert.StartColumn, insert.StartLine, insert.StartColumn),
            DeleteEdit delete => (delete.StartLine, delete.StartColumn, delete.EndLine, delete.EndColumn),
            _ => throw new ArgumentException($"Unknown edit type: {edit.GetType().Name}")
        };
    }

    private void ApplyEdit(List<string> lines, TextEdit edit)
    {
        // Validate edit position before applying
        ValidateEditPosition(lines, edit);

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

    private static void ApplyReplaceEdit(List<string> lines, ReplaceEdit edit)
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

    private static void ApplyInsertEdit(List<string> lines, InsertEdit edit)
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

    private static void ApplyDeleteEdit(List<string> lines, DeleteEdit edit)
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


    private static (int line, int column) GetEditStartPosition(TextEdit edit)
    {
        return edit switch
        {
            ReplaceEdit replace => (replace.StartLine, replace.StartColumn),
            InsertEdit insert => (insert.StartLine, insert.StartColumn),
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

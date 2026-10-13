using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MCPsharp.Models;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services;

/// <summary>
/// Service for file operations within a project context
/// </summary>
public partial class FileOperationsService
{
    private readonly string _rootPath;
    private readonly RoslynWorkspace? _workspace;
    private readonly ILogger<FileOperationsService> _logger;

    public FileOperationsService(string rootPath, RoslynWorkspace? workspace = null, ILogger<FileOperationsService>? logger = null)
    {
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root path does not exist: {rootPath}");
        }

        _rootPath = Path.GetFullPath(rootPath);
        _workspace = workspace;
        _logger = logger ?? NullLogger<FileOperationsService>.Instance;
    }

    /// <summary>
    /// Gets the absolute, normalized root directory that all file operations are confined to.
    /// </summary>
    public string RootPath => _rootPath;

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

            foreach (var matchedPath in result.Files.Select(file => file.Path))
            {
                var fullPath = Path.Combine(_rootPath, matchedPath);
                var fileInfo = new System.IO.FileInfo(fullPath);

                if (!includeHidden && IsHidden(fileInfo))
                    continue;

                files.Add(new Models.FileInfo
                {
                    Path = fullPath,
                    RelativePath = matchedPath,
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
            LogFileOperationFailed(_logger, ex, "read", relativePath);
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
                    LogFailedToUpdateRoslynWorkspace(_logger, workspaceEx, relativePath, workspaceEx.Message);
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
            LogFileOperationFailed(_logger, ex, "write", relativePath);
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
                    LogFailedToUpdateRoslynWorkspace(_logger, workspaceEx, relativePath, workspaceEx.Message);
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
            LogFileOperationFailed(_logger, ex, "edit", relativePath);

            // Provide more detailed error message for validation errors
            string errorMessage;
            if (ex is ArgumentException)
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

    private string GetFullPath(string relativePath)
    {
        // Handle both relative and absolute paths
        if (Path.IsPathRooted(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        return Path.GetFullPath(Path.Combine(_rootPath, relativePath));
    }

    private bool ValidatePath(string fullPath) => PathContainment.IsWithin(_rootPath, fullPath);

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

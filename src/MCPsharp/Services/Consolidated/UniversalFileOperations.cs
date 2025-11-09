using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Models.Consolidated;
using ValidationResult = MCPsharp.Models.Consolidated.ValidationResult;
using ValidationSeverity = MCPsharp.Models.Consolidated.ValidationSeverity;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Universal file operations service consolidating file_info, file_content, file_operation, and file_batch
/// Phase 1: Consolidate 8 file operation tools into 4 universal tools
/// </summary>
public class UniversalFileOperations
{
    private readonly string _rootPath;
    private readonly FileOperationsService _fileOperations;
    private readonly ILogger<UniversalFileOperations> _logger;

    public UniversalFileOperations(
        string rootPath,
        FileOperationsService fileOperations,
        IBulkEditService? bulkEditService = null,
        ILogger<UniversalFileOperations>? logger = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _fileOperations = fileOperations;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UniversalFileOperations>.Instance;
    }

    /// <summary>
    /// Get comprehensive file/directory information
    /// Consolidates: project_info, file_list, get_file_metadata, validate_file_path
    /// </summary>
    public async Task<FileInfoResponse> GetFileInfoAsync(FileInfoRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = new FileInfoResponse
            {
                Path = request.Path,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString()
            };

            // Determine if this is a directory or file
            var fullPath = GetFullPath(request.Path);
            var isDirectory = Directory.Exists(fullPath) && !File.Exists(fullPath);

            if (isDirectory)
            {
                result = await ProcessDirectoryInfoAsync(fullPath, request, ct);
            }
            else
            {
                result = await ProcessFileInfoAsync(fullPath, request, ct);
            }

            stopwatch.Stop();
            result.Metadata.ProcessingTime = stopwatch.Elapsed;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file info for {Path}", request.Path);

            return new FileInfoResponse
            {
                Path = request.Path,
                Error = ex.Message,
                Metadata = new ResponseMetadata
                {
                    ProcessingTime = stopwatch.Elapsed,
                    Success = false
                }
            };
        }
    }

    /// <summary>
    /// Read and write file content
    /// Consolidates: file_read, file_write
    /// </summary>
    public async Task<FileContentResponse> GetFileContentAsync(FileContentRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var fullPath = GetFullPath(request.Path);

            if (!ValidatePath(fullPath))
            {
                return new FileContentResponse
                {
                    Path = request.Path,
                    Error = "Path is outside project root",
                    Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
                };
            }

            var response = new FileContentResponse
            {
                Path = request.Path,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Operation = request.Operation ?? FileContentOperation.Read
            };

            switch (request.Operation)
            {
                case FileContentOperation.Read:
                    response = await HandleReadAsync(fullPath, request, ct);
                    break;

                case FileContentOperation.Write:
                    response = await HandleWriteAsync(fullPath, request, ct);
                    break;

                case FileContentOperation.Append:
                    response = await HandleAppendAsync(fullPath, request, ct);
                    break;

                case FileContentOperation.PartialRead:
                    response = await HandlePartialReadAsync(fullPath, request, ct);
                    break;

                default:
                    response.Error = $"Unsupported operation: {request.Operation}";
                    break;
            }

            stopwatch.Stop();
            response.Metadata.ProcessingTime = stopwatch.Elapsed;

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file content for {Path}", request.Path);

            return new FileContentResponse
            {
                Path = request.Path,
                Error = ex.Message,
                Metadata = new ResponseMetadata
                {
                    ProcessingTime = stopwatch.Elapsed,
                    Success = false
                }
            };
        }
    }

    /// <summary>
    /// Apply file operations (read-modify-write)
    /// Consolidates: file_edit, enhanced with validation and batch operations
    /// </summary>
    public async Task<FileOperationResponse> ExecuteFileOperationAsync(FileOperationRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new FileOperationResponse
            {
                Path = request.Path,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Operation = request.Operation
            };

            switch (request.Operation)
            {
                case FileOperationType.Edit:
                    response = await HandleEditAsync(request, ct);
                    break;

                case FileOperationType.Validate:
                    response = await HandleValidateAsync(request, ct);
                    break;

                case FileOperationType.Copy:
                    response = await HandleCopyAsync(request, ct);
                    break;

                case FileOperationType.Move:
                    response = await HandleMoveAsync(request, ct);
                    break;

                case FileOperationType.Delete:
                    response = await HandleDeleteAsync(request, ct);
                    break;

                default:
                    response.Error = $"Unsupported operation: {request.Operation}";
                    break;
            }

            stopwatch.Stop();
            response.Metadata.ProcessingTime = stopwatch.Elapsed;

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing file operation {Operation} on {Path}", request.Operation, request.Path);

            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = request.Operation,
                Error = ex.Message,
                Metadata = new ResponseMetadata
                {
                    ProcessingTime = stopwatch.Elapsed,
                    Success = false
                }
            };
        }
    }

    /// <summary>
    /// Batch file operations
    /// NEW: Combines multiple file operations in one call with transaction support
    /// </summary>
    public async Task<FileBatchResponse> ExecuteBatchAsync(FileBatchRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = new FileBatchResponse
        {
            Operations = new List<MCPsharp.Models.Consolidated.FileOperationResult>(),
            RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
            TransactionId = request.EnableTransactions ? Guid.NewGuid().ToString() : null
        };

        // Create backup if transactions are enabled
        string? backupPath = null;
        if (request.EnableTransactions)
        {
            backupPath = await CreateTransactionBackupAsync(request.Operations, ct);
        }

        try
        {
            // Execute operations in order
            foreach (var operation in request.Operations)
            {
                var result = await ExecuteSingleOperationAsync(operation, ct);
                response.Operations.Add(result);

                // Stop on first error if fail fast is enabled
                if (!result.Success && request.FailFast)
                {
                    response.Error = $"Batch operation failed at {operation.Path}: {result.Error}";
                    break;
                }
            }

            // Calculate summary
            response.Summary = new BatchSummary
            {
                TotalOperations = request.Operations.Count,
                SuccessfulOperations = response.Operations.Count(o => o.Success),
                FailedOperations = response.Operations.Count(o => !o.Success),
                TotalBytesProcessed = response.Operations.Sum(o => o.BytesProcessed ?? 0)
            };

            // Rollback if enabled and there are failures
            if (request.EnableTransactions && response.Summary.FailedOperations > 0 && backupPath != null)
            {
                await RollbackTransactionAsync(backupPath, ct);
                response.RolledBack = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing batch operations");
            response.Error = ex.Message;

            // Rollback on exception
            if (request.EnableTransactions && backupPath != null)
            {
                await RollbackTransactionAsync(backupPath, ct);
                response.RolledBack = true;
            }
        }
        finally
        {
            // Cleanup backup if successful or rollback completed
            if (backupPath != null && !response.RolledBack)
            {
                await CleanupTransactionBackupAsync(backupPath, ct);
            }
        }

        stopwatch.Stop();
        response.Metadata = new ResponseMetadata
        {
            ProcessingTime = stopwatch.Elapsed,
            Success = response.Error == null
        };

        return response;
    }

    #region Private Helper Methods

    private async Task<FileInfoResponse> ProcessFileInfoAsync(string fullPath, FileInfoRequest request, CancellationToken ct)
    {
        var fileInfo = new System.IO.FileInfo(fullPath);
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);

        // Basic info
        var response = new FileInfoResponse
        {
            Path = request.Path,
            Type = FileType.File,
            Size = fileInfo.Length,
            LastModified = fileInfo.LastWriteTimeUtc,
            Created = fileInfo.CreationTimeUtc,
            Extension = fileInfo.Extension,
            IsHidden = IsHidden(fileInfo),
            Encoding = null, // Will be set on read
            LineCount = 0,
            Language = DetectLanguage(fileInfo.Extension),
            RelativePath = relativePath
        };

        // Enhanced info based on detail level
        if (request.Options?.Detail >= DetailLevel.Standard)
        {
            response.ContentHash = await CalculateFileHashAsync(fullPath, ct);
            response.FileCategory = DetermineFileCategory(fileInfo.Extension, relativePath);
        }

        if (request.Options?.Detail >= DetailLevel.Detailed)
        {
            // Read file for line count and encoding
            try
            {
                var readResult = await _fileOperations.ReadFileAsync(relativePath, ct);
                if (readResult.Success)
                {
                    response.LineCount = readResult.LineCount;
                    response.Encoding = readResult.Encoding;

                    // Include content preview if requested
                    if (request.Options?.Include.HasFlag(IncludeFlags.Preview) == true)
                    {
                        response.ContentPreview = GenerateContentPreview(readResult.Content ?? string.Empty, 500);
                    }
                }
            }
            catch
            {
                // Binary files can't be read as text
                response.LineCount = -1;
            }

            // Include dependencies if requested
            if (request.Options?.Include.HasFlag(IncludeFlags.Dependencies) == true)
            {
                response.Dependencies = await ExtractFileDependenciesAsync(fullPath, ct);
            }
        }

        return response;
    }

    private async Task<FileInfoResponse> ProcessDirectoryInfoAsync(string fullPath, FileInfoRequest request, CancellationToken ct)
    {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);

        var response = new FileInfoResponse
        {
            Path = request.Path,
            Type = FileType.Directory,
            Size = await CalculateDirectorySizeAsync(fullPath, ct),
            LastModified = Directory.GetLastWriteTimeUtc(fullPath),
            Created = Directory.GetCreationTimeUtc(fullPath),
            Extension = "",
            IsHidden = IsHidden(new DirectoryInfo(fullPath)),
            RelativePath = relativePath,
            Children = new List<FileInfoResponse>()
        };

        // List directory contents
        if (request.Options?.Detail >= DetailLevel.Standard)
        {
            var pattern = request.Options?.Filters?.FirstOrDefault(f => f.Type == FilterType.Pattern)?.Value as string ?? "*";

            foreach (var entry in Directory.GetFileSystemEntries(fullPath, pattern))
            {
                var childRequest = new FileInfoRequest
                {
                    Path = Path.GetRelativePath(_rootPath, entry),
                    Options = request.Options != null ? new ToolOptions
                    {
                        Detail = DetailLevel.Basic,
                        Include = request.Options.Include,
                        Filters = request.Options.Filters,
                        Sort = request.Options.Sort,
                        Pagination = request.Options.Pagination
                    } : new ToolOptions { Detail = DetailLevel.Basic }
                };

                var childResponse = await GetFileInfoAsync(childRequest, ct);
                response.Children.Add(childResponse);
            }
        }

        return response;
    }

    private async Task<FileContentResponse> HandleReadAsync(string fullPath, FileContentRequest request, CancellationToken ct)
    {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);
        var readResult = await _fileOperations.ReadFileAsync(relativePath, ct);

        if (!readResult.Success)
        {
            return new FileContentResponse
            {
                Path = request.Path,
                Error = readResult.Error ?? "Failed to read file",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        var response = new FileContentResponse
        {
            Path = request.Path,
            Content = readResult.Content,
            Encoding = readResult.Encoding,
            Size = readResult.Size,
            LineCount = readResult.LineCount,
            Metadata = new ResponseMetadata { Success = true }
        };

        // Apply content transformations if requested
        if (request.Options?.Transformations?.Any() == true)
        {
            response.Content = ApplyContentTransformations(response.Content ?? string.Empty, request.Options.Transformations);
        }

        return response;
    }

    private async Task<FileContentResponse> HandleWriteAsync(string fullPath, FileContentRequest request, CancellationToken ct)
    {
        if (request.Content == null)
        {
            return new FileContentResponse
            {
                Path = request.Path,
                Error = "Content is required for write operation",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        var relativePath = Path.GetRelativePath(_rootPath, fullPath);
        var writeResult = await _fileOperations.WriteFileAsync(relativePath, request.Content, request.Options?.CreateDirectories ?? true, ct);

        return new FileContentResponse
        {
            Path = request.Path,
            BytesWritten = writeResult.BytesWritten,
            Created = writeResult.Created,
            Metadata = new ResponseMetadata
            {
                Success = writeResult.Success,
                Message = writeResult.Success ? "File written successfully" : writeResult.Error
            }
        };
    }

    private async Task<FileContentResponse> HandleAppendAsync(string fullPath, FileContentRequest request, CancellationToken ct)
    {
        if (request.Content == null)
        {
            return new FileContentResponse
            {
                Path = request.Path,
                Error = "Content is required for append operation",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        try
        {
            var existingContent = await File.ReadAllTextAsync(fullPath, ct);
            var newContent = existingContent + request.Content;

            var relativePath = Path.GetRelativePath(_rootPath, fullPath);
            var writeResult = await _fileOperations.WriteFileAsync(relativePath, newContent, true, ct);

            return new FileContentResponse
            {
                Path = request.Path,
                BytesWritten = writeResult.BytesWritten,
                Metadata = new ResponseMetadata { Success = writeResult.Success }
            };
        }
        catch (Exception ex)
        {
            return new FileContentResponse
            {
                Path = request.Path,
                Error = ex.Message,
                Metadata = new ResponseMetadata { Success = false }
            };
        }
    }

    private async Task<FileContentResponse> HandlePartialReadAsync(string fullPath, FileContentRequest request, CancellationToken ct)
    {
        var relativePath = Path.GetRelativePath(_rootPath, fullPath);
        var readResult = await _fileOperations.ReadFileAsync(relativePath, ct);

        if (!readResult.Success)
        {
            return new FileContentResponse
            {
                Path = request.Path,
                Error = readResult.Error,
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        var lines = (readResult.Content ?? string.Empty).Split('\n');
        var startLine = request.Options?.StartLine ?? 0;
        var endLine = request.Options?.EndLine ?? lines.Length - 1;

        // Validate line range
        if (startLine < 0) startLine = 0;
        if (endLine >= lines.Length) endLine = lines.Length - 1;
        if (startLine > endLine)
        {
            return new FileContentResponse
            {
                Path = request.Path,
                Error = "Invalid line range: StartLine cannot be greater than EndLine",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        var partialContent = string.Join('\n', lines[startLine..(endLine + 1)]);

        return new FileContentResponse
        {
            Path = request.Path,
            Content = partialContent,
            LineCount = endLine - startLine + 1,
            StartLine = startLine,
            EndLine = endLine,
            TotalLines = lines.Length,
            Metadata = new ResponseMetadata { Success = true }
        };
    }

    private async Task<FileOperationResponse> HandleEditAsync(FileOperationRequest request, CancellationToken ct)
    {
        if (request.Edits == null || !request.Edits.Any())
        {
            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Edit,
                Error = "Edits are required for edit operation",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        var relativePath = Path.GetRelativePath(_rootPath, GetFullPath(request.Path));
        var editResult = await _fileOperations.EditFileAsync(relativePath, request.Edits, ct);

        return new FileOperationResponse
        {
            Path = request.Path,
            Operation = FileOperationType.Edit,
            EditsApplied = editResult.EditsApplied,
            NewContent = request.Options?.IncludeNewContent == true ? editResult.NewContent : null,
            Metadata = new ResponseMetadata
            {
                Success = editResult.Success,
                Message = editResult.Success ? $"{editResult.EditsApplied} edits applied" : editResult.Error
            }
        };
    }

    private async Task<FileOperationResponse> HandleValidateAsync(FileOperationRequest request, CancellationToken ct)
    {
        var fullPath = GetFullPath(request.Path);
        var validationResults = new List<ValidationResult>();

        // Path validation
        if (!ValidatePath(fullPath))
        {
            validationResults.Add(new ValidationResult
            {
                Type = ValidationType.Path,
                Severity = ValidationSeverity.Error,
                Message = "Path is outside project root"
            });
        }

        // File existence validation
        if (!File.Exists(fullPath))
        {
            validationResults.Add(new ValidationResult
            {
                Type = ValidationType.Existence,
                Severity = ValidationSeverity.Error,
                Message = "File does not exist"
            });
        }
        else
        {
            // File size validation
            var fileInfo = new System.IO.FileInfo(fullPath);
            if (request.Options?.MaxFileSize != null && fileInfo.Length > request.Options.MaxFileSize)
            {
                validationResults.Add(new ValidationResult
                {
                    Type = ValidationType.Size,
                    Severity = ValidationSeverity.Warning,
                    Message = $"File size ({fileInfo.Length} bytes) exceeds limit ({request.Options.MaxFileSize} bytes)"
                });
            }

            // Encoding validation
            try
            {
                var content = await File.ReadAllTextAsync(fullPath, ct);
                if (request.Options?.RequireUtf8 == true && !IsValidUtf8(content))
                {
                    validationResults.Add(new ValidationResult
                    {
                        Type = ValidationType.Encoding,
                        Severity = ValidationSeverity.Warning,
                        Message = "File is not UTF-8 encoded"
                    });
                }
            }
            catch
            {
                validationResults.Add(new ValidationResult
                {
                    Type = ValidationType.Readable,
                    Severity = ValidationSeverity.Error,
                    Message = "File cannot be read as text (likely binary)"
                });
            }
        }

        return new FileOperationResponse
        {
            Path = request.Path,
            Operation = FileOperationType.Validate,
            ValidationResults = validationResults,
            IsValid = !validationResults.Any(v => v.Severity == ValidationSeverity.Error),
            Metadata = new ResponseMetadata
            {
                Success = true,
                Message = validationResults.Count == 0 ? "File is valid" : $"{validationResults.Count} validation issues found"
            }
        };
    }

    private async Task<FileOperationResponse> HandleCopyAsync(FileOperationRequest request, CancellationToken ct)
    {
        if (request.DestinationPath == null)
        {
            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Copy,
                Error = "Destination path is required for copy operation",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        try
        {
            var sourcePath = GetFullPath(request.Path);
            var destPath = GetFullPath(request.DestinationPath);

            // Create destination directory if needed
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Copy file
            await Task.Run(() => File.Copy(sourcePath, destPath, request.Options?.Overwrite ?? false), ct);

            var destFileInfo = new System.IO.FileInfo(destPath);

            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Copy,
                DestinationPath = request.DestinationPath,
                BytesProcessed = destFileInfo.Length,
                Metadata = new ResponseMetadata
                {
                    Success = true,
                    Message = $"File copied to {request.DestinationPath}"
                }
            };
        }
        catch (Exception ex)
        {
            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Copy,
                Error = ex.Message,
                Metadata = new ResponseMetadata { Success = false }
            };
        }
    }

    private async Task<FileOperationResponse> HandleMoveAsync(FileOperationRequest request, CancellationToken ct)
    {
        if (request.DestinationPath == null)
        {
            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Move,
                Error = "Destination path is required for move operation",
                Metadata = new ResponseMetadata { Success = false }
            };
        }

        try
        {
            var sourcePath = GetFullPath(request.Path);
            var destPath = GetFullPath(request.DestinationPath);

            // Create destination directory if needed
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Move file
            await Task.Run(() => File.Move(sourcePath, destPath, request.Options?.Overwrite ?? false), ct);

            var destFileInfo = new System.IO.FileInfo(destPath);

            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Move,
                DestinationPath = request.DestinationPath,
                BytesProcessed = destFileInfo.Length,
                Metadata = new ResponseMetadata
                {
                    Success = true,
                    Message = $"File moved to {request.DestinationPath}"
                }
            };
        }
        catch (Exception ex)
        {
            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Move,
                Error = ex.Message,
                Metadata = new ResponseMetadata { Success = false }
            };
        }
    }

    private async Task<FileOperationResponse> HandleDeleteAsync(FileOperationRequest request, CancellationToken ct)
    {
        try
        {
            var fullPath = GetFullPath(request.Path);
            var fileInfo = new System.IO.FileInfo(fullPath);
            var size = fileInfo.Exists ? fileInfo.Length : 0;

            if (fileInfo.Exists)
            {
                await Task.Run(() => File.Delete(fullPath), ct);
            }

            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Delete,
                BytesProcessed = size,
                Metadata = new ResponseMetadata
                {
                    Success = true,
                    Message = "File deleted successfully"
                }
            };
        }
        catch (Exception ex)
        {
            return new FileOperationResponse
            {
                Path = request.Path,
                Operation = FileOperationType.Delete,
                Error = ex.Message,
                Metadata = new ResponseMetadata { Success = false }
            };
        }
    }

    private async Task<MCPsharp.Models.Consolidated.FileOperationResult> ExecuteSingleOperationAsync(FileOperationDefinition operation, CancellationToken ct)
    {
        var request = new FileOperationRequest
        {
            Path = operation.Path,
            Operation = operation.Type,
            DestinationPath = operation.DestinationPath,
            Edits = operation.Edits,
            Options = operation.Options
        };

        var response = await ExecuteFileOperationAsync(request, ct);

        return new MCPsharp.Models.Consolidated.FileOperationResult
        {
            Path = operation.Path,
            Success = response.Metadata.Success,
            Error = response.Error,
            BytesProcessed = response.BytesProcessed,
            Operation = operation.Type
        };
    }

    #endregion

    #region Utility Methods

    private string GetFullPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }
        return Path.GetFullPath(Path.Combine(_rootPath, path));
    }

    private bool ValidatePath(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.StartsWith(_rootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHidden(FileSystemInfo info)
    {
        return info.Attributes.HasFlag(FileAttributes.Hidden) ||
               info.Name.StartsWith('.');
    }

    private static string DetectLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".csx" => "csharp",
            ".vb" => "vb",
            ".fs" => "fsharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".jsx" => "jsx",
            ".tsx" => "tsx",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            ".sql" => "sql",
            ".html" => "html",
            ".css" => "css",
            ".scss" or ".sass" => "scss",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            ".java" => "java",
            ".cpp" or ".cxx" or ".cc" => "cpp",
            ".c" => "c",
            ".h" or ".hpp" => "cpp",
            ".sh" or ".bash" => "shell",
            ".ps1" => "powershell",
            _ => "text"
        };
    }

    private static FileCategory DetermineFileCategory(string extension, string relativePath)
    {
        // Check by extension first
        var ext = extension.ToLowerInvariant();

        if (CodeFileExtensions.Contains(ext))
            return FileCategory.SourceCode;
        if (ConfigFileExtensions.Contains(ext))
            return FileCategory.Configuration;
        if (DocumentationExtensions.Contains(ext))
            return FileCategory.Documentation;
        if (TestFileExtensions.Contains(ext))
            return FileCategory.Test;
        if (BuildFileExtensions.Contains(ext))
            return FileCategory.Build;

        // Check by path pattern
        var pathLower = relativePath.ToLowerInvariant();
        if (pathLower.Contains("test") || pathLower.Contains("spec"))
            return FileCategory.Test;
        if (pathLower.Contains("doc") || pathLower.Contains("readme"))
            return FileCategory.Documentation;

        return FileCategory.Other;
    }

    private static readonly HashSet<string> CodeFileExtensions = new()
    {
        ".cs", ".csx", ".vb", ".fs", ".js", ".ts", ".jsx", ".tsx",
        ".py", ".go", ".rs", ".java", ".cpp", ".cxx", ".cc", ".c",
        ".h", ".hpp", ".swift", ".kt", ".scala", ".rb", ".php"
    };

    private static readonly HashSet<string> ConfigFileExtensions = new()
    {
        ".json", ".xml", ".yaml", ".yml", ".toml", ".ini", ".cfg",
        ".config", ".props", ".targets", ".csproj", ".sln"
    };

    private static readonly HashSet<string> DocumentationExtensions = new()
    {
        ".md", ".txt", ".rst", ".adoc", ".pdf", ".doc", ".docx"
    };

    private static readonly HashSet<string> TestFileExtensions = new()
    {
        ".test.cs", ".tests.cs", ".spec.cs", ".test.js", ".spec.js",
        ".test.ts", ".spec.ts", ".test.py", "test_*.py"
    };

    private static readonly HashSet<string> BuildFileExtensions = new()
    {
        ".props", ".targets", ".proj", ".build", ".cake", "Makefile",
        "CMakeLists.txt", "Dockerfile", "docker-compose.yml"
    };

    private static async Task<string> CalculateFileHashAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, ct);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<long> CalculateDirectorySizeAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(file => new System.IO.FileInfo(file).Length);
            }
            catch
            {
                return 0;
            }
        }, ct);
    }

    private static string? GenerateContentPreview(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || content.Length <= maxLength)
            return content;

        return content[..maxLength] + "...";
    }

    private static string ApplyContentTransformations(string content, List<ContentTransformation> transformations)
    {
        var result = content;

        foreach (var transformation in transformations)
        {
            result = transformation.Type switch
            {
                TransformationType.Trim => result.Trim(),
                TransformationType.NormalizeLineEndings => result.Replace("\r\n", "\n").Replace('\r', '\n'),
                TransformationType.RemoveBlankLines => string.Join('\n', result.Split('\n').Where(line => !string.IsNullOrWhiteSpace(line))),
                TransformationType.SortLines => string.Join('\n', result.Split('\n').OrderBy(line => line)),
                TransformationType.RemoveComments => RemoveComments(result, transformation.Language),
                _ => result
            };
        }

        return result;
    }

    private static string RemoveComments(string content, string? language)
    {
        // Simple comment removal - could be enhanced per language
        var lines = content.Split('\n');
        var result = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("//") && !trimmed.StartsWith("#") && !trimmed.StartsWith("/*"))
            {
                result.Add(line);
            }
        }

        return string.Join('\n', result);
    }

    private static async Task<List<string>> ExtractFileDependenciesAsync(string fullPath, CancellationToken ct)
    {
        var dependencies = new List<string>();

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, ct);
            var extension = Path.GetExtension(fullPath).ToLowerInvariant();

            // Simple dependency extraction - could be enhanced per language
            if (extension == ".cs")
            {
                // Extract using statements
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    content,
                    @"using\s+([\w.]+);",
                    System.Text.RegularExpressions.RegexOptions.Multiline);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    dependencies.Add($"namespace:{match.Groups[1].Value}");
                }
            }
        }
        catch
        {
            // Ignore errors in dependency extraction
        }

        return dependencies;
    }

    private static bool IsValidUtf8(string content)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var decoded = Encoding.UTF8.GetString(bytes);
            return decoded == content;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> CreateTransactionBackupAsync(List<FileOperationDefinition> operations, CancellationToken ct)
    {
        var backupDir = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", Guid.NewGuid().ToString());
        Directory.CreateDirectory(backupDir);

        try
        {
            foreach (var operation in operations.Where(o => File.Exists(GetFullPath(o.Path))))
            {
                var sourcePath = GetFullPath(operation.Path);
                var backupPath = Path.Combine(backupDir, operation.Path.Replace('/', '_').Replace('\\', '_'));
                await Task.Run(() => File.Copy(sourcePath, backupPath), ct);
            }

            return backupDir;
        }
        catch
        {
            // Cleanup on failure
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, true);
            }
            return null;
        }
    }

    private async Task RollbackTransactionAsync(string backupPath, CancellationToken ct)
    {
        if (!Directory.Exists(backupPath))
            return;

        foreach (var backupFile in Directory.GetFiles(backupPath))
        {
            try
            {
                var fileName = Path.GetFileName(backupFile);
                var originalPath = fileName.Replace('_', Path.DirectorySeparatorChar);
                var targetPath = GetFullPath(originalPath);

                await Task.Run(() => File.Copy(backupFile, targetPath, true), ct);
            }
            catch
            {
                // Continue with other files
            }
        }
    }

    private async Task CleanupTransactionBackupAsync(string backupPath, CancellationToken ct)
    {
        try
        {
            await Task.Run(() => Directory.Delete(backupPath, true), ct);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    #endregion
}
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services.Consolidated;

public partial class UniversalFileOperations
{
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
                    if (request.Options?.Include.HasFlag(IncludeOptions.Preview) == true)
                    {
                        response.ContentPreview = GenerateContentPreview(readResult.Content ?? string.Empty, 500);
                    }
                }
            }
            catch (Exception ex)
            {
                // Binary files can't be read as text
                LogIgnoredException(_logger, ex, "reading file for line count", relativePath);
                response.LineCount = -1;
            }

            // Include dependencies if requested
            if (request.Options?.Include.HasFlag(IncludeOptions.Dependencies) == true)
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
        if (request.Options?.Transformations?.Count > 0)
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
            LogFileOperationFailed(_logger, ex, "append", request.Path);
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
}

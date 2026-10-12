using MCPsharp.Models.Consolidated;
using ValidationResult = MCPsharp.Models.Consolidated.ValidationResult;
using ValidationSeverity = MCPsharp.Models.Consolidated.ValidationSeverity;

namespace MCPsharp.Services.Consolidated;

public partial class UniversalFileOperations
{
    private async Task<FileOperationResponse> HandleEditAsync(FileOperationRequest request, CancellationToken ct)
    {
        if (request.Edits == null || request.Edits.Count == 0)
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
                Message = OutsideRootError
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
            catch (Exception ex)
            {
                LogIgnoredException(_logger, ex, "reading file for validation", request.Path);
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
            LogFileOperationFailed(_logger, ex, "copy", request.Path);
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
            LogFileOperationFailed(_logger, ex, "move", request.Path);
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
            LogFileOperationFailed(_logger, ex, "delete", request.Path);
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
}

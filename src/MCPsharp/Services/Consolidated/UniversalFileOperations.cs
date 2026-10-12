using MCPsharp.Models.Consolidated;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Universal file operations service consolidating file_info, file_content, file_operation, and file_batch
/// Phase 1: Consolidate 8 file operation tools into 4 universal tools
/// </summary>
public partial class UniversalFileOperations
{
    private const string OutsideRootError = "Path is outside project root";

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
            FileInfoResponse result;

            // Determine if this is a directory or file
            var fullPath = GetFullPath(request.Path);
            if (!ValidatePath(fullPath))
            {
                return new FileInfoResponse
                {
                    Path = request.Path,
                    Error = OutsideRootError,
                    Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
                };
            }

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
            LogErrorGettingFileInfoFor(_logger, ex, request.Path);

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
                    Error = OutsideRootError,
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
            LogErrorHandlingFileContentFor(_logger, ex, request.Path);

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

            // Validate reports an outside path as a validation result; every other operation is refused
            if (request.Operation != FileOperationType.Validate &&
                (!ValidatePath(GetFullPath(request.Path)) ||
                 (request.DestinationPath != null && !ValidatePath(GetFullPath(request.DestinationPath)))))
            {
                return new FileOperationResponse
                {
                    Path = request.Path,
                    RequestId = response.RequestId,
                    Operation = request.Operation,
                    Error = OutsideRootError,
                    Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
                };
            }

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
            LogErrorExecutingFileOperationOn(_logger, ex, request.Operation, request.Path);

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
            LogErrorExecutingBatchOperations(_logger, ex);
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
}

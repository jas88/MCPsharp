using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// MCP handler for consolidated file operations tools
/// Phase 1: Implements 4 universal file operation tools replacing 8 original tools
/// </summary>
public partial class FileOperationsHandler
{
    private static readonly JsonSerializerOptions IndentedCamelCaseOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly UniversalFileOperations _fileOps;
    private readonly ILogger<FileOperationsHandler> _logger;

    public FileOperationsHandler(
        UniversalFileOperations fileOps,
        ILogger<FileOperationsHandler> logger,
        ResponseProcessor responseProcessor)
    {
        _fileOps = fileOps;
        _logger = logger;
    }

    /// <summary>
    /// file_info - Get comprehensive file/directory information
    /// Replaces: project_info, file_list, get_file_metadata, validate_file_path
    /// </summary>
    public async Task<string> HandleFileInfoAsync(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            var request = ParseFileInfoRequest(arguments);
            var response = await _fileOps.GetFileInfoAsync(request, ct);

            var result = new
            {
                success = response.Metadata.Success,
                data = response.Error == null ? response : null,
                error = response.Error,
                metadata = response.Metadata
            };

            return JsonSerializer.Serialize(result, IndentedCamelCaseOptions);
        }
        catch (Exception ex)
        {
            LogErrorHandlingFileInfoRequest(_logger, ex);

            var errorResponse = new
            {
                success = false,
                error = ex.Message,
                metadata = new
                {
                    processedAt = DateTime.UtcNow,
                    success = false
                }
            };

            return JsonSerializer.Serialize(errorResponse, IndentedCamelCaseOptions);
        }
    }

    /// <summary>
    /// file_content - Read and write file content
    /// Replaces: file_read, file_write
    /// </summary>
    public async Task<string> HandleFileContentAsync(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            var request = ParseFileContentRequest(arguments);
            var response = await _fileOps.GetFileContentAsync(request, ct);

            var result = new
            {
                success = response.Metadata.Success,
                data = response.Error == null ? response : null,
                error = response.Error,
                metadata = response.Metadata
            };

            return JsonSerializer.Serialize(result, IndentedCamelCaseOptions);
        }
        catch (Exception ex)
        {
            LogErrorHandlingFileContentRequest(_logger, ex);

            var errorResponse = new
            {
                success = false,
                error = ex.Message,
                metadata = new
                {
                    processedAt = DateTime.UtcNow,
                    success = false
                }
            };

            return JsonSerializer.Serialize(errorResponse, IndentedCamelCaseOptions);
        }
    }

    /// <summary>
    /// file_operation - Apply file operations
    /// Replaces: file_edit (enhanced with validation and more operations)
    /// </summary>
    public async Task<string> HandleFileOperationAsync(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            var request = ParseFileOperationRequest(arguments);
            var response = await _fileOps.ExecuteFileOperationAsync(request, ct);

            var result = new
            {
                success = response.Metadata.Success,
                data = response.Error == null ? response : null,
                error = response.Error,
                metadata = response.Metadata
            };

            return JsonSerializer.Serialize(result, IndentedCamelCaseOptions);
        }
        catch (Exception ex)
        {
            LogErrorHandlingFileOperationRequest(_logger, ex);

            var errorResponse = new
            {
                success = false,
                error = ex.Message,
                metadata = new
                {
                    processedAt = DateTime.UtcNow,
                    success = false
                }
            };

            return JsonSerializer.Serialize(errorResponse, IndentedCamelCaseOptions);
        }
    }

    /// <summary>
    /// file_batch - Batch file operations
    /// NEW: Combines multiple file operations in one call with transaction support
    /// </summary>
    public async Task<string> HandleFileBatchAsync(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            var request = ParseFileBatchRequest(arguments);
            var response = await _fileOps.ExecuteBatchAsync(request, ct);

            var result = new
            {
                success = response.Metadata.Success,
                data = response.Error == null ? response : null,
                error = response.Error,
                metadata = response.Metadata
            };

            return JsonSerializer.Serialize(result, IndentedCamelCaseOptions);
        }
        catch (Exception ex)
        {
            LogErrorHandlingFileBatchRequest(_logger, ex);

            var errorResponse = new
            {
                success = false,
                error = ex.Message,
                metadata = new
                {
                    processedAt = DateTime.UtcNow,
                    success = false
                }
            };

            return JsonSerializer.Serialize(errorResponse, IndentedCamelCaseOptions);
        }
    }
}

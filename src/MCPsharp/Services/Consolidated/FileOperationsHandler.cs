using System.Text.Json;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// MCP handler for consolidated file operations tools
/// Phase 1: Implements 4 universal file operation tools replacing 8 original tools
/// </summary>
public class FileOperationsHandler
{
    private readonly UniversalFileOperations _fileOps;
    private readonly ILogger<FileOperationsHandler> _logger;
    private readonly ResponseProcessor _responseProcessor;

    public FileOperationsHandler(
        UniversalFileOperations fileOps,
        ILogger<FileOperationsHandler> logger,
        ResponseProcessor responseProcessor)
    {
        _fileOps = fileOps;
        _logger = logger;
        _responseProcessor = responseProcessor;
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file_info request");

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

            return JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file_content request");

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

            return JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file_operation request");

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

            return JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
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

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling file_batch request");

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

            return JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }

    #region Request Parsing Methods

    private static FileInfoRequest ParseFileInfoRequest(JsonDocument arguments)
    {
        var root = arguments.RootElement;

        var path = root.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");

        var request = new FileInfoRequest
        {
            Path = path
        };

        // Parse optional options
        if (root.TryGetProperty("options", out var optionsElement))
        {
            request.Options = ParseToolOptions(optionsElement);
        }

        // Parse optional request ID
        if (root.TryGetProperty("requestId", out var requestIdElement))
        {
            request.RequestId = requestIdElement.GetString();
        }

        return request;
    }

    private static FileContentRequest ParseFileContentRequest(JsonDocument arguments)
    {
        var root = arguments.RootElement;

        var path = root.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");

        var request = new FileContentRequest
        {
            Path = path
        };

        // Parse operation
        if (root.TryGetProperty("operation", out var operationElement) &&
            operationElement.GetString() is { } operationStr)
        {
            request.Operation = Enum.Parse<FileContentOperation>(operationStr, true);
        }

        // Parse content
        if (root.TryGetProperty("content", out var contentElement))
        {
            request.Content = contentElement.GetString();
        }

        // Parse optional options
        if (root.TryGetProperty("options", out var optionsElement))
        {
            request.Options = ParseFileContentOptions(optionsElement);
        }

        // Parse optional request ID
        if (root.TryGetProperty("requestId", out var requestIdElement))
        {
            request.RequestId = requestIdElement.GetString();
        }

        return request;
    }

    private static FileOperationRequest ParseFileOperationRequest(JsonDocument arguments)
    {
        var root = arguments.RootElement;

        var path = root.GetProperty("path").GetString()
            ?? throw new ArgumentException("path is required");

        var operationStr = root.GetProperty("operation").GetString()
            ?? throw new ArgumentException("operation is required");

        var request = new FileOperationRequest
        {
            Path = path,
            Operation = Enum.Parse<FileOperationType>(operationStr, true)
        };

        // Parse destination path
        if (root.TryGetProperty("destinationPath", out var destPathElement))
        {
            request.DestinationPath = destPathElement.GetString();
        }

        // Parse edits
        if (root.TryGetProperty("edits", out var editsElement) && editsElement.ValueKind == JsonValueKind.Array)
        {
            request.Edits = ParseTextEdits(editsElement);
        }

        // Parse optional options
        if (root.TryGetProperty("options", out var optionsElement))
        {
            request.Options = ParseFileOperationOptions(optionsElement);
        }

        // Parse optional request ID
        if (root.TryGetProperty("requestId", out var requestIdElement))
        {
            request.RequestId = requestIdElement.GetString();
        }

        return request;
    }

    private static FileBatchRequest ParseFileBatchRequest(JsonDocument arguments)
    {
        var root = arguments.RootElement;

        if (!root.TryGetProperty("operations", out var opsElement) || opsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("operations array is required");
        }

        var request = new FileBatchRequest
        {
            Operations = ParseFileOperationDefinitions(opsElement)
        };

        // Parse optional flags
        if (root.TryGetProperty("enableTransactions", out var transactionsElement))
        {
            request.EnableTransactions = transactionsElement.GetBoolean();
        }

        if (root.TryGetProperty("failFast", out var failFastElement))
        {
            request.FailFast = failFastElement.GetBoolean();
        }

        // Parse optional request ID
        if (root.TryGetProperty("requestId", out var requestIdElement))
        {
            request.RequestId = requestIdElement.GetString();
        }

        return request;
    }

    private static ToolOptions? ParseToolOptions(JsonElement element)
    {
        var options = new ToolOptions();

        // Parse detail level
        if (element.TryGetProperty("detail", out var detailElement))
        {
            options.Detail = Enum.Parse<DetailLevel>(detailElement.GetString() ?? "Standard", true);
        }

        // Parse include flags
        if (element.TryGetProperty("include", out var includeElement) && includeElement.ValueKind == JsonValueKind.Array)
        {
            var includeFlags = IncludeFlags.Default;
            foreach (var flagElement in includeElement.EnumerateArray())
            {
                if (Enum.TryParse<IncludeFlags>(flagElement.GetString(), true, out var flag))
                {
                    includeFlags |= flag;
                }
            }
            options.Include = includeFlags;
        }

        // Parse filters
        if (element.TryGetProperty("filters", out var filtersElement) && filtersElement.ValueKind == JsonValueKind.Array)
        {
            options.Filters = ParseFilters(filtersElement);
        }

        // Parse sort
        if (element.TryGetProperty("sort", out var sortElement))
        {
            options.Sort = ParseSortSpecification(sortElement);
        }

        // Parse pagination
        if (element.TryGetProperty("pagination", out var paginationElement))
        {
            options.Pagination = ParsePaginationSpecification(paginationElement);
        }

        return options;
    }

    private static FileContentOptions ParseFileContentOptions(JsonElement element)
    {
        var options = new FileContentOptions();

        // Parse base options
        var baseOptions = ParseToolOptions(element);
        if (baseOptions != null)
        {
            options.Detail = baseOptions.Detail;
            options.Include = baseOptions.Include;
            options.Filters = baseOptions.Filters;
            options.Sort = baseOptions.Sort;
            options.Pagination = baseOptions.Pagination;
        }

        // Parse file content specific options
        if (element.TryGetProperty("createDirectories", out var createDirElement))
        {
            options.CreateDirectories = createDirElement.GetBoolean();
        }

        if (element.TryGetProperty("overwrite", out var overwriteElement))
        {
            options.Overwrite = overwriteElement.GetBoolean();
        }

        if (element.TryGetProperty("transformations", out var transformationsElement) && transformationsElement.ValueKind == JsonValueKind.Array)
        {
            options.Transformations = ParseTransformations(transformationsElement);
        }

        if (element.TryGetProperty("startLine", out var startLineElement))
        {
            options.StartLine = startLineElement.GetInt32();
        }

        if (element.TryGetProperty("endLine", out var endLineElement))
        {
            options.EndLine = endLineElement.GetInt32();
        }

        return options;
    }

    private static FileOperationOptions ParseFileOperationOptions(JsonElement element)
    {
        var options = new FileOperationOptions();

        if (element.TryGetProperty("maxFileSize", out var maxSizeElement))
        {
            options.MaxFileSize = maxSizeElement.GetInt64();
        }

        if (element.TryGetProperty("requireUtf8", out var utf8Element))
        {
            options.RequireUtf8 = utf8Element.GetBoolean();
        }

        if (element.TryGetProperty("includeNewContent", out var includeContentElement))
        {
            options.IncludeNewContent = includeContentElement.GetBoolean();
        }

        if (element.TryGetProperty("overwrite", out var overwriteElement))
        {
            options.Overwrite = overwriteElement.GetBoolean();
        }

        if (element.TryGetProperty("createBackup", out var backupElement))
        {
            options.CreateBackup = backupElement.GetBoolean();
        }

        return options;
    }

    private static List<TextEdit> ParseTextEdits(JsonElement element)
    {
        var edits = new List<TextEdit>();

        foreach (var editElement in element.EnumerateArray())
        {
            var typeStr = editElement.GetProperty("type").GetString()
                ?? throw new ArgumentException("Edit type is required");

            var editType = typeStr.ToLowerInvariant() switch
            {
                "replace" => typeof(ReplaceEdit),
                "insert" => typeof(InsertEdit),
                "delete" => typeof(DeleteEdit),
                _ => throw new ArgumentException($"Unknown edit type: {typeStr}")
            };

            var edit = (TextEdit)Activator.CreateInstance(editType)!;

            if (editElement.TryGetProperty("filePath", out var filePathElement))
            {
                edit.FilePath = filePathElement.GetString();
            }

            edit.StartLine = editElement.GetProperty("startLine").GetInt32();
            edit.StartColumn = editElement.GetProperty("startColumn").GetInt32();
            edit.EndLine = editElement.GetProperty("endLine").GetInt32();
            edit.EndColumn = editElement.GetProperty("endColumn").GetInt32();

            if (editElement.TryGetProperty("newText", out var newTextElement))
            {
                edit.NewText = newTextElement.GetString() ?? string.Empty;
            }

            edits.Add(edit);
        }

        return edits;
    }

    private static List<FileOperationDefinition> ParseFileOperationDefinitions(JsonElement element)
    {
        var operations = new List<FileOperationDefinition>();

        foreach (var opElement in element.EnumerateArray())
        {
            var path = opElement.GetProperty("path").GetString()
                ?? throw new ArgumentException("Operation path is required");

            var typeStr = opElement.GetProperty("type").GetString()
                ?? throw new ArgumentException("Operation type is required");

            var operation = new FileOperationDefinition
            {
                Path = path,
                Type = Enum.Parse<FileOperationType>(typeStr, true)
            };

            if (opElement.TryGetProperty("destinationPath", out var destPathElement))
            {
                operation.DestinationPath = destPathElement.GetString();
            }

            if (opElement.TryGetProperty("edits", out var editsElement) && editsElement.ValueKind == JsonValueKind.Array)
            {
                operation.Edits = ParseTextEdits(editsElement);
            }

            if (opElement.TryGetProperty("options", out var optionsElement))
            {
                operation.Options = ParseFileOperationOptions(optionsElement);
            }

            operations.Add(operation);
        }

        return operations;
    }

    private static List<Filter> ParseFilters(JsonElement element)
    {
        var filters = new List<Filter>();

        foreach (var filterElement in element.EnumerateArray())
        {
            var filter = new Filter
            {
                Type = Enum.Parse<FilterType>(filterElement.GetProperty("type").GetString() ?? "Pattern", true),
                Value = filterElement.GetProperty("value").GetString() ?? string.Empty
            };

            if (filterElement.TryGetProperty("operator", out var opElement))
            {
                filter.Operator = opElement.GetString();
            }

            if (filterElement.TryGetProperty("parameters", out var paramsElement))
            {
                filter.Parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(paramsElement);
            }

            filters.Add(filter);
        }

        return filters;
    }

    private static SortSpecification ParseSortSpecification(JsonElement element)
    {
        return new SortSpecification
        {
            Field = element.GetProperty("field").GetString() ?? "name",
            Ascending = element.TryGetProperty("ascending", out var ascElement) ? ascElement.GetBoolean() : true
        };
    }

    private static PaginationSpec ParsePaginationSpecification(JsonElement element)
    {
        var spec = new PaginationSpec();

        if (element.TryGetProperty("page", out var pageElement))
        {
            spec.Page = pageElement.GetInt32();
        }

        if (element.TryGetProperty("pageSize", out var pageSizeElement))
        {
            spec.PageSize = pageSizeElement.GetInt32();
        }

        if (element.TryGetProperty("continuationToken", out var tokenElement))
        {
            spec.ContinuationToken = tokenElement.GetString();
        }

        return spec;
    }

    private static List<ContentTransformation> ParseTransformations(JsonElement element)
    {
        var transformations = new List<ContentTransformation>();

        foreach (var transElement in element.EnumerateArray())
        {
            var transformation = new ContentTransformation
            {
                Type = Enum.Parse<TransformationType>(transElement.GetProperty("type").GetString() ?? "Trim", true)
            };

            if (transElement.TryGetProperty("language", out var langElement))
            {
                transformation.Language = langElement.GetString();
            }

            if (transElement.TryGetProperty("parameters", out var paramsElement))
            {
                transformation.Parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(paramsElement);
            }

            transformations.Add(transformation);
        }

        return transformations;
    }

    #endregion
}
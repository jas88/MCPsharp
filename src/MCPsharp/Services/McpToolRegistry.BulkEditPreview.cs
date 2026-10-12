using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

namespace MCPsharp.Services;

/// <summary>
/// Bulk edit preview, validation and impact estimation tools
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Preview bulk edit changes without applying them, with impact analysis
    /// </summary>
    /// <param name="arguments">Tool arguments containing operation type, files, and operation-specific parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with preview details and impact analysis</returns>
    private async Task<ToolCallResult> ExecutePreviewBulkChanges(JsonDocument arguments, CancellationToken cancellationToken)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationType = arguments.RootElement.GetProperty("operationType").GetString();

            if (string.IsNullOrEmpty(operationType))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            // Create BulkEditRequest from parameters
            BulkEditRequest request;
            var bulkEditOpType = Enum.Parse<BulkEditOperationType>(operationType);

            if (bulkEditOpType == BulkEditOperationType.BulkReplace)
            {
                var regexPattern = arguments.RootElement.GetProperty("regexPattern").GetString();
                var replacement = arguments.RootElement.GetProperty("replacement").GetString();

                request = new BulkEditRequest
                {
                    OperationType = bulkEditOpType,
                    Files = files,
                    RegexPattern = regexPattern,
                    RegexReplacement = replacement,
                    Options = new BulkEditOptions { PreviewMode = true }
                };
            }
            else if (bulkEditOpType == BulkEditOperationType.ConditionalEdit)
            {
                var conditionType = arguments.RootElement.GetProperty("conditionType").GetString();
                var pattern = arguments.RootElement.GetProperty("pattern").GetString();

                request = new BulkEditRequest
                {
                    OperationType = bulkEditOpType,
                    Files = files,
                    Condition = new BulkEditCondition
                    {
                        ConditionType = Enum.Parse<BulkConditionType>(conditionType ?? "FileContains"),
                        Pattern = pattern ?? string.Empty
                    },
                    Options = new BulkEditOptions { PreviewMode = true }
                };
            }
            else if (bulkEditOpType == BulkEditOperationType.MultiFileEdit)
            {
                var operationsArray = arguments.RootElement.GetProperty("operations");
                var multiFileOperations = JsonSerializer.Deserialize<List<MultiFileOperation>>(operationsArray.GetRawText(), ToolArgumentJsonOptions) ?? new List<MultiFileOperation>();

                var editOperations = multiFileOperations.Select(op => new MultiFileEditOperation
                {
                    FilePattern = op.FilePattern,
                    Edits = op.Edits.Select<FileEdit, TextEdit>(e =>
                    {
                        // Determine the type of edit based on the Type property
                        return e.Type?.ToLowerInvariant() switch
                        {
                            "insert" or "insertion" => new InsertEdit
                            {
                                NewText = e.NewText ?? string.Empty,
                                StartLine = e.StartLine ?? 0,
                                EndLine = e.EndLine ?? 0,
                                StartColumn = e.StartColumn ?? 0,
                                EndColumn = e.EndColumn ?? 0
                            },
                            "delete" or "deletion" => new DeleteEdit
                            {
                                NewText = e.NewText ?? string.Empty,
                                StartLine = e.StartLine ?? 0,
                                EndLine = e.EndLine ?? 0,
                                StartColumn = e.StartColumn ?? 0,
                                EndColumn = e.EndColumn ?? 0
                            },
                            "replace" or "replacement" or _ => new ReplaceEdit
                            {
                                NewText = e.NewText ?? string.Empty,
                                StartLine = e.StartLine ?? 0,
                                EndLine = e.EndLine ?? 0,
                                StartColumn = e.StartColumn ?? 0,
                                EndColumn = e.EndColumn ?? 0
                            }
                        };
                    }).ToList(),
                    Priority = op.Priority,
                    DependsOn = op.DependsOn
                }).ToList();

                request = new BulkEditRequest
                {
                    OperationType = bulkEditOpType,
                    Files = files,
                    MultiFileEdits = editOperations,
                    Options = new BulkEditOptions { PreviewMode = true }
                };
            }
            else
            {
                return new ToolCallResult { Success = false, Error = $"Unsupported operation type: {operationType}" };
            }

            var result = await _bulkEditService.PreviewBulkChangesAsync(request, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecutePreviewBulkChanges));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Validate a bulk edit request before execution to identify potential issues
    /// </summary>
    /// <param name="arguments">Tool arguments containing operation type, files, and operation-specific parameters</param>
    /// <returns>Tool execution result with validation details</returns>
    private async Task<ToolCallResult> ExecuteValidateBulkEdit(JsonDocument arguments)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationType = arguments.RootElement.GetProperty("operationType").GetString();

            if (string.IsNullOrEmpty(operationType))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            // Create BulkEditRequest for validation
            BulkEditRequest request;
            var bulkEditOpType = Enum.Parse<BulkEditOperationType>(operationType);

            if (bulkEditOpType == BulkEditOperationType.BulkReplace)
            {
                var regexPattern = arguments.RootElement.GetProperty("regexPattern").GetString();
                request = new BulkEditRequest
                {
                    OperationType = bulkEditOpType,
                    Files = files,
                    RegexPattern = regexPattern,
                    Options = new BulkEditOptions()
                };
            }
            else if (bulkEditOpType == BulkEditOperationType.ConditionalEdit)
            {
                var conditionType = arguments.RootElement.GetProperty("conditionType").GetString();
                var pattern = arguments.RootElement.GetProperty("pattern").GetString();
                request = new BulkEditRequest
                {
                    OperationType = bulkEditOpType,
                    Files = files,
                    Condition = new BulkEditCondition
                    {
                        ConditionType = Enum.Parse<BulkConditionType>(conditionType ?? "FileContains"),
                        Pattern = pattern ?? string.Empty
                    },
                    Options = new BulkEditOptions()
                };
            }
            else
            {
                return new ToolCallResult { Success = false, Error = $"Unsupported operation type for validation: {operationType}" };
            }

            var result = await _bulkEditService.ValidateBulkEditAsync(request, CancellationToken.None);

            return new ToolCallResult { Success = result.IsValid, Result = result, Error = result.IsValid ? null : result.Summary };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteValidateBulkEdit));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Estimate the impact and complexity of a bulk edit operation before execution
    /// </summary>
    /// <param name="arguments">Tool arguments containing operation type and files</param>
    /// <returns>Tool execution result with impact estimation</returns>
    private async Task<ToolCallResult> ExecuteEstimateBulkImpact(JsonDocument arguments)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            var operationType = arguments.RootElement.GetProperty("operationType").GetString();

            if (string.IsNullOrEmpty(operationType))
            {
                return new ToolCallResult { Success = false, Error = "Operation type is required" };
            }

            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            // Create BulkEditRequest for impact estimation
            var bulkEditOpType = Enum.Parse<BulkEditOperationType>(operationType);
            var request = new BulkEditRequest
            {
                OperationType = bulkEditOpType,
                Files = files,
                Options = new BulkEditOptions()
            };

            var result = await _bulkEditService.EstimateImpactAsync(request, CancellationToken.None);

            // ImpactEstimate doesn't have Success/Error properties, so we assume success
            return new ToolCallResult { Success = true, Result = result, Error = null };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteEstimateBulkImpact));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get statistics about files for planning bulk operations (sizes, counts, types)
    /// </summary>
    /// <param name="arguments">Tool arguments containing files array</param>
    /// <returns>Tool execution result with file statistics</returns>
    private async Task<ToolCallResult> ExecuteGetBulkFileStatistics(JsonDocument arguments)
    {
        if (_bulkEditService == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "BulkEditService is not available. Ensure it's properly initialized."
            };
        }

        try
        {
            // Parse files array
            var filesArray = arguments.RootElement.GetProperty("files");
            var files = new List<string>();
            foreach (var fileElement in filesArray.EnumerateArray())
            {
                var file = fileElement.GetString();
                if (!string.IsNullOrEmpty(file))
                {
                    files.Add(file);
                }
            }

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            var result = await _bulkEditService.GetFileStatisticsAsync(files, CancellationToken.None);

            return new ToolCallResult { Success = true, Result = result };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteGetBulkFileStatistics));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}

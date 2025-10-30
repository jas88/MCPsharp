using System.Text.Json;
using System.Linq;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Partial class for bulk editing MCP tools
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Perform regex search and replace across multiple files in parallel
    /// </summary>
    /// <param name="arguments">Tool arguments containing files, regexPattern, replacement, and optional parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with bulk replacement details</returns>
    private async Task<ToolCallResult> ExecuteBulkReplace(JsonDocument arguments, CancellationToken cancellationToken)
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

            var regexPattern = arguments.RootElement.GetProperty("regexPattern").GetString();
            var replacement = arguments.RootElement.GetProperty("replacement").GetString();

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            if (string.IsNullOrEmpty(regexPattern))
            {
                return new ToolCallResult { Success = false, Error = "Regex pattern is required" };
            }

            if (string.IsNullOrEmpty(replacement))
            {
                return new ToolCallResult { Success = false, Error = "Replacement text is required" };
            }

            // Parse optional parameters
            var maxParallelism = arguments.RootElement.TryGetProperty("maxParallelism", out var maxParallelElement)
                ? maxParallelElement.GetInt32()
                : Environment.ProcessorCount;

            var createBackups = arguments.RootElement.TryGetProperty("createBackups", out var backupsElement)
                ? backupsElement.GetBoolean()
                : true;

            var previewMode = arguments.RootElement.TryGetProperty("previewMode", out var previewElement)
                ? previewElement.GetBoolean()
                : false;

            var excludedFiles = new List<string>();
            if (arguments.RootElement.TryGetProperty("excludedFiles", out var excludedElement))
            {
                foreach (var excludedFileElement in excludedElement.EnumerateArray())
                {
                    var excludedFile = excludedFileElement.GetString();
                    if (!string.IsNullOrEmpty(excludedFile))
                    {
                        excludedFiles.Add(excludedFile);
                    }
                }
            }

            var options = new BulkEditOptions
            {
                MaxParallelism = maxParallelism,
                CreateBackups = createBackups,
                PreviewMode = previewMode,
                ExcludedFiles = excludedFiles
            };

            var result = await _bulkEditService.BulkReplaceAsync(files, regexPattern, replacement, options, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Edit files based on content conditions (e.g., file contains specific text)
    /// </summary>
    /// <param name="arguments">Tool arguments containing files, conditionType, pattern, edits, and optional parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with conditional edit details</returns>
    private async Task<ToolCallResult> ExecuteConditionalEdit(JsonDocument arguments, CancellationToken cancellationToken)
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

            var conditionType = arguments.RootElement.GetProperty("conditionType").GetString();
            var pattern = arguments.RootElement.GetProperty("pattern").GetString();
            var negate = arguments.RootElement.TryGetProperty("negate", out var negateElement) && negateElement.GetBoolean();

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            if (string.IsNullOrEmpty(conditionType))
            {
                return new ToolCallResult { Success = false, Error = "Condition type is required" };
            }

            if (string.IsNullOrEmpty(pattern))
            {
                return new ToolCallResult { Success = false, Error = "Pattern is required" };
            }

            // Parse edits array
            var editsArray = arguments.RootElement.GetProperty("edits");
            var edits = new List<FileEdit>();
            foreach (var editElement in editsArray.EnumerateArray())
            {
                var edit = JsonSerializer.Deserialize<FileEdit>(editElement.GetRawText());
                if (edit != null)
                {
                    edits.Add(edit);
                }
            }

            if (edits.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one edit operation is required" };
            }

            // Create BulkEditCondition from parameters
            var condition = new BulkEditCondition
            {
                ConditionType = Enum.Parse<BulkConditionType>(conditionType),
                Pattern = pattern,
                Negate = negate
            };

            // Convert FileEdit to TextEdit using appropriate concrete types
            var textEdits = edits.Select<FileEdit, TextEdit>(e =>
            {
                // Determine the type of edit based on the Type property
                return e.Type?.ToLowerInvariant() switch
                {
                    "insert" or "insertion" => new InsertEdit
                    {
                        NewText = e.NewText ?? string.Empty,
                        StartLine = e.StartLine.HasValue ? e.StartLine.Value : 0,
                        EndLine = e.EndLine.HasValue ? e.EndLine.Value : 0,
                        StartColumn = e.StartColumn.HasValue ? e.StartColumn.Value : 0,
                        EndColumn = e.EndColumn.HasValue ? e.EndColumn.Value : 0
                    },
                    "delete" or "deletion" => new DeleteEdit
                    {
                        NewText = e.NewText ?? string.Empty,
                        StartLine = e.StartLine.HasValue ? e.StartLine.Value : 0,
                        EndLine = e.EndLine.HasValue ? e.EndLine.Value : 0,
                        StartColumn = e.StartColumn.HasValue ? e.StartColumn.Value : 0,
                        EndColumn = e.EndColumn.HasValue ? e.EndColumn.Value : 0
                    },
                    "replace" or "replacement" or _ => new ReplaceEdit
                    {
                        NewText = e.NewText ?? string.Empty,
                        StartLine = e.StartLine.HasValue ? e.StartLine.Value : 0,
                        EndLine = e.EndLine.HasValue ? e.EndLine.Value : 0,
                        StartColumn = e.StartColumn.HasValue ? e.StartColumn.Value : 0,
                        EndColumn = e.EndColumn.HasValue ? e.EndColumn.Value : 0
                    }
                };
            }).ToList();

            var options = new BulkEditOptions
            {
                CreateBackups = true,
                PreviewMode = false,
                MaxParallelism = Environment.ProcessorCount
            };

            var result = await _bulkEditService.ConditionalEditAsync(files, condition, textEdits, options, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Perform pattern-based code refactoring across multiple files
    /// </summary>
    /// <param name="arguments">Tool arguments containing files, refactorType, targetPattern, replacementPattern, and optional parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with refactoring details</returns>
    private async Task<ToolCallResult> ExecuteBatchRefactor(JsonDocument arguments, CancellationToken cancellationToken)
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

            var refactorType = arguments.RootElement.GetProperty("refactorType").GetString();
            var targetPattern = arguments.RootElement.GetProperty("targetPattern").GetString();
            var replacementPattern = arguments.RootElement.GetProperty("replacementPattern").GetString();

            if (files.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one file pattern is required" };
            }

            if (string.IsNullOrEmpty(refactorType))
            {
                return new ToolCallResult { Success = false, Error = "Refactor type is required" };
            }

            if (string.IsNullOrEmpty(targetPattern))
            {
                return new ToolCallResult { Success = false, Error = "Target pattern is required" };
            }

            if (string.IsNullOrEmpty(replacementPattern))
            {
                return new ToolCallResult { Success = false, Error = "Replacement pattern is required" };
            }

            // Create BulkRefactorPattern from parameters
            var refactorPattern = new BulkRefactorPattern
            {
                RefactorType = Enum.Parse<BulkRefactorType>(refactorType),
                TargetPattern = targetPattern,
                ReplacementPattern = replacementPattern
            };

            var options = new BulkEditOptions
            {
                CreateBackups = true,
                PreviewMode = false,
                MaxParallelism = Environment.ProcessorCount
            };

            var result = await _bulkEditService.BatchRefactorAsync(files, refactorPattern, options, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Perform coordinated edits across multiple files with dependency management
    /// </summary>
    /// <param name="arguments">Tool arguments containing operations array with file patterns and edits</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with multi-file edit details</returns>
    private async Task<ToolCallResult> ExecuteMultiFileEdit(JsonDocument arguments, CancellationToken cancellationToken)
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
            // Parse operations array
            var operationsArray = arguments.RootElement.GetProperty("operations");
            var operations = new List<MultiFileOperation>();
            foreach (var operationElement in operationsArray.EnumerateArray())
            {
                var operation = JsonSerializer.Deserialize<MultiFileOperation>(operationElement.GetRawText());
                if (operation != null)
                {
                    operations.Add(operation);
                }
            }

            if (operations.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one operation is required" };
            }

            // Convert MultiFileOperation to MultiFileEditOperation
          var editOperations = operations.Select(op => new MultiFileEditOperation
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

          var options = new BulkEditOptions
          {
              CreateBackups = true,
              PreviewMode = false,
              MaxParallelism = Environment.ProcessorCount
          };

          var result = await _bulkEditService.MultiFileEditAsync(editOperations, options, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

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
                        ConditionType = Enum.Parse<BulkConditionType>(conditionType),
                        Pattern = pattern
                    },
                    Options = new BulkEditOptions { PreviewMode = true }
                };
            }
            else if (bulkEditOpType == BulkEditOperationType.MultiFileEdit)
            {
                var operationsArray = arguments.RootElement.GetProperty("operations");
                var multiFileOperations = JsonSerializer.Deserialize<List<MultiFileOperation>>(operationsArray.GetRawText()) ?? new List<MultiFileOperation>();

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
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Rollback a previous bulk edit operation using created backups
    /// </summary>
    /// <param name="arguments">Tool arguments containing rollbackId</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Tool execution result with rollback details</returns>
    private async Task<ToolCallResult> ExecuteRollbackBulkEdit(JsonDocument arguments, CancellationToken cancellationToken)
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
            var rollbackId = arguments.RootElement.GetProperty("rollbackId").GetString();

            if (string.IsNullOrEmpty(rollbackId))
            {
                return new ToolCallResult { Success = false, Error = "Rollback ID is required" };
            }

            var result = await _bulkEditService.RollbackBulkEditAsync(rollbackId, cancellationToken);

            return new ToolCallResult { Success = result.Success, Result = result, Error = result.Error };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get list of available rollback sessions that can be used to undo changes
    /// </summary>
    /// <param name="arguments">Tool arguments (no parameters required)</param>
    /// <returns>Tool execution result with list of available rollbacks</returns>
    private async Task<ToolCallResult> ExecuteGetAvailableRollbacks(JsonDocument arguments, CancellationToken cancellationToken)
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
            var rollbacks = await _bulkEditService.GetAvailableRollbacksAsync();

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    rollbacks = rollbacks.Select(r => new
                    {
                        rollbackId = r.RollbackId,
                        operationType = r.OperationType,
                        timestamp = r.Timestamp,
                        filesAffected = r.FilesAffected,
                        changesCount = r.ChangesCount,
                        description = r.Description
                    }).ToArray(),
                    count = rollbacks.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Validate a bulk edit request before execution to identify potential issues
    /// </summary>
    /// <param name="arguments">Tool arguments containing operation type, files, and operation-specific parameters</param>
    /// <returns>Tool execution result with validation details</returns>
    private async Task<ToolCallResult> ExecuteValidateBulkEdit(JsonDocument arguments, CancellationToken cancellationToken)
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
                        ConditionType = Enum.Parse<BulkConditionType>(conditionType),
                        Pattern = pattern
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
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Estimate the impact and complexity of a bulk edit operation before execution
    /// </summary>
    /// <param name="arguments">Tool arguments containing operation type and files</param>
    /// <returns>Tool execution result with impact estimation</returns>
    private async Task<ToolCallResult> ExecuteEstimateBulkImpact(JsonDocument arguments, CancellationToken cancellationToken)
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
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Get statistics about files for planning bulk operations (sizes, counts, types)
    /// </summary>
    /// <param name="arguments">Tool arguments containing files array</param>
    /// <returns>Tool execution result with file statistics</returns>
    private async Task<ToolCallResult> ExecuteGetBulkFileStatistics(JsonDocument arguments, CancellationToken cancellationToken)
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
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}
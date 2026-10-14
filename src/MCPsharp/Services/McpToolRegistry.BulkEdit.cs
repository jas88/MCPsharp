using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;

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

            var createBackups = !arguments.RootElement.TryGetProperty("createBackups", out var backupsElement) || backupsElement.GetBoolean();

            var previewMode = arguments.RootElement.TryGetProperty("previewMode", out var previewElement) && previewElement.GetBoolean();

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
            LogToolOperationFailed(_logger, ex, nameof(ExecuteBulkReplace));
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
                var edit = JsonSerializer.Deserialize<FileEdit>(editElement.GetRawText(), ToolArgumentJsonOptions);
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
            LogToolOperationFailed(_logger, ex, nameof(ExecuteConditionalEdit));
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
            LogToolOperationFailed(_logger, ex, nameof(ExecuteBatchRefactor));
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
                var operation = JsonSerializer.Deserialize<MultiFileOperation>(operationElement.GetRawText(), ToolArgumentJsonOptions);
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
            LogToolOperationFailed(_logger, ex, nameof(ExecuteMultiFileEdit));
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

}

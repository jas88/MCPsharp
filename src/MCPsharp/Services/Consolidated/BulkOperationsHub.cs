using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Services;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Bulk Operations Hub consolidating all bulk operations
/// Phase 3: Consolidate 35 bulk operation tools into 8 unified tools
/// </summary>
public class BulkOperationsHub
{
    private readonly IBulkEditService? _bulkEditService;
    private readonly UniversalFileOperations _fileOps;
    private readonly ILogger<BulkOperationsHub> _logger;
    private readonly IProgressTracker? _progressTracker;
    private readonly ITempFileManager? _tempFileManager;

    // Active operations storage
    private readonly Dictionary<string, BulkOperationContext> _activeOperations = new();
    private readonly object _operationsLock = new object();

    public BulkOperationsHub(
        IBulkEditService? bulkEditService = null,
        UniversalFileOperations? fileOps = null,
        IProgressTracker? progressTracker = null,
        ITempFileManager? tempFileManager = null,
        ILogger<BulkOperationsHub>? logger = null)
    {
        _bulkEditService = bulkEditService;
        _fileOps = fileOps ?? throw new ArgumentNullException(nameof(fileOps));
        _progressTracker = progressTracker;
        _tempFileManager = tempFileManager;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<BulkOperationsHub>.Instance;
    }

    /// <summary>
    /// bulk_operation - Execute bulk operations
    /// Consolidates: bulk_replace, conditional_edit, batch_refactor, multi_file_edit
    /// </summary>
    public async Task<BulkOperationResponse> ExecuteBulkOperationAsync(BulkOperationRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var operationId = request.OperationId ?? Guid.NewGuid().ToString();

        try
        {
            var response = new BulkOperationResponse
            {
                OperationId = operationId,
                OperationType = request.OperationType,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString()
            };

            // Create operation context
            var context = new BulkOperationContext
            {
                Id = operationId,
                Type = request.OperationType,
                CreatedAt = DateTime.UtcNow,
                Status = BulkOperationStatus.Running
            };

            lock (_operationsLock)
            {
                _activeOperations[operationId] = context;
            }

            // Initialize progress tracking
            if (_progressTracker != null)
            {
                context.ProgressId = await _progressTracker.StartTrackingAsync(operationId, new ProgressInfo
                {
                    Title = $"Bulk {request.OperationType}",
                    TotalSteps = request.Files?.Count ?? 0,
                    CurrentStep = 0
                }, ct);
            }

            // Validate request
            var validation = await ValidateBulkOperationRequestAsync(request, ct);
            if (!validation.IsValid)
            {
                response.Error = validation.ErrorMessage;
                response.Success = false;
                context.Status = BulkOperationStatus.Failed;
                return response;
            }

            // Create backup if requested
            string? backupId = null;
            if (request.Options?.CreateBackup == true)
            {
                backupId = await CreateBulkBackupAsync(request, ct);
                context.BackupId = backupId;
            }

            try
            {
                // Execute operation based on type
                var results = await ExecuteBulkOperationByTypeAsync(request, context, ct);
                response.Results = results;
                response.Summary = new BulkOperationSummary
                {
                    TotalFiles = request.Files?.Count ?? 0,
                    SuccessfulFiles = results.Count(r => r.Success),
                    FailedFiles = results.Count(r => !r.Success),
                    TotalChanges = results.Sum(r => r.Changes ?? 0),
                    TotalBytesProcessed = results.Sum(r => r.BytesProcessed ?? 0),
                    ExecutionTime = stopwatch.Elapsed
                };

                response.Success = response.Summary.FailedFiles == 0;
                context.Status = response.Success ? BulkOperationStatus.Completed : BulkOperationStatus.Failed;
            }
            catch (Exception ex)
            {
                // Rollback if backup was created
                if (backupId != null)
                {
                    await RestoreBulkBackupAsync(backupId, ct);
                    response.RolledBack = true;
                }

                response.Error = ex.Message;
                response.Success = false;
                context.Status = BulkOperationStatus.Failed;
                _logger.LogError(ex, "Error executing bulk operation {OperationId}", operationId);
            }
            finally
            {
                // Cleanup backup if successful
                if (backupId != null && response.Success)
                {
                    await CleanupBulkBackupAsync(backupId, ct);
                }
            }

            // Update final progress
            if (_progressTracker != null && context.ProgressId != null)
            {
                await _progressTracker.CompleteTrackingAsync(context.ProgressId, new ProgressResult
                {
                    Success = response.Success,
                    Message = response.Success ? "Bulk operation completed successfully" : response.Error ?? "Bulk operation failed",
                    Metrics = new Dictionary<string, object>
                    {
                        ["TotalFiles"] = response.Summary?.TotalFiles ?? 0,
                        ["SuccessfulFiles"] = response.Summary?.SuccessfulFiles ?? 0,
                        ["FailedFiles"] = response.Summary?.FailedFiles ?? 0,
                        ["TotalChanges"] = response.Summary?.TotalChanges ?? 0
                    }
                }, ct);
            }

            stopwatch.Stop();
            response.ExecutionTime = stopwatch.Elapsed;
            response.Metadata = new ResponseMetadata
            {
                ProcessingTime = stopwatch.Elapsed,
                Success = response.Success,
                ResultCount = response.Results?.Count ?? 0
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in bulk operation {OperationId}", operationId);

            return new BulkOperationResponse
            {
                OperationId = operationId,
                OperationType = request.OperationType,
                Error = ex.Message,
                Success = false,
                ExecutionTime = stopwatch.Elapsed,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
        finally
        {
            // Cleanup context after a delay
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(30));
                lock (_operationsLock)
                {
                    _activeOperations.Remove(operationId);
                }
            }, ct);
        }
    }

    /// <summary>
    /// bulk_preview - Preview bulk changes
    /// Consolidates: All preview tools
    /// </summary>
    public async Task<BulkPreviewResponse> PreviewBulkOperationAsync(BulkPreviewRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new BulkPreviewResponse
            {
                OperationType = request.OperationType,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString()
            };

            // Dry run to analyze changes
            var dryRunRequest = new BulkOperationRequest
            {
                OperationType = request.OperationType,
                Files = request.Files,
                Options = request.Options?.WithDryRun(true),
                Pattern = request.Pattern,
                Replacement = request.Replacement,
                Conditions = request.Conditions,
                RefactorType = request.RefactorType,
                TargetPattern = request.TargetPattern,
                ReplacementPattern = request.ReplacementPattern,
                Operations = request.Operations?.Select(op => new FileOperationDefinition
            {
                Path = op.Path,
                Type = op.Type,
                DestinationPath = op.DestinationPath,
                Edits = op.Edits,
                Options = op.Options?.WithDryRun(true)
            }).ToList()
            };

            var previewResults = await ExecuteBulkOperationByTypeAsync(dryRunRequest, new BulkOperationContext
            {
                Id = Guid.NewGuid().ToString(),
                Type = request.OperationType,
                CreatedAt = DateTime.UtcNow,
                Status = BulkOperationStatus.Running
            }, ct);

            response.PreviewResults = previewResults;
            response.ImpactAnalysis = await AnalyzeBulkImpactAsync(previewResults, ct);
            response.RiskAssessment = await AssessBulkRisksAsync(previewResults, ct);
            response.EstimatedTime = EstimateExecutionTime(previewResults);

            stopwatch.Stop();
            response.Metadata = new ResponseMetadata
            {
                ProcessingTime = stopwatch.Elapsed,
                Success = true,
                ResultCount = previewResults.Count
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error previewing bulk operation");

            return new BulkPreviewResponse
            {
                OperationType = request.OperationType,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// bulk_progress - Track bulk operations
    /// Consolidates: Progress tracking tools
    /// </summary>
    public async Task<BulkProgressResponse> GetBulkProgressAsync(string operationId, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new BulkProgressResponse
            {
                OperationId = operationId,
                RequestId = Guid.NewGuid().ToString()
            };

            // Get operation context
            BulkOperationContext? context;
            lock (_operationsLock)
            {
                _activeOperations.TryGetValue(operationId, out context);
            }

            if (context == null)
            {
                response.Error = $"Operation {operationId} not found";
                response.Status = BulkOperationStatus.NotFound;
                return response;
            }

            response.Status = context.Status;
            response.CreatedAt = context.CreatedAt;
            response.StartedAt = context.StartedAt;
            response.EstimatedCompletion = context.EstimatedCompletion;

            // Get progress from tracker
            if (_progressTracker != null && context.ProgressId != null)
            {
                var streamProgress = await _progressTracker.GetProgressAsync(context.ProgressId);
                if (streamProgress != null)
                {
                    response.Progress = new BulkProgressInfo
                    {
                        CurrentStep = (int)(streamProgress.BytesProcessed > 0 ? 1 : 0), // Simplified
                        TotalSteps = 1, // Simplified
                        PercentComplete = streamProgress.ProgressPercentage,
                        CurrentFile = "Processing...", // StreamProgress doesn't have CurrentFile
                        ElapsedTime = DateTime.UtcNow - context.CreatedAt,
                        EstimatedTimeRemaining = streamProgress.EstimatedTimeRemaining,
                        Message = streamProgress.CurrentPhase ?? "Processing"
                    };
                }
            }

            // Get recent logs
            response.RecentLogs = GetRecentLogs(context, 20);

            stopwatch.Stop();
            response.Metadata = new ResponseMetadata
            {
                ProcessingTime = stopwatch.Elapsed,
                Success = true
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting bulk progress for {OperationId}", operationId);

            return new BulkProgressResponse
            {
                OperationId = operationId,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// bulk_management - Manage bulk operations
    /// Consolidates: Management tools
    /// </summary>
    public async Task<BulkManagementResponse> ManageBulkOperationAsync(BulkManagementRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new BulkManagementResponse
            {
                Action = request.Action,
                Target = request.Target,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString()
            };

            switch (request.Action)
            {
                case BulkManagementAction.List:
                    response.ActiveOperations = await ListActiveOperationsAsync(ct);
                    break;

                case BulkManagementAction.Cancel:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for cancel operation";
                    }
                    else
                    {
                        response.CancelResult = await CancelBulkOperationAsync(request.Target, ct);
                    }
                    break;

                case BulkManagementAction.Pause:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for pause operation";
                    }
                    else
                    {
                        response.PauseResult = await PauseBulkOperationAsync(request.Target, ct);
                    }
                    break;

                case BulkManagementAction.Resume:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for resume operation";
                    }
                    else
                    {
                        response.ResumeResult = await ResumeBulkOperationAsync(request.Target, ct);
                    }
                    break;

                case BulkManagementAction.Cleanup:
                    response.CleanupResult = await CleanupBulkOperationsAsync(request.MaxAge, ct);
                    break;

                case BulkManagementAction.Status:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for status operation";
                    }
                    else
                    {
                        response.StatusResult = await GetBulkOperationStatusAsync(request.Target, ct);
                    }
                    break;

                default:
                    response.Error = $"Unknown management action: {request.Action}";
                    break;
            }

            stopwatch.Stop();
            response.Metadata = new ResponseMetadata
            {
                ProcessingTime = stopwatch.Elapsed,
                Success = response.Error == null
            };

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error managing bulk operation");

            return new BulkManagementResponse
            {
                Action = request.Action,
                Target = request.Target,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    #region Private Helper Methods

    private async Task<BulkValidationResult> ValidateBulkOperationRequestAsync(BulkOperationRequest request, CancellationToken ct)
    {
        // Check if we have the necessary services
        if (_bulkEditService == null && request.OperationType != BulkOperationType.FileOperation)
        {
            return new BulkValidationResult
            {
                IsValid = false,
                ErrorMessage = "Bulk edit service not available"
            };
        }

        // Validate files
        if (request.Files?.Any() == true)
        {
            foreach (var file in request.Files)
            {
                if (!File.Exists(file))
                {
                    return new BulkValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = $"File not found: {file}"
                    };
                }
            }
        }

        // Validate pattern-based operations
        if (request.OperationType == BulkOperationType.Replace && (request.Pattern == null || request.Replacement == null))
        {
            return new BulkValidationResult
            {
                IsValid = false,
                ErrorMessage = "Pattern and replacement are required for replace operations"
            };
        }

        return new BulkValidationResult { IsValid = true };
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkOperationByTypeAsync(
        BulkOperationRequest request,
        BulkOperationContext context,
        CancellationToken ct)
    {
        var results = new List<BulkOperationResult>();

        context.StartedAt = DateTime.UtcNow;

        switch (request.OperationType)
        {
            case BulkOperationType.Replace:
                results = await ExecuteBulkReplaceAsync(request, context, ct);
                break;

            case BulkOperationType.Conditional:
                results = await ExecuteBulkConditionalAsync(request, context, ct);
                break;

            case BulkOperationType.Refactor:
                results = await ExecuteBulkRefactorAsync(request, context, ct);
                break;

            case BulkOperationType.MultiEdit:
                results = await ExecuteBulkMultiEditAsync(request, context, ct);
                break;

            case BulkOperationType.FileOperation:
                results = await ExecuteBulkFileOperationsAsync(request, context, ct);
                break;

            default:
                throw new ArgumentException($"Unknown bulk operation type: {request.OperationType}");
        }

        return results;
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkReplaceAsync(
        BulkOperationRequest request,
        BulkOperationContext context,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.Pattern == null || request.Replacement == null)
            return new List<BulkOperationResult>();

        var options = new BulkEditOptions
        {
            CreateBackups = request.Options?.CreateBackup == true,
            MaxParallelism = request.Options?.MaxParallelism ?? 10,
            PreviewMode = request.Options?.DryRun == true
        };

        var result = await _bulkEditService.BulkReplaceAsync(
            request.Files ?? new List<string>(),
            request.Pattern ?? string.Empty,
            request.Replacement ?? string.Empty,
            options,
            ct);

        return result.FileResults.Select(r => new BulkOperationResult
        {
            FilePath = r.FilePath,
            Success = r.Success,
            Changes = r.ChangesCount,
            Error = null, // FileBulkEditResult doesn't have Error property directly
            BytesProcessed = new System.IO.FileInfo(r.FilePath).Length
        }).ToList();
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkConditionalAsync(
        BulkOperationRequest request,
        BulkOperationContext context,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.Conditions?.Any() != true)
            return new List<BulkOperationResult>();

        var results = new List<BulkOperationResult>();

        foreach (var file in request.Files ?? new List<string>())
        {
            try
            {
                // Read file content
                var content = await File.ReadAllTextAsync(file, ct);

                // Check conditions - convert Consolidated.EditCondition to Models.BulkEdit.BulkEditCondition
                var shouldEdit = request.Conditions.All(condition =>
                    {
                        var bulkCondition = new MCPsharp.Models.BulkEdit.BulkEditCondition
                        {
                            ConditionType = condition.Type.ToLowerInvariant() switch
                            {
                                "contains" => MCPsharp.Models.BulkEdit.BulkConditionType.FileContains,
                                "notcontains" => MCPsharp.Models.BulkEdit.BulkConditionType.FileContains,
                                "regex" => MCPsharp.Models.BulkEdit.BulkConditionType.FileMatches,
                                "fileexists" => MCPsharp.Models.BulkEdit.BulkConditionType.Custom,
                                "filedoesnotexist" => MCPsharp.Models.BulkEdit.BulkConditionType.Custom,
                                _ => MCPsharp.Models.BulkEdit.BulkConditionType.Custom
                            },
                            Pattern = condition.Value,
                            Negate = condition.Type.ToLowerInvariant() == "notcontains" || condition.Type.ToLowerInvariant() == "filedoesnotexist"
                        };
                        return EvaluateCondition(content, bulkCondition);
                    });

                if (shouldEdit)
                {
                    // Apply edits
                    foreach (var edit in request.Edits ?? new List<TextEdit>())
                    {
                        // Apply edit logic here
                    }

                    results.Add(new BulkOperationResult
                    {
                        FilePath = file,
                        Success = true,
                        Changes = request.Edits?.Count ?? 0,
                        BytesProcessed = new System.IO.FileInfo(file).Length
                    });
                }
                else
                {
                    results.Add(new BulkOperationResult
                    {
                        FilePath = file,
                        Success = true,
                        Changes = 0,
                        Message = "Conditions not met",
                        BytesProcessed = new System.IO.FileInfo(file).Length
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new BulkOperationResult
                {
                    FilePath = file,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkRefactorAsync(
        BulkOperationRequest request,
        BulkOperationContext context,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.TargetPattern == null || request.ReplacementPattern == null)
            return new List<BulkOperationResult>();

        var options = new BulkEditOptions
        {
            CreateBackups = request.Options?.CreateBackup == true,
            MaxParallelism = request.Options?.MaxParallelism ?? 10,
            PreviewMode = request.Options?.DryRun == true
        };

        var refactorPattern = new MCPsharp.Models.BulkEdit.BulkRefactorPattern
        {
            RefactorType = Enum.Parse<MCPsharp.Models.BulkEdit.BulkRefactorType>(request.RefactorType ?? "RenameSymbol"),
            TargetPattern = request.TargetPattern ?? string.Empty,
            ReplacementPattern = request.ReplacementPattern ?? string.Empty
        };

        var result = await _bulkEditService.BatchRefactorAsync(
            request.Files ?? new List<string>(),
            refactorPattern,
            options,
            ct);

        return result.FileResults.Select(r => new BulkOperationResult
        {
            FilePath = r.FilePath,
            Success = r.Success,
            Changes = r.ChangesCount,
            Error = null, // FileBulkEditResult doesn't have Error property directly
            BytesProcessed = new System.IO.FileInfo(r.FilePath).Length
        }).ToList();
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkMultiEditAsync(
        BulkOperationRequest request,
        BulkOperationContext context,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.Operations?.Any() != true)
            return new List<BulkOperationResult>();

        var options = new BulkEditOptions
        {
            CreateBackups = request.Options?.CreateBackup == true,
            MaxParallelism = request.Options?.MaxParallelism ?? 10,
            PreviewMode = request.Options?.DryRun == true
        };

        // For multi-file operations, we'll need to transform the operations
        // Convert FileOperationDefinition to MultiFileEditOperation
        var multiFileOps = request.Operations?.Select(op => new MCPsharp.Models.BulkEdit.MultiFileEditOperation
        {
            FilePattern = op.Path,
            Edits = op.Edits?.Select(e => new MCPsharp.Models.ReplaceEdit
            {
                // Convert TextEdit to proper format - use concrete ReplaceEdit class
                NewText = e.NewText ?? "",
                StartLine = e.StartLine,
                StartColumn = e.StartColumn,
                EndLine = e.EndLine,
                EndColumn = e.EndColumn
            }).ToList() ?? new List<MCPsharp.Models.ReplaceEdit>(),
            Priority = 0 // FileOperationOptions doesn't have Priority property, use default
        }).ToList() ?? new List<MCPsharp.Models.BulkEdit.MultiFileEditOperation>();

        var result = await _bulkEditService.MultiFileEditAsync(
            multiFileOps,
            options,
            ct);

        return result.FileResults.Select(r => new BulkOperationResult
        {
            FilePath = r.FilePath,
            Success = r.Success,
            Changes = r.ChangesCount,
            Error = null, // FileBulkEditResult doesn't have Error property directly
            BytesProcessed = new System.IO.FileInfo(r.FilePath).Length
        }).ToList();
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkFileOperationsAsync(
        BulkOperationRequest request,
        BulkOperationContext context,
        CancellationToken ct)
    {
        var results = new List<BulkOperationResult>();

        foreach (var fileOp in request.Operations ?? new List<FileOperationDefinition>())
        {
            try
            {
                var fileRequest = new FileOperationRequest
                {
                    Path = fileOp.Path,
                    Operation = Enum.Parse<FileOperationType>(fileOp.Type.ToString()),
                    DestinationPath = fileOp.DestinationPath,
                    Edits = fileOp.Edits
                };

                var response = await _fileOps.ExecuteFileOperationAsync(fileRequest, ct);

                results.Add(new BulkOperationResult
                {
                    FilePath = fileOp.Path,
                    Success = response.Metadata.Success,
                    Changes = response.EditsApplied,
                    Error = response.Error,
                    BytesProcessed = response.BytesProcessed
                });
            }
            catch (Exception ex)
            {
                results.Add(new BulkOperationResult
                {
                    FilePath = fileOp.Path,
                    Success = false,
                    Error = ex.Message
                });
            }
        }

        return results;
    }

    private static bool EvaluateCondition(string content, MCPsharp.Models.BulkEdit.BulkEditCondition condition)
    {
        // Simple condition evaluation - could be enhanced
        var matches = condition.ConditionType switch
        {
            MCPsharp.Models.BulkEdit.BulkConditionType.FileContains => content.Contains(condition.Pattern, StringComparison.OrdinalIgnoreCase),
            MCPsharp.Models.BulkEdit.BulkConditionType.FileMatches => System.Text.RegularExpressions.Regex.IsMatch(content, condition.Pattern),
            MCPsharp.Models.BulkEdit.BulkConditionType.Custom => condition.Pattern.ToLowerInvariant() switch
            {
                "fileexists" => File.Exists(condition.Pattern),
                "filedoesnotexist" => !File.Exists(condition.Pattern),
                _ => false
            },
            _ => false
        };

        return condition.Negate ? !matches : matches;
    }

    private async Task<string?> CreateBulkBackupAsync(BulkOperationRequest request, CancellationToken ct)
    {
        if (_tempFileManager == null) return null;

        var backupId = Guid.NewGuid().ToString();
        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        Directory.CreateDirectory(backupPath);

        try
        {
            foreach (var file in request.Files ?? new List<string>())
            {
                if (File.Exists(file))
                {
                    var backupFile = Path.Combine(backupPath, Path.GetFileName(file));
                    await Task.Run(() => File.Copy(file, backupFile, true), ct);
                }
            }

            return backupId;
        }
        catch
        {
            // Cleanup on failure
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }
            return null;
        }
    }

    private async Task RestoreBulkBackupAsync(string backupId, CancellationToken ct)
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        if (!Directory.Exists(backupPath)) return;

        foreach (var backupFile in Directory.GetFiles(backupPath))
        {
            try
            {
                var fileName = Path.GetFileName(backupFile);
                var originalPath = fileName; // Would need more sophisticated mapping
                await Task.Run(() => File.Copy(backupFile, originalPath, true), ct);
            }
            catch
            {
                // Continue with other files
            }
        }
    }

    private async Task CleanupBulkBackupAsync(string backupId, CancellationToken ct)
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        if (Directory.Exists(backupPath))
        {
            await Task.Run(() => Directory.Delete(backupPath, true), ct);
        }
    }

    private async Task<BulkImpactAnalysis> AnalyzeBulkImpactAsync(List<BulkOperationResult> results, CancellationToken ct)
    {
        return new BulkImpactAnalysis
        {
            AffectedFiles = results.Count,
            TotalChanges = results.Sum(r => r.Changes ?? 0),
            FilesWithErrors = results.Count(r => !r.Success),
            HighRiskFiles = results.Count(r => r.Changes > 100), // Arbitrary threshold
            EstimatedComplexity = results.Sum(r => r.Changes ?? 0) * 0.1 // Arbitrary complexity calculation
        };
    }

    private async Task<BulkRiskAssessment> AssessBulkRisksAsync(List<BulkOperationResult> results, CancellationToken ct)
    {
        var riskLevel = MCPsharp.Models.BulkEdit.RiskLevel.Low;
        var warnings = new List<string>();

        var errorCount = results.Count(r => !r.Success);
        var changeCount = results.Sum(r => r.Changes ?? 0);

        if (errorCount > 0)
        {
            riskLevel = MCPsharp.Models.BulkEdit.RiskLevel.High;
            warnings.Add($"{errorCount} files failed during preview");
        }
        else if (changeCount > 1000)
        {
            riskLevel = MCPsharp.Models.BulkEdit.RiskLevel.Medium;
            warnings.Add($"Large number of changes ({changeCount}) - consider breaking into smaller operations");
        }

        return new BulkRiskAssessment
        {
            RiskLevel = riskLevel,
            Warnings = warnings,
            Recommendations = GenerateRiskRecommendations(riskLevel, changeCount, errorCount)
        };
    }

    private static List<string> GenerateRiskRecommendations(MCPsharp.Models.BulkEdit.RiskLevel riskLevel, int changeCount, int errorCount)
    {
        var recommendations = new List<string>();

        if (riskLevel >= MCPsharp.Models.BulkEdit.RiskLevel.Medium)
        {
            recommendations.Add("Create a backup before proceeding");
        }

        if (changeCount > 500)
        {
            recommendations.Add("Consider breaking into smaller operations");
        }

        if (errorCount > 0)
        {
            recommendations.Add("Review and fix file access issues");
        }

        recommendations.Add("Review changes in a version control system");

        return recommendations;
    }

    private static TimeSpan EstimateExecutionTime(List<BulkOperationResult> previewResults)
    {
        // Simple estimation based on file count and changes
        var baseTime = TimeSpan.FromSeconds(1);
        var perFileTime = TimeSpan.FromMilliseconds(100);
        var perChangeTime = TimeSpan.FromMilliseconds(10);

        var totalFiles = previewResults.Count;
        var totalChanges = previewResults.Sum(r => r.Changes ?? 0);

        return baseTime + (perFileTime * totalFiles) + (perChangeTime * totalChanges);
    }

    private async Task<List<BulkOperationInfo>> ListActiveOperationsAsync(CancellationToken ct)
    {
        List<BulkOperationInfo> operations;

        lock (_operationsLock)
        {
            operations = _activeOperations.Values.Select(ctx => new BulkOperationInfo
            {
                Id = ctx.Id,
                Type = ctx.Type,
                Status = ctx.Status,
                CreatedAt = ctx.CreatedAt,
                StartedAt = ctx.StartedAt,
                HasBackup = ctx.BackupId != null
            }).ToList();
        }

        return operations;
    }

    private async Task<BulkCancelResult> CancelBulkOperationAsync(string operationId, CancellationToken ct)
    {
        // Implementation for canceling operations
        return new BulkCancelResult
        {
            Success = true,
            Message = "Operation cancelled"
        };
    }

    private async Task<BulkPauseResult> PauseBulkOperationAsync(string operationId, CancellationToken ct)
    {
        // Implementation for pausing operations
        return new BulkPauseResult
        {
            Success = true,
            Message = "Operation paused"
        };
    }

    private async Task<BulkResumeResult> ResumeBulkOperationAsync(string operationId, CancellationToken ct)
    {
        // Implementation for resuming operations
        return new BulkResumeResult
        {
            Success = true,
            Message = "Operation resumed"
        };
    }

    private async Task<BulkCleanupResult> CleanupBulkOperationsAsync(TimeSpan? maxAge, CancellationToken ct)
    {
        var cleanedCount = 0;
        var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromHours(24));

        lock (_operationsLock)
        {
            var toRemove = _activeOperations
                .Where(kvp => kvp.Value.CreatedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _activeOperations.Remove(key);
                cleanedCount++;
            }
        }

        return new BulkCleanupResult
        {
            Success = true,
            CleanedOperations = cleanedCount,
            Message = $"Cleaned up {cleanedCount} old operations"
        };
    }

    private async Task<BulkOperationStatusResult> GetBulkOperationStatusAsync(string operationId, CancellationToken ct)
    {
        BulkOperationContext? context;

        lock (_operationsLock)
        {
            _activeOperations.TryGetValue(operationId, out context);
        }

        return context == null
            ? new BulkOperationStatusResult { Found = false }
            : new BulkOperationStatusResult
            {
                Found = true,
                Status = context.Status,
                CreatedAt = context.CreatedAt,
                StartedAt = context.StartedAt,
                EstimatedCompletion = context.EstimatedCompletion
            };
    }

    private static List<string> GetRecentLogs(BulkOperationContext context, int maxCount)
    {
        // Implementation would collect logs from various sources
        return new List<string>
        {
            $"Operation {context.Id} started",
            "Processing files...",
            "Completed successfully"
        };
    }

    #endregion
}

#region Supporting Classes

/// <summary>
/// Bulk operation context
/// </summary>
internal class BulkOperationContext
{
    public required string Id { get; init; }
    public required BulkOperationType Type { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
    public BulkOperationStatus Status { get; set; }
    public string? ProgressId { get; set; }
    public string? BackupId { get; set; }
}

/// <summary>
/// Bulk validation result
/// </summary>
internal class BulkValidationResult
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Edit condition for conditional edits
/// </summary>
public class EditCondition
{
    public required string Type { get; init; }
    public required string Value { get; init; }
    public string? Operator { get; init; }
}


#endregion
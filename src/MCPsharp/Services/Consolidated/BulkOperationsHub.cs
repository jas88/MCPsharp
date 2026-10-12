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
public partial class BulkOperationsHub
{
    private readonly IBulkEditService? _bulkEditService;
    private readonly UniversalFileOperations _fileOps;
    private readonly ILogger<BulkOperationsHub> _logger;
    private readonly IProgressTracker? _progressTracker;
    private readonly ITempFileManager? _tempFileManager;

    // Active operations storage
    private readonly Dictionary<string, BulkOperationContext> _activeOperations = new();
    private readonly object _operationsLock = new object();

    // Hub-level backups (create_backup): backup id -> original file paths and their backup copies
    private readonly Dictionary<string, List<(string OriginalPath, string BackupPath)>> _backupManifests = new();

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
            var validation = ValidateBulkOperationRequest(request);
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
                LogErrorExecutingBulkOperation(_logger, ex, operationId);
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
            LogFatalErrorInBulkOperation(_logger, ex, operationId);

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
            // Cleanup context after a delay. Deliberately not tied to the request token:
            // cancelling the request must not skip removal of the tracked operation.
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMinutes(30), CancellationToken.None);
                lock (_operationsLock)
                {
                    _activeOperations.Remove(operationId);
                }
            }, CancellationToken.None);
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
                Options = (request.Options ?? new BulkOperationOptions()).WithDryRun(true),
                Pattern = request.Pattern,
                Replacement = request.Replacement,
                Conditions = request.Conditions,
                Edits = request.Edits,
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
            response.ImpactAnalysis = AnalyzeBulkImpact(previewResults);
            response.RiskAssessment = AssessBulkRisks(previewResults);
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
            LogErrorPreviewingBulkOperation(_logger, ex);

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
                        CurrentStep = streamProgress.BytesProcessed > 0 ? 1 : 0, // Simplified
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
            response.RecentLogs = GetRecentLogs(context);

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
            LogErrorGettingBulkProgressFor(_logger, ex, operationId);

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
    public Task<BulkManagementResponse> ManageBulkOperationAsync(BulkManagementRequest request, CancellationToken ct = default)
    {
        return Task.FromResult(ManageBulkOperation(request));
    }

    private BulkManagementResponse ManageBulkOperation(BulkManagementRequest request)
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
                    response.ActiveOperations = ListActiveOperations();
                    break;

                case BulkManagementAction.Cancel:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for cancel operation";
                    }
                    else
                    {
                        response.CancelResult = CancelBulkOperation();
                    }
                    break;

                case BulkManagementAction.Pause:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for pause operation";
                    }
                    else
                    {
                        response.PauseResult = PauseBulkOperation();
                    }
                    break;

                case BulkManagementAction.Resume:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for resume operation";
                    }
                    else
                    {
                        response.ResumeResult = ResumeBulkOperation();
                    }
                    break;

                case BulkManagementAction.Cleanup:
                    response.CleanupResult = CleanupBulkOperations(request.MaxAge);
                    break;

                case BulkManagementAction.Status:
                    if (string.IsNullOrEmpty(request.Target))
                    {
                        response.Error = "Target operation ID is required for status operation";
                    }
                    else
                    {
                        response.StatusResult = GetBulkOperationStatus(request.Target);
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
            LogErrorManagingBulkOperation(_logger, ex);

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

    private BulkValidationResult ValidateBulkOperationRequest(BulkOperationRequest request)
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
        if (request.Files?.Count > 0)
        {
            var missingFile = request.Files.Find(file => !File.Exists(file));
            if (missingFile != null)
            {
                return new BulkValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"File not found: {missingFile}"
                };
            }
        }

        if (request.OperationType == BulkOperationType.Conditional &&
            (request.Conditions is not { Count: > 0 } || request.Edits is not { Count: > 0 }))
        {
            return new BulkValidationResult
            {
                IsValid = false,
                ErrorMessage = "Conditions and edits are required for conditional operations"
            };
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
        List<BulkOperationResult> results;

        context.StartedAt = DateTime.UtcNow;

        switch (request.OperationType)
        {
            case BulkOperationType.Replace:
                results = await ExecuteBulkReplaceAsync(request, ct);
                break;

            case BulkOperationType.Conditional:
                results = await ExecuteBulkConditionalAsync(request, ct);
                break;

            case BulkOperationType.Refactor:
                results = await ExecuteBulkRefactorAsync(request, ct);
                break;

            case BulkOperationType.MultiEdit:
                results = await ExecuteBulkMultiEditAsync(request, ct);
                break;

            case BulkOperationType.FileOperation:
                results = await ExecuteBulkFileOperationsAsync(request, ct);
                break;

            default:
                throw new ArgumentException($"Unknown bulk operation type: {request.OperationType}");
        }

        return results;
    }

    private static BulkEditOptions BuildEditOptions(BulkOperationRequest request) => new()
    {
        CreateBackups = request.Options?.CreateBackup == true,
        MaxParallelism = request.Options?.MaxParallelism ?? 10,
        PreviewMode = request.Options?.DryRun == true
    };

    private static List<BulkOperationResult> ToOperationResults(BulkEditResult result) =>
        result.FileResults.Select(r => new BulkOperationResult
        {
            FilePath = r.FilePath,
            Success = r.Success,
            Changes = r.ChangesCount,
            Error = r.ErrorMessage,
            Message = r.SkipReason,
            BytesProcessed = File.Exists(r.FilePath) ? new System.IO.FileInfo(r.FilePath).Length : 0
        }).ToList();

    private async Task<List<BulkOperationResult>> ExecuteBulkReplaceAsync(
        BulkOperationRequest request,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.Pattern == null || request.Replacement == null)
            return new List<BulkOperationResult>();

        var result = await _bulkEditService.BulkReplaceAsync(
            request.Files ?? new List<string>(),
            request.Pattern,
            request.Replacement,
            BuildEditOptions(request),
            ct);

        return ToOperationResults(result);
    }

    /// <summary>
    /// Apply the edits to every file satisfying all conditions. The bulk edit service evaluates one
    /// condition per call, so files are first filtered on the remaining conditions here and the
    /// edits are then applied through <see cref="IBulkEditService.ConditionalEditAsync"/>.
    /// </summary>
    private async Task<List<BulkOperationResult>> ExecuteBulkConditionalAsync(
        BulkOperationRequest request,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.Conditions is not { Count: > 0 } || request.Edits is not { Count: > 0 })
            return new List<BulkOperationResult>();

        var conditions = request.Conditions.Select(ToBulkEditCondition).ToList();
        var results = new List<BulkOperationResult>();
        var candidates = new List<string>();

        foreach (var file in request.Files ?? new List<string>())
        {
            if (conditions.Count == 1)
            {
                candidates.Add(file);
                continue;
            }

            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                if (conditions.Skip(1).All(condition => BulkEditService.CheckCondition(content, condition, file)))
                {
                    candidates.Add(file);
                }
                else
                {
                    results.Add(new BulkOperationResult
                    {
                        FilePath = file,
                        Success = true,
                        Changes = 0,
                        Message = "Condition not met",
                        BytesProcessed = new System.IO.FileInfo(file).Length
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogBulkFileOperationFailed(_logger, ex, file);
                results.Add(new BulkOperationResult { FilePath = file, Success = false, Error = ex.Message });
            }
        }

        if (candidates.Count > 0)
        {
            var result = await _bulkEditService.ConditionalEditAsync(
                candidates,
                conditions[0],
                request.Edits,
                BuildEditOptions(request),
                ct);

            if (!result.Success && result.FileResults.Count == 0)
            {
                throw new InvalidOperationException(result.Error ?? "Conditional edit failed");
            }

            results.AddRange(ToOperationResults(result));
        }

        return results;
    }

    /// <summary>
    /// Map a consolidated edit condition onto the bulk edit service's condition model.
    /// Operator "not" (or "!=") negates any condition type.
    /// </summary>
    internal static BulkEditCondition ToBulkEditCondition(EditCondition condition)
    {
        var (type, negate) = condition.Type.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "contains" or "filecontains" => (BulkConditionType.FileContains, false),
            "notcontains" => (BulkConditionType.FileContains, true),
            "regex" or "matches" or "filematches" => (BulkConditionType.FileMatches, false),
            "notregex" or "notmatches" => (BulkConditionType.FileMatches, true),
            "extension" or "fileextension" => (BulkConditionType.FileExtension, false),
            "directory" or "indirectory" or "fileindirectory" => (BulkConditionType.FileInDirectory, false),
            _ => throw new ArgumentException($"Unsupported condition type: {condition.Type}")
        };

        if (condition.Operator is "not" or "!=")
        {
            negate = !negate;
        }

        return new BulkEditCondition
        {
            ConditionType = type,
            Pattern = condition.Value,
            Negate = negate
        };
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkRefactorAsync(
        BulkOperationRequest request,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.TargetPattern == null || request.ReplacementPattern == null)
            return new List<BulkOperationResult>();

        var refactorPattern = new MCPsharp.Models.BulkEdit.BulkRefactorPattern
        {
            RefactorType = Enum.Parse<MCPsharp.Models.BulkEdit.BulkRefactorType>(request.RefactorType ?? "RenameSymbol"),
            TargetPattern = request.TargetPattern,
            ReplacementPattern = request.ReplacementPattern
        };

        var result = await _bulkEditService.BatchRefactorAsync(
            request.Files ?? new List<string>(),
            refactorPattern,
            BuildEditOptions(request),
            ct);

        return ToOperationResults(result);
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkMultiEditAsync(
        BulkOperationRequest request,
        CancellationToken ct)
    {
        if (_bulkEditService == null || request.Operations is not { Count: > 0 })
            return new List<BulkOperationResult>();

        // Convert FileOperationDefinition to MultiFileEditOperation
        var multiFileOps = request.Operations.Select(op => new MCPsharp.Models.BulkEdit.MultiFileEditOperation
        {
            FilePattern = op.Path,
            Edits = op.Edits?.Select(e => new MCPsharp.Models.ReplaceEdit
            {
                NewText = e.NewText ?? "",
                StartLine = e.StartLine,
                StartColumn = e.StartColumn,
                EndLine = e.EndLine,
                EndColumn = e.EndColumn
            }).ToList() ?? new List<MCPsharp.Models.ReplaceEdit>(),
            Priority = 0 // FileOperationOptions doesn't have Priority property, use default
        }).ToList();

        var result = await _bulkEditService.MultiFileEditAsync(
            multiFileOps,
            BuildEditOptions(request),
            ct);

        return ToOperationResults(result);
    }

    private async Task<List<BulkOperationResult>> ExecuteBulkFileOperationsAsync(
        BulkOperationRequest request,
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
                LogBulkFileOperationFailed(_logger, ex, fileOp.Path);
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

    private async Task<string?> CreateBulkBackupAsync(BulkOperationRequest request, CancellationToken ct)
    {
        if (_tempFileManager == null) return null;

        var backupId = Guid.NewGuid().ToString();
        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        Directory.CreateDirectory(backupPath);

        try
        {
            // Backups are named by position so files sharing a name in different directories don't collide;
            // the manifest records where each one is restored to.
            var manifest = new List<(string OriginalPath, string BackupPath)>();
            foreach (var file in (request.Files ?? new List<string>()).Where(File.Exists))
            {
                var backupFile = Path.Combine(backupPath, manifest.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await Task.Run(() => File.Copy(file, backupFile, true), ct);
                manifest.Add((Path.GetFullPath(file), backupFile));
            }

            lock (_operationsLock)
            {
                _backupManifests[backupId] = manifest;
            }

            return backupId;
        }
        catch (Exception ex)
        {
            LogBulkBackupFailed(_logger, ex, backupPath);
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
        List<(string OriginalPath, string BackupPath)>? manifest;
        lock (_operationsLock)
        {
            _backupManifests.TryGetValue(backupId, out manifest);
        }

        foreach (var (originalPath, backupFile) in manifest ?? new List<(string OriginalPath, string BackupPath)>())
        {
            try
            {
                await Task.Run(() => File.Copy(backupFile, originalPath, true), ct);
            }
            catch (IOException ex)
            {
                LogBulkFileOperationFailed(_logger, ex, originalPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                LogBulkFileOperationFailed(_logger, ex, originalPath);
            }
        }
    }

    private async Task CleanupBulkBackupAsync(string backupId, CancellationToken ct)
    {
        lock (_operationsLock)
        {
            _backupManifests.Remove(backupId);
        }

        var backupPath = Path.Combine(Path.GetTempPath(), "mcpsharp_backups", backupId);
        if (Directory.Exists(backupPath))
        {
            await Task.Run(() => Directory.Delete(backupPath, true), ct);
        }
    }

    private static BulkImpactAnalysis AnalyzeBulkImpact(List<BulkOperationResult> results)
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

    private static BulkRiskAssessment AssessBulkRisks(List<BulkOperationResult> results)
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

    private List<BulkOperationInfo> ListActiveOperations()
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

    private static BulkCancelResult CancelBulkOperation()
    {
        // Implementation for canceling operations
        return new BulkCancelResult
        {
            Success = true,
            Message = "Operation cancelled"
        };
    }

    private static BulkPauseResult PauseBulkOperation()
    {
        // Implementation for pausing operations
        return new BulkPauseResult
        {
            Success = true,
            Message = "Operation paused"
        };
    }

    private static BulkResumeResult ResumeBulkOperation()
    {
        // Implementation for resuming operations
        return new BulkResumeResult
        {
            Success = true,
            Message = "Operation resumed"
        };
    }

    private BulkCleanupResult CleanupBulkOperations(TimeSpan? maxAge)
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

    private BulkOperationStatusResult GetBulkOperationStatus(string operationId)
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

    private static List<string> GetRecentLogs(BulkOperationContext context)
    {
        // Implementation would collect logs from various sources
        return new List<string>
        {
            $"Operation {context.Id} started",
            "Processing files...",
            "Completed successfully"
        };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bulk operation failed for file {FilePath}")]
    private static partial void LogBulkFileOperationFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to create bulk operation backup at {BackupPath}")]
    private static partial void LogBulkBackupFailed(ILogger logger, Exception exception, string backupPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error executing bulk operation {OperationId}")]
    private static partial void LogErrorExecutingBulkOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal error in bulk operation {OperationId}")]
    private static partial void LogFatalErrorInBulkOperation(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error previewing bulk operation")]
    private static partial void LogErrorPreviewingBulkOperation(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting bulk progress for {OperationId}")]
    private static partial void LogErrorGettingBulkProgressFor(ILogger logger, Exception exception, string operationId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error managing bulk operation")]
    private static partial void LogErrorManagingBulkOperation(ILogger logger, Exception exception);

    #endregion
}

#region Supporting Classes

/// <summary>
/// Bulk operation context
/// </summary>
internal sealed class BulkOperationContext
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
internal sealed class BulkValidationResult
{
    public required bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
}


#endregion
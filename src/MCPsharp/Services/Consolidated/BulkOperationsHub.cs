using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
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
}

using MCPsharp.Models.Consolidated;
using MCPsharp.Models;
using MCPsharp.Models.BulkEdit;
using MCPsharp.Services;

namespace MCPsharp.Services.Consolidated;

public partial class BulkOperationsHub
{
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
}

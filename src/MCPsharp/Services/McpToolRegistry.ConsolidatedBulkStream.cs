using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Consolidated;

namespace MCPsharp.Services;

/// <summary>
/// Handlers for the bulk operations hub tools and the stream processing controller tools.
/// </summary>
public partial class McpToolRegistry
{
    private const string BulkOperationsHubName = "Bulk operations hub";
    private const string StreamControllerName = "Stream processing controller";

    /// <summary>
    /// Fields shared by execute_bulk_operation and preview_bulk_operation, with all paths resolved in the project.
    /// </summary>
    private sealed record BulkArguments(
        BulkOperationType OperationType,
        List<string>? Files,
        List<FileOperationDefinition>? Operations);

    private ToolCallResult? ParseBulkArguments(JsonDocument arguments, out BulkArguments parsed)
    {
        parsed = new BulkArguments(default, null, null);
        var root = _projectContext.GetProjectContext()?.RootPath;
        if (root == null)
        {
            return ToolError(NoProjectOpenError);
        }

        var typeName = GetStringArgument(arguments, "operation_type");
        if (!TryParseEnumValue<BulkOperationType>(typeName, out var operationType))
        {
            return ToolError($"Invalid value for 'operation_type': {typeName ?? "(missing)"}");
        }

        List<string>? files = null;
        var rawFiles = GetStringListArgument(arguments, "files");
        if (rawFiles != null)
        {
            var pathError = TryResolvePathsInRoot(root, rawFiles, out files);
            if (pathError != null)
            {
                return ToolError(pathError);
            }
        }

        parsed = new BulkArguments(operationType, files, ParseFileOperationDefinitions(arguments, root));
        return null;
    }

    private static List<EditCondition>? ParseEditConditions(JsonDocument arguments)
    {
        if (!arguments.RootElement.TryGetProperty("conditions", out var conditions) ||
            conditions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return conditions.EnumerateArray().Select(c => new EditCondition
        {
            Type = c.GetProperty("type").GetString() ?? string.Empty,
            Value = c.GetProperty("value").GetString() ?? string.Empty,
            Operator = c.TryGetProperty("operator", out var op) ? op.GetString() : null
        }).ToList();
    }

    private async Task<ToolCallResult> ExecuteBulkOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return ServiceUnavailable(BulkOperationsHubName);
        }

        var argumentError = ParseBulkArguments(arguments, out var bulk);
        if (argumentError != null)
        {
            return argumentError;
        }

        var response = await _bulkOperationsHub.ExecuteBulkOperationAsync(new BulkOperationRequest
        {
            OperationType = bulk.OperationType,
            Files = bulk.Files,
            Operations = bulk.Operations,
            Pattern = GetStringArgument(arguments, "pattern"),
            Replacement = GetStringArgument(arguments, "replacement"),
            Conditions = ParseEditConditions(arguments),
            RefactorType = GetStringArgument(arguments, "refactor_type"),
            TargetPattern = GetStringArgument(arguments, "target_pattern"),
            ReplacementPattern = GetStringArgument(arguments, "replacement_pattern"),
            Options = new BulkOperationOptions
            {
                DryRun = GetBoolArgument(arguments, "dry_run") ?? false,
                CreateBackup = GetBoolArgument(arguments, "create_backup") ?? false,
                MaxParallelism = GetIntArgument(arguments, "max_parallelism") ?? 10,
                FailFast = GetBoolArgument(arguments, "fail_fast") ?? true
            }
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecutePreviewBulkOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return ServiceUnavailable(BulkOperationsHubName);
        }

        var argumentError = ParseBulkArguments(arguments, out var bulk);
        if (argumentError != null)
        {
            return argumentError;
        }

        var response = await _bulkOperationsHub.PreviewBulkOperationAsync(new BulkPreviewRequest
        {
            OperationType = bulk.OperationType,
            Files = bulk.Files,
            Operations = bulk.Operations,
            Pattern = GetStringArgument(arguments, "pattern"),
            Replacement = GetStringArgument(arguments, "replacement"),
            RefactorType = GetStringArgument(arguments, "refactor_type"),
            TargetPattern = GetStringArgument(arguments, "target_pattern"),
            ReplacementPattern = GetStringArgument(arguments, "replacement_pattern"),
            MaxPreviewFiles = GetIntArgument(arguments, "max_preview_files"),
            Options = new BulkOperationOptions()
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteGetBulkProgress(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return ServiceUnavailable(BulkOperationsHubName);
        }

        var operationId = GetStringArgument(arguments, "operation_id");
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return ToolError("Missing required parameter 'operation_id'");
        }

        var response = await _bulkOperationsHub.GetBulkProgressAsync(operationId, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteManageBulkOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_bulkOperationsHub == null)
        {
            return ServiceUnavailable(BulkOperationsHubName);
        }

        var actionName = GetStringArgument(arguments, "action");
        if (!TryParseEnumValue<BulkManagementAction>(actionName, out var action))
        {
            return ToolError($"Invalid value for 'action': {actionName ?? "(missing)"}");
        }

        var operationId = GetStringArgument(arguments, "operation_id");
        var response = await _bulkOperationsHub.ManageBulkOperationAsync(new BulkManagementRequest
        {
            Action = action,
            Target = operationId,
            OperationId = operationId,
            MaxAge = GetHoursArgument(arguments, "max_age_hours")
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    // ===== Stream Processing Controller =====

    private async Task<ToolCallResult> ExecuteProcessStream(JsonDocument arguments, CancellationToken ct)
    {
        if (_streamController == null)
        {
            return ServiceUnavailable(StreamControllerName);
        }

        var root = _projectContext.GetProjectContext()?.RootPath;
        if (root == null)
        {
            return ToolError(NoProjectOpenError);
        }

        var processorType = GetStringArgument(arguments, "processor_type");
        if (string.IsNullOrWhiteSpace(processorType))
        {
            return ToolError("Missing required parameter 'processor_type'");
        }

        if (!TryGetEnumArgument(arguments, "input_type", StreamInputType.File, out var inputType, out var enumError))
        {
            return enumError!;
        }

        if (inputType == StreamInputType.Stream)
        {
            return ToolError("Input type 'stream' is not supported over MCP; use file, directory or multiple_files");
        }

        // multiple_files has no single input path; the project root anchors it
        var inputPath = root;
        if (GetStringArgument(arguments, "input_path") != null || inputType != StreamInputType.MultipleFiles)
        {
            var inputError = ResolveRequiredProjectPath(arguments, "input_path", out inputPath);
            if (inputError != null)
            {
                return inputError;
            }
        }

        string? outputPath = null;
        if (GetStringArgument(arguments, "output_path") != null)
        {
            var outputError = ResolveRequiredProjectPath(arguments, "output_path", out var resolvedOutput);
            if (outputError != null)
            {
                return outputError;
            }

            outputPath = resolvedOutput;
        }

        List<string>? files = null;
        var rawFiles = GetStringListArgument(arguments, "files");
        if (rawFiles != null)
        {
            var filesError = TryResolvePathsInRoot(root, rawFiles, out files);
            if (filesError != null)
            {
                return ToolError(filesError);
            }
        }

        var chunkSize = GetIntArgument(arguments, "chunk_size");
        var response = await _streamController.ProcessStreamAsync(new StreamProcessRequest
        {
            InputType = inputType,
            ProcessorType = processorType,
            InputPath = inputPath,
            OutputPath = outputPath,
            Files = files,
            SearchPattern = GetStringArgument(arguments, "search_pattern"),
            Recursive = GetBoolArgument(arguments, "recursive") ?? false,
            ChunkSize = chunkSize,
            EnableCompression = GetBoolArgument(arguments, "enable_compression") ?? false,
            EnableCheckpoints = GetBoolArgument(arguments, "enable_checkpoints") ?? false,
            ProcessorOptions = chunkSize.HasValue ? new ProcessorOptions { ChunkSize = chunkSize.Value } : null
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteMonitorStream(JsonDocument arguments, CancellationToken ct)
    {
        if (_streamController == null)
        {
            return ServiceUnavailable(StreamControllerName);
        }

        var operationId = GetStringArgument(arguments, "operation_id");
        if (string.IsNullOrWhiteSpace(operationId))
        {
            return ToolError("Missing required parameter 'operation_id'");
        }

        var response = await _streamController.MonitorStreamAsync(operationId, new StreamMonitorRequest
        {
            IncludeDetails = GetBoolArgument(arguments, "include_details") ?? true,
            IncludeLogs = GetBoolArgument(arguments, "include_logs") ?? true,
            IncludePerformance = GetBoolArgument(arguments, "include_performance") ?? true,
            MaxLogEntries = GetIntArgument(arguments, "max_log_entries")
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteManageStream(JsonDocument arguments, CancellationToken ct)
    {
        if (_streamController == null)
        {
            return ServiceUnavailable(StreamControllerName);
        }

        var actionName = GetStringArgument(arguments, "action");
        if (!TryParseEnumValue<StreamManagementAction>(actionName, out var action))
        {
            return ToolError($"Invalid value for 'action': {actionName ?? "(missing)"}");
        }

        if (action == StreamManagementAction.RegisterProcessor)
        {
            return ToolError("Action 'register_processor' is not supported over MCP");
        }

        var target = GetStringArgument(arguments, "operation_id");
        var response = await _streamController.ManageStreamAsync(new StreamManagementRequest
        {
            Action = action,
            Target = target,
            OperationId = target,
            MaxAge = GetHoursArgument(arguments, "max_age_hours")
        }, ct);
        return FromServiceResponse(response, response.Error);
    }
}

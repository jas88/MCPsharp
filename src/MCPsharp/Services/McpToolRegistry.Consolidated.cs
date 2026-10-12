using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Consolidated;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Shared plumbing for the consolidated tools plus the universal file operation handlers.
/// </summary>
public partial class McpToolRegistry
{
    private const string NoProjectOpenError = "No project is open";

    /// <summary>
    /// Re-root the consolidated services on the newly opened project. Only services that were
    /// supplied at construction are rebuilt, so tool availability never changes after startup.
    /// The bulk operations hub is rebuilt because it performs file operations through the
    /// (re-rooted) universal file service; operations tracked for the previous project are dropped.
    /// </summary>
    private void RebindConsolidatedServices()
    {
        var root = _projectContext.GetProjectContext()?.RootPath;
        if (root == null || _fileOperations == null)
        {
            return;
        }

        if (_universalFileOps != null)
        {
            _universalFileOps = new UniversalFileOperations(
                root, _fileOperations, _bulkEditService, _loggerFactory?.CreateLogger<UniversalFileOperations>());

            if (_bulkOperationsHub != null)
            {
                _bulkOperationsHub = new BulkOperationsHub(
                    _bulkEditService, _universalFileOps, _progressTracker, _tempFileManager,
                    _loggerFactory?.CreateLogger<BulkOperationsHub>());
            }
        }

        if (_unifiedAnalysis != null)
        {
            _unifiedAnalysis = new UnifiedAnalysisService(
                _workspace, _symbolQuery, _classStructure, _referenceFinder,
                _architectureValidator, _duplicateCodeDetector,
                _loggerFactory?.CreateLogger<UnifiedAnalysisService>());
        }
    }

    private static ToolCallResult ToolError(string error) => new() { Success = false, Error = error };

    private static ToolCallResult ServiceUnavailable(string serviceName) =>
        ToolError($"{serviceName} is not available");

    /// <summary>
    /// Map a consolidated service response to a tool result: the service reports failures
    /// through its Error property rather than by throwing.
    /// </summary>
    private static ToolCallResult FromServiceResponse(object response, string? error) =>
        error == null ? new ToolCallResult { Success = true, Result = response } : ToolError(error);

    /// <summary>
    /// Resolve a tool path argument against the open project root, rejecting paths that escape it.
    /// </summary>
    private static bool TryResolvePathInRoot(string root, string path, out string fullPath)
    {
        fullPath = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, fullPath);
        return !Path.IsPathRooted(relative)
               && relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolve a required path argument within the open project.
    /// Returns an error result when no project is open, the argument is missing, or the path escapes the project.
    /// </summary>
    private ToolCallResult? ResolveRequiredProjectPath(JsonDocument arguments, string argumentName, out string fullPath)
    {
        fullPath = string.Empty;
        var root = _projectContext.GetProjectContext()?.RootPath;
        if (root == null)
        {
            return ToolError(NoProjectOpenError);
        }

        var path = GetStringArgument(arguments, argumentName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return ToolError($"Missing required parameter '{argumentName}'");
        }

        return TryResolvePathInRoot(root, path, out fullPath)
            ? null
            : ToolError($"Path is outside the project root: {path}");
    }

    /// <summary>
    /// Resolve a list of paths within the open project root. Returns an error message for the first invalid path.
    /// </summary>
    private static string? TryResolvePathsInRoot(string root, IEnumerable<string> paths, out List<string> resolved)
    {
        resolved = new List<string>();
        foreach (var path in paths)
        {
            if (!TryResolvePathInRoot(root, path, out var fullPath))
            {
                return $"Path is outside the project root: {path}";
            }

            resolved.Add(fullPath);
        }

        return null;
    }

    private static List<string>? GetStringListArgument(JsonDocument arguments, string name)
    {
        if (!arguments.RootElement.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return element.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
            .Select(e => e.GetString()!)
            .ToList();
    }

    /// <summary>
    /// Parse a snake_case or PascalCase enum argument value (e.g. "partial_read" or "PartialRead").
    /// Numeric values are rejected so only named members are accepted.
    /// </summary>
    private static bool TryParseEnumValue<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal);
        return !char.IsDigit(normalized[0])
               && Enum.TryParse(normalized, ignoreCase: true, out result)
               && Enum.IsDefined(result);
    }

    /// <summary>
    /// Parse an optional enum argument. Returns false (with an error) only when a value is present but invalid.
    /// </summary>
    private static bool TryGetEnumArgument<TEnum>(JsonDocument arguments, string name, TEnum defaultValue,
        out TEnum value, out ToolCallResult? error) where TEnum : struct, Enum
    {
        value = defaultValue;
        error = null;
        var raw = GetStringArgument(arguments, name);
        if (raw == null)
        {
            return true;
        }

        if (TryParseEnumValue(raw, out value))
        {
            return true;
        }

        error = ToolError($"Invalid value for '{name}': {raw}");
        return false;
    }

    /// <summary>
    /// Parse an optional array of enum values, ignoring unknown entries.
    /// </summary>
    private static List<TEnum> GetEnumListArgument<TEnum>(JsonDocument arguments, string name) where TEnum : struct, Enum =>
        (GetStringListArgument(arguments, name) ?? new List<string>())
            .Select(v => TryParseEnumValue<TEnum>(v, out var parsed) ? (TEnum?)parsed : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

    private static ToolOptions BuildToolOptions(JsonDocument arguments)
    {
        var options = new ToolOptions();
        if (TryParseEnumValue<DetailLevel>(GetStringArgument(arguments, "detail"), out var detail))
        {
            options.Detail = detail;
        }

        foreach (var include in GetEnumListArgument<IncludeOptions>(arguments, "include"))
        {
            options.Include |= include;
        }

        return options;
    }

    private static TimeSpan? GetHoursArgument(JsonDocument arguments, string name)
    {
        var hours = GetDoubleFromArguments(arguments, name, double.NaN);
        return double.IsNaN(hours) || hours < 0 ? null : TimeSpan.FromHours(hours);
    }

    /// <summary>
    /// Parse file_edit-style edit objects (replace/insert/delete with 0-indexed positions).
    /// </summary>
    private static List<TextEdit> ParseTextEdits(JsonElement editsArray)
    {
        var edits = new List<TextEdit>();
        foreach (var editElement in editsArray.EnumerateArray())
        {
            var type = editElement.GetProperty("type").GetString();
            TextEdit edit = type switch
            {
                "replace" => new ReplaceEdit
                {
                    StartLine = editElement.GetProperty("start_line").GetInt32(),
                    StartColumn = editElement.GetProperty("start_column").GetInt32(),
                    EndLine = editElement.GetProperty("end_line").GetInt32(),
                    EndColumn = editElement.GetProperty("end_column").GetInt32(),
                    NewText = editElement.GetProperty("new_text").GetString() ?? ""
                },
                "insert" => new InsertEdit
                {
                    StartLine = editElement.GetProperty("line").GetInt32(),
                    StartColumn = editElement.GetProperty("column").GetInt32(),
                    EndLine = editElement.GetProperty("line").GetInt32(),
                    EndColumn = editElement.GetProperty("column").GetInt32(),
                    NewText = editElement.GetProperty("text").GetString() ?? ""
                },
                "delete" => new DeleteEdit
                {
                    StartLine = editElement.GetProperty("start_line").GetInt32(),
                    StartColumn = editElement.GetProperty("start_column").GetInt32(),
                    EndLine = editElement.GetProperty("end_line").GetInt32(),
                    EndColumn = editElement.GetProperty("end_column").GetInt32(),
                    NewText = ""
                },
                _ => throw new ArgumentException($"Unknown edit type: {type}")
            };
            edits.Add(edit);
        }

        return edits;
    }

    /// <summary>
    /// Parse one file operation object (as used by execute_batch and bulk operations), resolving its paths in the project.
    /// </summary>
    private static FileOperationDefinition ParseFileOperationDefinition(JsonElement element, string root)
    {
        var path = element.GetProperty("path").GetString() ?? string.Empty;
        var operationName = element.TryGetProperty("operation", out var op) ? op.GetString() : null;
        if (!TryParseEnumValue<FileOperationType>(operationName, out var operation))
        {
            throw new ArgumentException($"Invalid file operation '{operationName}' for path '{path}'");
        }

        if (!TryResolvePathInRoot(root, path, out var fullPath))
        {
            throw new ArgumentException($"Path is outside the project root: {path}");
        }

        string? destination = null;
        if (element.TryGetProperty("destination_path", out var dest) && dest.ValueKind == JsonValueKind.String)
        {
            var destPath = dest.GetString()!;
            if (!TryResolvePathInRoot(root, destPath, out destination))
            {
                throw new ArgumentException($"Path is outside the project root: {destPath}");
            }
        }

        return new FileOperationDefinition
        {
            Path = fullPath,
            Type = operation,
            DestinationPath = destination,
            Edits = element.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array
                ? ParseTextEdits(edits)
                : null,
            Options = new FileOperationOptions
            {
                Overwrite = element.TryGetProperty("overwrite", out var ow) && ow.ValueKind == JsonValueKind.True,
                DryRun = element.TryGetProperty("dry_run", out var dr) && dr.ValueKind == JsonValueKind.True
            }
        };
    }

    private static List<FileOperationDefinition>? ParseFileOperationDefinitions(JsonDocument arguments, string root)
    {
        if (!arguments.RootElement.TryGetProperty("operations", out var operations) ||
            operations.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return operations.EnumerateArray().Select(e => ParseFileOperationDefinition(e, root)).ToList();
    }

    // ===== Universal File Operations =====

    private async Task<ToolCallResult> ExecuteGetFileInfo(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return ServiceUnavailable("Universal file operations service");
        }

        var pathError = ResolveRequiredProjectPath(arguments, "path", out var fullPath);
        if (pathError != null)
        {
            return pathError;
        }

        var response = await _universalFileOps.GetFileInfoAsync(new FileInfoRequest
        {
            Path = fullPath,
            Options = BuildToolOptions(arguments)
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteGetFileContent(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return ServiceUnavailable("Universal file operations service");
        }

        var pathError = ResolveRequiredProjectPath(arguments, "path", out var fullPath);
        if (pathError != null)
        {
            return pathError;
        }

        if (!TryGetEnumArgument(arguments, "operation", FileContentOperation.Read, out var operation, out var enumError))
        {
            return enumError!;
        }

        var response = await _universalFileOps.GetFileContentAsync(new FileContentRequest
        {
            Path = fullPath,
            Operation = operation,
            Content = GetStringArgument(arguments, "content"),
            Options = new FileContentOptions
            {
                StartLine = GetIntArgument(arguments, "start_line"),
                EndLine = GetIntArgument(arguments, "end_line"),
                CreateDirectories = GetBoolArgument(arguments, "create_directories") ?? true,
                Overwrite = GetBoolArgument(arguments, "overwrite") ?? false
            }
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteFileOperation(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return ServiceUnavailable("Universal file operations service");
        }

        var pathError = ResolveRequiredProjectPath(arguments, "path", out var fullPath);
        if (pathError != null)
        {
            return pathError;
        }

        var operationName = GetStringArgument(arguments, "operation");
        if (!TryParseEnumValue<FileOperationType>(operationName, out var operation))
        {
            return ToolError($"Invalid value for 'operation': {operationName ?? "(missing)"}");
        }

        string? destination = null;
        if (GetStringArgument(arguments, "destination_path") != null)
        {
            var destError = ResolveRequiredProjectPath(arguments, "destination_path", out var destPath);
            if (destError != null)
            {
                return destError;
            }

            destination = destPath;
        }

        var maxFileSize = GetIntArgument(arguments, "max_file_size");
        var response = await _universalFileOps.ExecuteFileOperationAsync(new FileOperationRequest
        {
            Path = fullPath,
            Operation = operation,
            DestinationPath = destination,
            Edits = arguments.RootElement.TryGetProperty("edits", out var edits) && edits.ValueKind == JsonValueKind.Array
                ? ParseTextEdits(edits)
                : null,
            Options = new FileOperationOptions
            {
                Overwrite = GetBoolArgument(arguments, "overwrite") ?? false,
                CreateBackup = GetBoolArgument(arguments, "create_backup") ?? false,
                DryRun = GetBoolArgument(arguments, "dry_run") ?? false,
                IncludeNewContent = GetBoolArgument(arguments, "include_new_content") ?? false,
                RequireUtf8 = GetBoolArgument(arguments, "require_utf8") ?? false,
                MaxFileSize = maxFileSize
            }
        }, ct);
        return FromServiceResponse(response, response.Error);
    }

    private async Task<ToolCallResult> ExecuteBatch(JsonDocument arguments, CancellationToken ct)
    {
        if (_universalFileOps == null)
        {
            return ServiceUnavailable("Universal file operations service");
        }

        var root = _projectContext.GetProjectContext()?.RootPath;
        if (root == null)
        {
            return ToolError(NoProjectOpenError);
        }

        var operations = ParseFileOperationDefinitions(arguments, root);
        if (operations == null || operations.Count == 0)
        {
            return ToolError("Missing required parameter 'operations'");
        }

        var response = await _universalFileOps.ExecuteBatchAsync(new FileBatchRequest
        {
            Operations = operations,
            EnableTransactions = GetBoolArgument(arguments, "enable_transactions") ?? false,
            FailFast = GetBoolArgument(arguments, "fail_fast") ?? true,
            ContinueOnError = GetBoolArgument(arguments, "continue_on_error") ?? false
        }, ct);
        return FromServiceResponse(response, response.Error);
    }
}

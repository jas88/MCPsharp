using System.Text.Json;
using System.Text.Json.Nodes;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Project containment for tools whose handlers take raw file paths or glob patterns.
/// </summary>
/// <remarks>
/// The bulk edit, streaming, code fix, migration, large file, architecture and duplicate detection
/// handlers pass path arguments straight to their services. Before dispatch, every declared path
/// argument is resolved against the open project root (relative paths) and rejected when it escapes
/// the project; the handler then receives the arguments with absolute paths.
/// </remarks>
public partial class McpToolRegistry
{
    private static readonly char[] GlobCharacters = { '*', '?', '[', '{' };
    private static readonly char[] PathSeparators = { '/', '\\' };

    /// <summary>
    /// A path argument: a property name, optionally followed by "[]" for an array (or single string)
    /// and ".name" segments for properties of array elements, e.g. "files[]" or "operations[].filePattern".
    /// </summary>
    private sealed record PathArgument(string Name, bool AllowGlob = false);

    private static readonly PathArgument[] ProjectPathArgument = { new("projectPath") };
    private static readonly PathArgument[] FilePathArguments = { new("projectPath"), new("filePath") };
    private static readonly PathArgument[] BulkFilesArgument = { new("files[]", AllowGlob: true) };
    private static readonly PathArgument[] BulkReplaceFilesArgument =
        { new("files[]", AllowGlob: true), new("excludedFiles[]", AllowGlob: true) };
    private static readonly PathArgument[] BulkFilesAndOperationsArguments =
        { new("files[]", AllowGlob: true), new("operations[].filePattern", AllowGlob: true) };
    private static readonly PathArgument[] MultiFileOperationsArgument = { new("operations[].filePattern", AllowGlob: true) };
    private static readonly PathArgument[] StreamFileArguments = { new("filePath"), new("outputPath") };
    private static readonly PathArgument[] BulkTransformArguments = { new("inputFiles[]"), new("outputDirectory") };
    private static readonly PathArgument[] StreamEstimateArguments = { new("filePath") };
    private static readonly PathArgument[] MigrationHistoryArguments = { new("dbContextPath") };
    private static readonly PathArgument[] RoslynFixArguments = { new("target_path") };
    private static readonly PathArgument[] AnalysisReportArguments = { new("output_path") };

    /// <summary>
    /// Resolve the declared path arguments within the open project and invoke the handler with the
    /// rewritten arguments. Arguments that are absent are left for the handler's own defaults.
    /// </summary>
    private async Task<ToolCallResult> WithProjectPaths(
        JsonDocument arguments,
        PathArgument[] pathArguments,
        Func<JsonDocument, Task<ToolCallResult>> handler)
    {
        if (JsonNode.Parse(arguments.RootElement.GetRawText(), new JsonNodeOptions { PropertyNameCaseInsensitive = true })
            is not JsonObject argumentObject)
        {
            return await handler(arguments);
        }

        var root = _projectContext.GetProjectContext()?.RootPath;
        var changed = false;
        foreach (var pathArgument in pathArguments)
        {
            var error = ResolvePathNode(argumentObject, pathArgument.Name.Split('.'), 0, pathArgument.AllowGlob, root, ref changed);
            if (error != null)
            {
                return ToolError(error);
            }
        }

        if (!changed)
        {
            return await handler(arguments);
        }

        // Not disposed here: results may hold JsonElements that reference the rewritten document
        var rewritten = JsonDocument.Parse(argumentObject.ToJsonString());
        return await handler(rewritten);
    }

    private static string? ResolvePathNode(JsonObject parent, string[] segments, int index, bool allowGlob, string? root, ref bool changed)
    {
        var segment = segments[index];
        var isArray = segment.EndsWith("[]", StringComparison.Ordinal);
        var name = isArray ? segment[..^2] : segment;
        var isLast = index == segments.Length - 1;

        if (!parent.TryGetPropertyValue(name, out var value) || value == null)
        {
            return null;
        }

        if (isLast && value is JsonValue)
        {
            return ResolvePathValue(parent, name, allowGlob, root, ref changed);
        }

        if (isArray && value is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                string? error = null;
                if (isLast && array[i] is JsonValue)
                {
                    error = ResolveArrayPathValue(array, i, allowGlob, root, ref changed);
                }
                else if (!isLast && array[i] is JsonObject element)
                {
                    error = ResolvePathNode(element, segments, index + 1, allowGlob, root, ref changed);
                }

                if (error != null)
                {
                    return error;
                }
            }

            return null;
        }

        return !isLast && value is JsonObject child
            ? ResolvePathNode(child, segments, index + 1, allowGlob, root, ref changed)
            : null;
    }

    private static string? ResolvePathValue(JsonObject parent, string name, bool allowGlob, string? root, ref bool changed)
    {
        if (parent[name] is not JsonValue value || !value.TryGetValue<string>(out var path) || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var error = TryResolveToolPath(root, path, allowGlob, name, out var resolved);
        if (error == null)
        {
            parent[name] = resolved;
            changed = true;
        }

        return error;
    }

    private static string? ResolveArrayPathValue(JsonArray array, int index, bool allowGlob, string? root, ref bool changed)
    {
        if (array[index] is not JsonValue value || !value.TryGetValue<string>(out var path) || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var error = TryResolveToolPath(root, path, allowGlob, "path", out var resolved);
        if (error == null)
        {
            array[index] = resolved;
            changed = true;
        }

        return error;
    }

    /// <summary>
    /// Resolve a path or glob pattern argument to an absolute path within the project root.
    /// A glob is accepted when its fixed leading directory lies within the project and it has no ".." segments.
    /// </summary>
    internal static string? TryResolveToolPath(string? root, string path, bool allowGlob, string argumentName, out string resolved)
    {
        resolved = path;
        if (root == null)
        {
            return NoProjectOpenError;
        }

        var globIndex = allowGlob ? path.IndexOfAny(GlobCharacters) : -1;
        if (globIndex < 0)
        {
            return PathContainment.TryResolveWithin(root, path, out resolved)
                ? null
                : $"Path '{path}' for '{argumentName}' is outside the project root";
        }

        if (path.Split(PathSeparators).Any(part => part == ".."))
        {
            return $"Pattern '{path}' for '{argumentName}' must not contain '..' segments";
        }

        var absolute = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
        var fixedPrefixEnd = absolute.LastIndexOfAny(PathSeparators, absolute.IndexOfAny(GlobCharacters));
        var fixedDirectory = fixedPrefixEnd <= 0 ? absolute[..(fixedPrefixEnd + 1)] : absolute[..fixedPrefixEnd];
        if (fixedDirectory.Length == 0 || !PathContainment.IsWithin(root, fixedDirectory))
        {
            return $"Pattern '{path}' for '{argumentName}' is outside the project root";
        }

        resolved = absolute;
        return null;
    }
}

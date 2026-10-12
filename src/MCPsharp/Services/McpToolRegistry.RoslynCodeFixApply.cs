using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;
using MCPsharp.Services.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services;

/// <summary>
/// McpToolRegistry partial class for the apply_roslyn_fixes tool
/// </summary>
/// <remarks>
/// Fixes are computed in memory by <see cref="RoslynCodeFixApplier"/> against the current file content,
/// one diagnostic at a time. Preview returns the outcomes and diffs without writing. Otherwise every
/// changed file is backed up into a bulk edit rollback session (undone with rollback_bulk_edit) before
/// it is written, and the Roslyn workspace is updated with the new content.
/// </remarks>
public partial class McpToolRegistry
{
    private const string RoslynFixOperationType = "RoslynCodeFix";

    private RoslynCodeFixLoader? _codeFixLoader;
    private readonly List<RoslynCodeFixAdapter> _loadedCodeFixProviders = new();
    private readonly HashSet<string> _loadedCodeFixAssemblies = new(StringComparer.Ordinal);

    /// <summary>
    /// Execute apply_roslyn_fixes tool
    /// </summary>
    private async Task<ToolCallResult> ExecuteApplyRoslynFixes(JsonDocument arguments, CancellationToken ct)
    {
        if (_roslynAnalyzerService == null)
        {
            return ToolError("Roslyn Analyzer service is not available");
        }

        try
        {
            var parseError = ParseApplyRoslynFixesArguments(arguments.RootElement, out var diagnosticIds, out var targetPath, out var preview);
            if (parseError != null)
            {
                return ToolError(parseError);
            }

            await _roslynAnalyzerService.EnsureAnalyzersLoadedAsync(ct);
            var analyzerAdapters = _roslynAnalyzerService.GetLoadedAnalyzers()
                .OfType<RoslynAnalyzerAdapter>()
                .Where(a => a.IsEnabled && a.DiagnosticAnalyzer.SupportedDiagnostics.Any(d => diagnosticIds.Contains(d.Id)))
                .ToList();
            var analyzers = analyzerAdapters
                .Select(a => a.DiagnosticAnalyzer)
                .GroupBy(a => a.GetType())
                .Select(g => g.First())
                .ToImmutableArray();

            if (analyzers.IsEmpty)
            {
                return ToolError($"No loaded analyzer reports diagnostic IDs: {string.Join(", ", diagnosticIds)}");
            }

            await LoadCodeFixProvidersAsync(analyzerAdapters, ct);
            var providers = _loadedCodeFixProviders.Where(p => diagnosticIds.Any(p.CanFix)).ToList();
            if (providers.Count == 0)
            {
                return ToolError($"No code fix providers found for diagnostic IDs: {string.Join(", ", diagnosticIds)}");
            }

            var run = await ComputeRoslynFixesAsync(targetPath, analyzers, providers, diagnosticIds, ct);

            if (run.Outcomes.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        message = "No issues found matching the specified diagnostic IDs",
                        diagnosticIds,
                        filePath = targetPath
                    }
                };
            }

            if (preview)
            {
                return new ToolCallResult { Success = true, Result = BuildRoslynFixResult(run, targetPath, preview: true, rollbackId: null) };
            }

            string? rollbackId = null;
            if (run.ChangedDocuments.Count > 0)
            {
                var (id, writeError) = await WriteFixedDocumentsAsync(run.ChangedDocuments, ct);
                if (writeError != null)
                {
                    return new ToolCallResult
                    {
                        Success = false,
                        Error = writeError,
                        Result = BuildRoslynFixResult(run, targetPath, preview: false, rollbackId: null)
                    };
                }

                rollbackId = id;
            }

            var allFailed = run.AppliedCount == 0 && run.FailedCount > 0;
            return new ToolCallResult
            {
                Success = !allFailed,
                Error = allFailed ? "None of the fixes could be applied; see 'fixes' for the reasons" : null,
                Result = BuildRoslynFixResult(run, targetPath, preview: false, rollbackId)
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteApplyRoslynFixes));
            return ToolError($"Failed to apply Roslyn fixes: {ex.Message}");
        }
    }

    private static string? ParseApplyRoslynFixesArguments(
        JsonElement root, out HashSet<string> diagnosticIds, out string targetPath, out bool preview)
    {
        diagnosticIds = new HashSet<string>(StringComparer.Ordinal);
        targetPath = string.Empty;
        preview = true;

        if (!root.TryGetProperty("diagnostic_ids", out var idsElement) || idsElement.ValueKind != JsonValueKind.Array)
        {
            return "Missing required parameter 'diagnostic_ids' (array of strings)";
        }

        foreach (var element in idsElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
            {
                diagnosticIds.Add(element.GetString()!);
            }
        }

        if (diagnosticIds.Count == 0)
        {
            return "No valid diagnostic IDs provided";
        }

        if (!root.TryGetProperty("target_path", out var pathElement))
        {
            return "Missing required parameter 'target_path'";
        }

        targetPath = pathElement.ValueKind == JsonValueKind.String ? pathElement.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return "Parameter 'target_path' cannot be empty";
        }

        if (!File.Exists(targetPath))
        {
            return $"File not found: {targetPath}";
        }

        if (root.TryGetProperty("preview", out var previewElement))
        {
            if (previewElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return "Parameter 'preview' must be a boolean";
            }

            preview = previewElement.GetBoolean();
        }

        return null;
    }

    /// <summary>
    /// Load code fix providers from the analyzer assemblies and their companion code fix assemblies
    /// (e.g. "X.Analyzers.CodeFixes.dll" next to "X.Analyzers.dll"). Each assembly is loaded once.
    /// </summary>
    private async Task LoadCodeFixProvidersAsync(IEnumerable<RoslynAnalyzerAdapter> analyzers, CancellationToken ct)
    {
        var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
        _codeFixLoader ??= new RoslynCodeFixLoader(loggerFactory.CreateLogger<RoslynCodeFixLoader>(), loggerFactory);

        var candidates = analyzers
            .Select(a => a.DiagnosticAnalyzer.GetType().Assembly.Location)
            .Where(path => !string.IsNullOrEmpty(path))
            .Distinct(StringComparer.Ordinal)
            .SelectMany(GetCodeFixAssemblyCandidates)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var assemblyPath in candidates)
        {
            if (!_loadedCodeFixAssemblies.Add(assemblyPath))
            {
                continue;
            }

            var providers = await _codeFixLoader.LoadCodeFixProvidersFromAssemblyAsync(assemblyPath, ct);
            _loadedCodeFixProviders.AddRange(providers);
        }
    }

    private static IEnumerable<string> GetCodeFixAssemblyCandidates(string analyzerAssemblyPath)
    {
        yield return analyzerAssemblyPath;

        var directory = Path.GetDirectoryName(analyzerAssemblyPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            yield break;
        }

        var baseName = Path.GetFileNameWithoutExtension(analyzerAssemblyPath);
        var companions = Directory.EnumerateFiles(directory, baseName + ".*.dll")
            .Where(file => Path.GetFileName(file).Contains("Fix", StringComparison.OrdinalIgnoreCase));
        foreach (var companion in companions)
        {
            yield return companion;
        }
    }

    /// <summary>
    /// Compute the fixes in memory. The workspace document is used when the file belongs to the loaded
    /// project (so fixes see the real references), synchronised with the content on disk; otherwise the
    /// file is fixed in a standalone single-document project.
    /// </summary>
    private async Task<CodeFixRunResult> ComputeRoslynFixesAsync(
        string targetPath,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        IReadOnlyList<RoslynCodeFixAdapter> providers,
        IReadOnlyCollection<string> diagnosticIds,
        CancellationToken ct)
    {
        var diskContent = await File.ReadAllTextAsync(targetPath, ct);
        var applier = new RoslynCodeFixApplier(_loggerFactory?.CreateLogger<RoslynCodeFixApplier>());

        var document = _workspace?.GetDocument(targetPath);
        if (document != null)
        {
            var workspaceText = await document.GetTextAsync(ct);
            if (!string.Equals(workspaceText.ToString(), diskContent, StringComparison.Ordinal))
            {
                document = document.WithText(SourceText.From(diskContent));
            }

            return await applier.ApplyAsync(document, analyzers, providers, diagnosticIds, null, ct);
        }

        using var adhocWorkspace = new AdhocWorkspace();
        var adhocDocument = RoslynCodeFixApplier.CreateAdhocDocument(adhocWorkspace, targetPath, diskContent);
        return await applier.ApplyAsync(adhocDocument, analyzers, providers, diagnosticIds, null, ct);
    }

    /// <summary>
    /// Back up the files into a rollback session, then write them and update the workspace. Nothing is
    /// written when a file lies outside the project, changed on disk since the fixes were computed, or
    /// could not be backed up. A failed write restores every file from the backup.
    /// </summary>
    private async Task<(string? RollbackId, string? Error)> WriteFixedDocumentsAsync(
        IReadOnlyList<ChangedDocumentText> documents, CancellationToken ct)
    {
        var root = _projectContext.GetProjectContext()?.RootPath;
        if (root == null)
        {
            return (null, NoProjectOpenError);
        }

        foreach (var document in documents)
        {
            if (!PathContainment.IsWithin(root, document.FilePath))
            {
                return (null, $"Fix changes '{document.FilePath}', which is outside the project root; nothing was written");
            }

            if (!File.Exists(document.FilePath) ||
                !string.Equals(await File.ReadAllTextAsync(document.FilePath, ct), document.OriginalText, StringComparison.Ordinal))
            {
                return (null, $"'{document.FilePath}' changed on disk while the fixes were computed; nothing was written");
            }
        }

        if (_bulkEditService == null)
        {
            return (null, "Rollback service is not available, so fixes cannot be written safely; nothing was written (use preview)");
        }

        var paths = documents.Select(d => d.FilePath).ToList();
        var rollback = await _bulkEditService.CreateRollbackSessionAsync(RoslynFixOperationType, paths, ct);
        if (rollback.Files.Count != paths.Count)
        {
            await _bulkEditService.RollbackBulkEditAsync(rollback.RollbackId, CancellationToken.None);
            return (null, "Could not back up every file to be fixed; nothing was written");
        }

        try
        {
            foreach (var document in documents)
            {
                await WritePreservingEncodingAsync(document.FilePath, document.NewText, ct);
            }
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(WriteFixedDocumentsAsync));
            var restore = await _bulkEditService.RollbackBulkEditAsync(rollback.RollbackId, CancellationToken.None);
            return (null, restore.Success
                ? $"Writing the fixes failed ({ex.Message}); the original files were restored"
                : $"Writing the fixes failed ({ex.Message}) and restoring the originals also failed: {restore.Error}; rollback ID {rollback.RollbackId}");
        }

        if (_workspace != null)
        {
            foreach (var document in documents)
            {
                await _workspace.UpdateDocumentAsync(document.FilePath, document.NewText, ct);
            }
        }

        return (rollback.RollbackId, null);
    }

    private static async Task WritePreservingEncodingAsync(string path, string content, CancellationToken ct)
    {
        var encoding = await DetectEncodingAsync(path, ct);
        await File.WriteAllTextAsync(path, content, encoding, ct);
    }

    private static async Task<Encoding> DetectEncodingAsync(string path, CancellationToken ct)
    {
        var prefix = new byte[3];
        int read;
        await using (var stream = File.OpenRead(path))
        {
            read = await stream.ReadAsync(prefix, ct);
        }

        if (read >= 3 && prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }

        if (read >= 2 && prefix[0] == 0xFF && prefix[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        if (read >= 2 && prefix[0] == 0xFE && prefix[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    private static object BuildRoslynFixResult(CodeFixRunResult run, string targetPath, bool preview, string? rollbackId) => new
    {
        preview,
        filePath = targetPath,
        issuesFound = run.Outcomes.Count,
        fixesApplied = run.AppliedCount,
        fixesSkipped = run.SkippedCount,
        fixesFailed = run.FailedCount,
        rollbackId,
        modifiedFiles = preview || (rollbackId == null)
            ? Array.Empty<string>()
            : run.ChangedDocuments.Select(d => d.FilePath).ToArray(),
        changes = run.ChangedDocuments.Select(d => new
        {
            filePath = d.FilePath,
            diff = d.Diff,
            newContent = preview ? d.NewText : null
        }).ToArray(),
        fixes = run.Outcomes.Select(o => new
        {
            ruleId = o.RuleId,
            line = o.Line,
            column = o.Column,
            message = o.Message,
            status = o.Status.ToString().ToLowerInvariant(),
            fixTitle = o.FixTitle,
            provider = o.Provider,
            reason = o.Reason
        }).ToArray()
    };
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Performing the rename and applying the resulting solution changes.
/// </summary>
public partial class RenameSymbolService
{
    private async Task<RenameResult> PerformRenameAsync(
        ISymbol symbol,
        RenameRequest request,
        CancellationToken ct)
    {
        LogPerformRename(_logger, symbol.Name, symbol.Kind, request.NewName);

        var solution = _workspace.Solution;

        // CRITICAL: The symbol must be from the current solution's compilation,
        // not from a cached compilation. Roslyn symbols are tied to a specific compilation.
        // If the symbol is from an old compilation, Renamer won't find all references.
        // Re-resolve the symbol to ensure it's from the current compilation
        var resolvedSymbol = await ResolveSymbolInCurrentCompilationAsync(symbol, solution, ct);
        if (resolvedSymbol != null)
        {
            LogUsingReResolvedSymbol(_logger, resolvedSymbol.Name);
            symbol = resolvedSymbol;
        }
        else
        {
            LogUsingOriginalSymbol(_logger, symbol.Name);
        }

        await LogRenameDiagnosticsAsync(symbol, solution, ct);

        var renameOptions = new SymbolRenameOptions(
            RenameOverloads: request.RenameOverloads && symbol is IMethodSymbol,
            RenameInStrings: request.RenameInStrings,
            RenameInComments: request.RenameInComments,
            RenameFile: false);

        // Track modified files
        var modifiedFiles = new HashSet<string>();
        var renamedCount = 0;

        // Use Roslyn's Renamer API for the actual rename.
        // Failures propagate to RenameSymbolAsync, which logs them and converts them to a failed result.
        var newSolution = await Renamer.RenameSymbolAsync(
            solution,
            symbol,
            renameOptions,
            request.NewName,
            ct);

        // Calculate changes
        var changes = newSolution.GetChanges(solution);
        var projectChanges = changes.GetProjectChanges().ToList();
        LogRenameProjectChanges(_logger, projectChanges.Count);

        var fileChanges = new List<FileChange>();

        // A file shared by several projects (multi-targeting, linked files) changes once per project;
        // report and count each file only once.
        var processedFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var projectChange in projectChanges)
        {
            var changedDocs = projectChange.GetChangedDocuments().ToList();
            LogProjectChangedDocuments(_logger, projectChange.NewProject.Name, changedDocs.Count);

            foreach (var docId in changedDocs)
            {
                var oldDoc = solution.GetDocument(docId);
                var newDoc = newSolution.GetDocument(docId);

                if (oldDoc == null || newDoc == null)
                {
                    continue;
                }

                if (oldDoc.FilePath != null && !processedFiles.Add(oldDoc.FilePath))
                {
                    continue;
                }

                var oldText = await oldDoc.GetTextAsync(ct);
                var newText = await newDoc.GetTextAsync(ct);
                var textChanges = newText.GetTextChanges(oldText).ToList();

                LogDocumentTextChanges(_logger, oldDoc.Name, textChanges.Count);

                if (textChanges.Count == 0)
                {
                    continue;
                }

                modifiedFiles.Add(oldDoc.FilePath ?? docId.ToString());
                renamedCount += CountSymbolRenames(oldText, textChanges, request);

                if (request.PreviewOnly)
                {
                    fileChanges.Add(new FileChange
                    {
                        FilePath = oldDoc.FilePath ?? "",
                        Changes = textChanges.Select(tc => new TextChange
                        {
                            Span = tc.Span,
                            NewText = tc.NewText!
                        }).ToList()
                    });
                }
            }
        }

        // Apply changes if not preview mode
        if (!request.PreviewOnly)
        {
            if (!_workspace.Solution.Workspace.TryApplyChanges(newSolution))
            {
                return RenameResult.Failed(ApplyChangesFailedErrors);
            }

            await _workspace.RefreshAfterSolutionChangeAsync(projectChanges.Select(pc => pc.ProjectId), ct);
        }

        return new RenameResult
        {
            Success = true,
            RenamedCount = renamedCount,
            FilesModified = modifiedFiles.ToList(),
            Preview = request.PreviewOnly ? fileChanges : null
        };
    }

    /// <summary>
    /// Count actual symbol renames, not just text change spans.
    /// Roslyn may consolidate multiple renames into fewer text changes.
    /// </summary>
    private int CountSymbolRenames(
        SourceText oldText,
        List<Microsoft.CodeAnalysis.Text.TextChange> textChanges,
        RenameRequest request)
    {
        int symbolRenames = 0;
        foreach (var change in textChanges)
        {
            var oldTextSpan = oldText.GetSubText(change.Span).ToString();
            LogTextChange(_logger, change.Span, oldTextSpan, change.NewText);

            // Count occurrences of old name being replaced
            // Handle case where Roslyn does bulk replacements
            if (change.NewText != null)
            {
                int oldNameCount = CountOccurrences(oldTextSpan, request.OldName);
                int newNameCount = CountOccurrences(change.NewText, request.NewName);
                symbolRenames += Math.Max(oldNameCount, newNameCount);
            }
        }
        return symbolRenames;
    }

    /// <summary>
    /// Diagnostic logging of the symbol, the solution and the references Roslyn can find.
    /// Skipped entirely unless Information logging is enabled, as finding references is expensive.
    /// </summary>
    private async Task LogRenameDiagnosticsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        LogRenamingSymbolDetails(_logger, symbol.Name, symbol.Kind, symbol.ContainingType?.Name ?? "none", symbol.Locations.Length);

        var projectCount = 0;
        var documentCount = 0;
        foreach (var project in solution.Projects)
        {
            projectCount++;
            documentCount += project.DocumentIds.Count;
        }
        LogSolutionState(_logger, projectCount, documentCount);

        try
        {
            var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(
                symbol, solution, ct);
            var refList = references.ToList();
            LogReferenceGroupCount(_logger, refList.Count);
            foreach (var refGroup in refList)
            {
                var locationCount = refGroup.Locations.Count();
                LogReferenceLocationCount(_logger, refGroup.Definition.Name, locationCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSymbolFinderError(_logger, ex);
        }
    }

    // NOTE: FilterCandidatesAsync removed - filtering is now handled by SymbolSelector
    // in the SymbolFinderService as part of the unified selection process.
}

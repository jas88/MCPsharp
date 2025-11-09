using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Automated code fix provider for unused code
/// Removes unused local variables and private fields
/// </summary>
public class UnusedCodeProvider : AutomatedCodeFixProviderBase
{
    public override string Id => "UnusedCode";
    public override string Name => "Unused Code Remover";
    public override string Description => "Removes unused local variables and private fields to improve code clarity";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            UnusedCodeAnalyzer.UnusedLocalDiagnosticId,
            UnusedCodeAnalyzer.UnusedFieldDiagnosticId);

    public override FixProfile Profile => FixProfile.Conservative;

    public override bool IsFullyAutomated => true; // Safe to apply automatically (conservative)

    private readonly UnusedCodeAnalyzer _analyzer = new();
    private readonly UnusedCodeFixer _fixer = new();

    public UnusedCodeProvider(ILogger<UnusedCodeProvider> logger) : base(logger)
    {
    }

    public override DiagnosticAnalyzer GetAnalyzer() => _analyzer;

    public override CodeFixProvider GetCodeFixProvider() => _fixer;

    protected override async Task<DocumentFixResult> ApplySingleDocumentFixAsync(
        Document document,
        FixConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var filePath = document.FilePath ?? "Unknown";

        try
        {
            // Get diagnostics for this document
            var diagnostics = await GetDocumentDiagnosticsAsync(document, cancellationToken);

            if (diagnostics.IsEmpty)
            {
                return new DocumentFixResult
                {
                    FilePath = filePath,
                    Modified = false,
                    DiagnosticsFound = 0,
                    DiagnosticsFixed = 0
                };
            }

            Logger.LogDebug("Found {Count} unused code issues in {FilePath}",
                diagnostics.Length, filePath);

            var currentDocument = document;
            var fixedCount = 0;

            // Apply fixes one by one
            foreach (var diagnostic in diagnostics)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var root = await currentDocument.GetSyntaxRootAsync(cancellationToken);
                if (root == null)
                    continue;

                // Create a code fix context
                var actions = new List<CodeAction>();
                var context = new CodeFixContext(
                    currentDocument,
                    diagnostic,
                    (action, _) => actions.Add(action),
                    cancellationToken);

                await _fixer.RegisterCodeFixesAsync(context);

                if (actions.Any())
                {
                    // Apply the first code action
                    var codeAction = actions.First();
                    currentDocument = await ApplyCodeActionAsync(currentDocument, codeAction, cancellationToken);
                    fixedCount++;

                    Logger.LogTrace("Applied fix for diagnostic at {Location}",
                        diagnostic.Location.GetLineSpan());
                }
            }

            // Save the modified document if changes were made
            if (fixedCount > 0 && currentDocument != document)
            {
                var text = await currentDocument.GetTextAsync(cancellationToken);
                var originalFilePath = document.FilePath;

                if (!string.IsNullOrEmpty(originalFilePath))
                {
                    await File.WriteAllTextAsync(originalFilePath, text.ToString(), cancellationToken);
                }
            }

            return new DocumentFixResult
            {
                FilePath = filePath,
                Modified = fixedCount > 0,
                DiagnosticsFound = diagnostics.Length,
                DiagnosticsFixed = fixedCount
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error fixing document: {FilePath}", filePath);

            return new DocumentFixResult
            {
                FilePath = filePath,
                Modified = false,
                DiagnosticsFound = 0,
                DiagnosticsFixed = 0,
                ErrorMessage = ex.Message
            };
        }
    }
}

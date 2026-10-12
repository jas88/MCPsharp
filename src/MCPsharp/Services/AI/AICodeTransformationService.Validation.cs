using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace MCPsharp.Services.AI;

public partial class AICodeTransformationService
{
    /// <summary>
    /// Validate that transformed code compiles.
    /// </summary>
    private async Task<bool> ValidateCodeAsync(
        string code,
        string filePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse to syntax tree
            var syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath, cancellationToken: cancellationToken);

            // Check for syntax errors
            var diagnostics = syntaxTree.GetDiagnostics(cancellationToken);
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

            if (errors.Count > 0)
            {
                LogSyntaxErrors(_logger, errors.Count);
                foreach (var error in errors.Take(5))
                {
                    LogDiagnostic(_logger, error.GetMessage(CultureInfo.InvariantCulture));
                }
                return false;
            }

            // Check semantic errors against the workspace compilation when the file is part of it
            return await ValidateSemanticsAsync(syntaxTree, filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            LogValidationFailed(_logger, ex);
            return false;
        }
    }

    /// <summary>
    /// Replace the file's syntax tree in the workspace compilation and reject the transformation
    /// if it introduces semantic errors that the original file did not have.
    /// Returns true when the file is not part of a loaded workspace (syntax validation only).
    /// </summary>
    private async Task<bool> ValidateSemanticsAsync(
        SyntaxTree newTree,
        string filePath,
        CancellationToken cancellationToken)
    {
        var document = _workspace.GetDocument(filePath);
        if (document == null)
        {
            return true;
        }

        // Validate against the compilation of the project that owns the file
        var oldTree = await document.GetSyntaxTreeAsync(cancellationToken);
        var compilation = oldTree == null ? null : _workspace.GetCompilationForTree(oldTree);
        if (oldTree == null || compilation == null)
        {
            return true;
        }

        var reparsedTree = newTree.WithRootAndOptions(await newTree.GetRootAsync(cancellationToken), oldTree.Options);
        var newCompilation = compilation.ReplaceSyntaxTree(oldTree, reparsedTree);

        var originalErrorCount = CountErrors(compilation.GetSemanticModel(oldTree).GetDiagnostics(cancellationToken: cancellationToken));
        var newErrors = newCompilation.GetSemanticModel(reparsedTree).GetDiagnostics(cancellationToken: cancellationToken)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if (newErrors.Count <= originalErrorCount)
        {
            return true;
        }

        LogSemanticErrors(_logger, newErrors.Count, originalErrorCount);
        foreach (var error in newErrors.Take(5))
        {
            LogDiagnostic(_logger, error.GetMessage(CultureInfo.InvariantCulture));
        }
        return false;
    }

    private static int CountErrors(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
}

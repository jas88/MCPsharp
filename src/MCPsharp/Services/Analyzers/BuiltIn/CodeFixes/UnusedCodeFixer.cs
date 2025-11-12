using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Fixes unused code by removing unused local variables and private fields
/// </summary>
/// <remarks>
/// Conservative fixer that only removes code that is provably unused:
/// - Removes unused local variable declarations
/// - Removes unused private field declarations
/// - Does not remove if there are any side effects
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnusedCodeFixer)), Shared]
public class UnusedCodeFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            UnusedCodeAnalyzer.UnusedLocalDiagnosticId,
            UnusedCodeAnalyzer.UnusedFieldDiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Handle unused local variable
        if (diagnostic.Id == UnusedCodeAnalyzer.UnusedLocalDiagnosticId)
        {
            var variableDeclarator = root.FindToken(diagnosticSpan.Start)
                .Parent?.AncestorsAndSelf()
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();

            if (variableDeclarator != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove unused local variable",
                        c => RemoveUnusedLocalAsync(context.Document, variableDeclarator, c),
                        nameof(UnusedCodeFixer) + "_Local"),
                    diagnostic);
            }
        }
        // Handle unused private field
        else if (diagnostic.Id == UnusedCodeAnalyzer.UnusedFieldDiagnosticId)
        {
            var fieldDeclaration = root.FindToken(diagnosticSpan.Start)
                .Parent?.AncestorsAndSelf()
                .OfType<FieldDeclarationSyntax>()
                .FirstOrDefault();

            if (fieldDeclaration != null)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove unused private field",
                        c => RemoveUnusedFieldAsync(context.Document, fieldDeclaration, c),
                        nameof(UnusedCodeFixer) + "_Field"),
                    diagnostic);
            }
        }
    }

    private async Task<Document> RemoveUnusedLocalAsync(
        Document document,
        VariableDeclaratorSyntax variableDeclarator,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
            return document;

        // Find the parent declaration
        var declaration = variableDeclarator.Parent as VariableDeclarationSyntax;
        var statement = declaration?.Parent as LocalDeclarationStatementSyntax;

        if (statement == null || declaration == null)
            return document;

        // If this is the only variable in the declaration, remove the entire statement
        if (declaration.Variables.Count == 1)
        {
            var newRoot = root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
            if (newRoot == null)
                return document;

            return document.WithSyntaxRoot(newRoot);
        }
        else
        {
            // Multiple variables in declaration, only remove this one
            var newDeclaration = declaration.RemoveNode(variableDeclarator, SyntaxRemoveOptions.KeepNoTrivia);
            if (newDeclaration == null || declaration == null)
                return document;

            var newRoot = root.ReplaceNode(declaration, newDeclaration);
            if (newRoot == null)
                return document;

            return document.WithSyntaxRoot(newRoot);
        }
    }

    private async Task<Document> RemoveUnusedFieldAsync(
        Document document,
        FieldDeclarationSyntax fieldDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
            return document;

        // Remove the entire field declaration
        var newRoot = root.RemoveNode(fieldDeclaration, SyntaxRemoveOptions.KeepNoTrivia);
        if (newRoot == null)
            return document;

        return document.WithSyntaxRoot(newRoot);
    }
}

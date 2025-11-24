using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Fixes private methods that don't use instance members by adding the static modifier
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StaticMethodFixer)), Shared]
public class StaticMethodFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(StaticMethodAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var methodDecl = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (methodDecl == null)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Make method static",
                c => MakeMethodStaticAsync(context.Document, methodDecl, c),
                nameof(StaticMethodFixer)),
            diagnostic);
    }

    private async Task<Document> MakeMethodStaticAsync(
        Document document,
        MethodDeclarationSyntax methodDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
            return document;

        // Add static modifier
        var staticModifier = SyntaxFactory.Token(SyntaxKind.StaticKeyword);

        // Insert static modifier in the correct position
        // Typically: [access modifier] static [other modifiers] returnType methodName
        var newModifiers = InsertStaticModifier(methodDecl.Modifiers, staticModifier);

        var newMethod = methodDecl.WithModifiers(newModifiers);

        var newRoot = root.ReplaceNode(methodDecl, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxTokenList InsertStaticModifier(SyntaxTokenList modifiers, SyntaxToken staticToken)
    {
        // Check if static modifier already exists (prevent duplicates)
        if (modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            return modifiers;

        // Find the correct position to insert 'static'
        // Order: access modifiers -> static -> other modifiers (async, unsafe, etc.)

        int insertPosition = 0;

        // Access modifiers come first
        for (int i = 0; i < modifiers.Count; i++)
        {
            var modifier = modifiers[i];
            if (IsAccessModifier(modifier))
            {
                insertPosition = i + 1;
            }
            else
            {
                break;
            }
        }

        // Insert static at the determined position
        if (insertPosition == 0)
        {
            // No access modifiers, insert at the beginning
            return modifiers.Insert(0, staticToken.WithTrailingTrivia(SyntaxFactory.Space));
        }
        else if (insertPosition >= modifiers.Count)
        {
            // After all access modifiers, at the end
            // Don't add leading trivia - previous modifier already has trailing space
            return modifiers.Add(staticToken.WithTrailingTrivia(SyntaxFactory.Space));
        }
        else
        {
            // Insert in the middle
            // Don't add leading trivia - previous modifier already has trailing space
            return modifiers.Insert(insertPosition, staticToken
                .WithTrailingTrivia(SyntaxFactory.Space));
        }
    }

    private static bool IsAccessModifier(SyntaxToken token)
    {
        return token.IsKind(SyntaxKind.PublicKeyword) ||
               token.IsKind(SyntaxKind.PrivateKeyword) ||
               token.IsKind(SyntaxKind.ProtectedKeyword) ||
               token.IsKind(SyntaxKind.InternalKeyword);
    }
}

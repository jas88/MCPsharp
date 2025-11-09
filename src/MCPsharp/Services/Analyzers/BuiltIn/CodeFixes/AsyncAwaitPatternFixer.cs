using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Fixes async methods that don't use await
/// Removes async keyword and wraps return values appropriately
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncAwaitPatternFixer)), Shared]
public class AsyncAwaitPatternFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(AsyncAwaitPatternAnalyzer.DiagnosticId);

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
                "Remove async and wrap return value",
                c => RemoveAsyncAndWrapReturnAsync(context.Document, methodDecl, c),
                nameof(AsyncAwaitPatternFixer)),
            diagnostic);
    }

    private async Task<Document> RemoveAsyncAndWrapReturnAsync(
        Document document,
        MethodDeclarationSyntax methodDecl,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
            return document;

        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
        if (methodSymbol == null)
            return document;

        // Remove async modifier
        var newModifiers = methodDecl.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.AsyncKeyword))
            .ToArray();

        var modifierList = SyntaxFactory.TokenList(newModifiers);

        // Determine if return type is Task or Task<T>
        var returnType = methodSymbol.ReturnType;
        var isGenericTask = returnType is INamedTypeSymbol namedType &&
                           namedType.IsGenericType &&
                           namedType.Name == "Task";

        // Use rewriter to wrap return statements
        var rewriter = new AsyncReturnRewriter(isGenericTask);
        var newMethod = (MethodDeclarationSyntax)rewriter.Visit(methodDecl);
        newMethod = newMethod.WithModifiers(modifierList);

        var newRoot = root.ReplaceNode(methodDecl, newMethod);
        return document.WithSyntaxRoot(newRoot);
    }

    /// <summary>
    /// Syntax rewriter that wraps return statements with Task.FromResult or Task.CompletedTask
    /// </summary>
    private class AsyncReturnRewriter : CSharpSyntaxRewriter
    {
        private readonly bool _isGenericTask;

        public AsyncReturnRewriter(bool isGenericTask)
        {
            _isGenericTask = isGenericTask;
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            // Handle void Task (Task with no return value)
            if (node.Expression == null)
            {
                // return; -> return Task.CompletedTask;
                var completedTask = SyntaxFactory.ParseExpression("Task.CompletedTask")
                    .WithLeadingTrivia(node.GetLeadingTrivia())
                    .WithTrailingTrivia(node.GetTrailingTrivia());

                return node.WithExpression(completedTask);
            }

            // Check if already wrapped
            if (node.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "FromResult")
            {
                return node; // Already wrapped
            }

            if (node.Expression.ToString() == "Task.CompletedTask")
            {
                return node; // Already using Task.CompletedTask
            }

            // For Task<T>, wrap with Task.FromResult(...)
            if (_isGenericTask)
            {
                var wrappedExpr = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Task"),
                        SyntaxFactory.IdentifierName("FromResult")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(node.Expression))));

                return node.WithExpression(wrappedExpr);
            }
            else
            {
                // For non-generic Task, should use Task.CompletedTask
                // But if there's an expression, it's likely wrong - keep original
                return node;
            }
        }

        public override SyntaxNode? VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            // Handle expression-bodied methods
            // async Task<int> GetValue() => 42;
            // Should become: Task<int> GetValue() => Task.FromResult(42);

            if (node.Expression == null)
                return node;

            // Check if already wrapped
            if (node.Expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "FromResult")
            {
                return node; // Already wrapped
            }

            if (_isGenericTask)
            {
                var wrappedExpr = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("Task"),
                        SyntaxFactory.IdentifierName("FromResult")),
                    SyntaxFactory.ArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Argument(node.Expression))));

                return node.WithExpression(wrappedExpr);
            }

            return node;
        }
    }
}

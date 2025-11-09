using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Syntax rewriter for batch processing of async/await pattern fixes
/// Efficiently processes multiple methods in a syntax tree
/// </summary>
public class AsyncAwaitRewriter : CSharpSyntaxRewriter
{
    private readonly SemanticModel _semanticModel;
    private readonly ILogger? _logger;
    private readonly List<MethodTransformation> _transformations = new();

    public AsyncAwaitRewriter(SemanticModel semanticModel, ILogger? logger = null)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        _logger = logger;
    }

    /// <summary>
    /// Get all transformations applied by this rewriter
    /// </summary>
    public ImmutableArray<MethodTransformation> GetTransformations() => _transformations.ToImmutableArray();

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        // Check if this method should be transformed
        if (!ShouldTransformMethod(node))
            return base.VisitMethodDeclaration(node);

        try
        {
            var methodSymbol = _semanticModel.GetDeclaredSymbol(node);
            if (methodSymbol == null)
                return base.VisitMethodDeclaration(node);

            var returnType = methodSymbol.ReturnType;
            var isGenericTask = returnType is INamedTypeSymbol namedType &&
                               namedType.IsGenericType &&
                               namedType.Name == "Task";

            // Remove async modifier
            var newModifiers = node.Modifiers
                .Where(m => !m.IsKind(SyntaxKind.AsyncKeyword))
                .ToArray();

            var modifierList = SyntaxFactory.TokenList(newModifiers);

            // Transform return statements
            var returnRewriter = new AsyncReturnRewriter(isGenericTask);
            var newMethod = (MethodDeclarationSyntax)returnRewriter.Visit(node);
            newMethod = newMethod.WithModifiers(modifierList);

            // Track transformation
            _transformations.Add(new MethodTransformation
            {
                MethodName = methodSymbol.Name,
                FilePath = node.SyntaxTree.FilePath,
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                WasGenericTask = isGenericTask,
                Success = true
            });

            _logger?.LogDebug("Transformed method {MethodName} at line {Line}",
                methodSymbol.Name, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

            return newMethod;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error transforming method at line {Line}",
                node.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

            _transformations.Add(new MethodTransformation
            {
                MethodName = node.Identifier.Text,
                FilePath = node.SyntaxTree.FilePath,
                LineNumber = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                Success = false,
                ErrorMessage = ex.Message
            });

            return base.VisitMethodDeclaration(node);
        }
    }

    private bool ShouldTransformMethod(MethodDeclarationSyntax node)
    {
        // Must have async modifier
        if (!node.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return false;

        // Must not contain await
        var hasAwait = node.DescendantNodes()
            .OfType<AwaitExpressionSyntax>()
            .Any();

        if (hasAwait)
            return false;

        // Must return Task or Task<T>
        var methodSymbol = _semanticModel.GetDeclaredSymbol(node);
        if (methodSymbol == null)
            return false;

        var returnType = methodSymbol.ReturnType;
        if (returnType.Name != "Task" ||
            returnType.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks")
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Internal rewriter for transforming return statements
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
            if (node.Expression == null)
            {
                // return; -> return Task.CompletedTask;
                var completedTask = SyntaxFactory.ParseExpression("Task.CompletedTask");
                return node.WithExpression(completedTask);
            }

            // Check if already wrapped
            if (IsAlreadyWrapped(node.Expression))
                return node;

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

        public override SyntaxNode? VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            if (node.Expression == null)
                return node;

            if (IsAlreadyWrapped(node.Expression))
                return node;

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

        private static bool IsAlreadyWrapped(ExpressionSyntax expression)
        {
            if (expression is InvocationExpressionSyntax invocation &&
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "FromResult")
            {
                return true;
            }

            if (expression.ToString() == "Task.CompletedTask")
            {
                return true;
            }

            return false;
        }
    }
}

/// <summary>
/// Record of a method transformation
/// </summary>
public record MethodTransformation
{
    public string MethodName { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public int LineNumber { get; init; }
    public bool WasGenericTask { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

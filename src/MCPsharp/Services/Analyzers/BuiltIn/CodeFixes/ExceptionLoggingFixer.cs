using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Fixes improper exception logging by converting string-based exception messages
/// to proper exception parameter logging
/// </summary>
/// <remarks>
/// Transforms:
///   _logger.LogError($"Error: {ex.Message}")
/// To:
///   _logger.LogError(ex, "Error")
///
/// This ensures full exception details are captured in logs
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExceptionLoggingFixer)), Shared]
public class ExceptionLoggingFixer : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ExceptionLoggingAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null)
            return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        var invocation = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault();

        if (invocation == null)
            return;

        // Get exception variable name from diagnostic properties
        if (!diagnostic.Properties.TryGetValue("ExceptionVariable", out var exceptionVarName) ||
            string.IsNullOrEmpty(exceptionVarName))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Pass '{exceptionVarName}' as exception parameter",
                c => FixExceptionLoggingAsync(context.Document, invocation, exceptionVarName, c),
                nameof(ExceptionLoggingFixer)),
            diagnostic);
    }

    private async Task<Document> FixExceptionLoggingAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        string exceptionVarName,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null)
            return document;

        // Extract the logging method name
        var methodName = ((MemberAccessExpressionSyntax)invocation.Expression).Name.Identifier.Text;

        // Use rewriter to transform the logging call
        var rewriter = new ExceptionLoggingRewriter(exceptionVarName, invocation, semanticModel);
        var newRoot = rewriter.Visit(root);

        if (newRoot == null)
            return document;

        return document.WithSyntaxRoot(newRoot);
    }
}

/// <summary>
/// Syntax rewriter that transforms improper exception logging to proper format
/// </summary>
internal class ExceptionLoggingRewriter : CSharpSyntaxRewriter
{
    private readonly string _exceptionVarName;
    private readonly InvocationExpressionSyntax _targetInvocation;
    private readonly SemanticModel _semanticModel;

    public ExceptionLoggingRewriter(
        string exceptionVarName,
        InvocationExpressionSyntax targetInvocation,
        SemanticModel semanticModel)
    {
        _exceptionVarName = exceptionVarName;
        _targetInvocation = targetInvocation;
        _semanticModel = semanticModel;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // Only transform the target invocation
        if (node != _targetInvocation)
            return base.VisitInvocationExpression(node);

        // Extract the simplified message
        var simplifiedMessage = ExtractSimplifiedMessage(node.ArgumentList.Arguments[0].Expression);

        // Create exception variable reference
        var exceptionArg = SyntaxFactory.Argument(
            SyntaxFactory.IdentifierName(_exceptionVarName));

        // Create message argument
        var messageArg = SyntaxFactory.Argument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(simplifiedMessage)));

        // Build new argument list: (exception, message, ...)
        var newArguments = new List<ArgumentSyntax> { exceptionArg, messageArg };

        // Preserve any additional arguments (like log level, event ID)
        if (node.ArgumentList.Arguments.Count > 1)
        {
            for (int i = 1; i < node.ArgumentList.Arguments.Count; i++)
            {
                newArguments.Add(node.ArgumentList.Arguments[i]);
            }
        }

        var newArgumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(newArguments));

        return node.WithArgumentList(newArgumentList)
            .WithLeadingTrivia(node.GetLeadingTrivia())
            .WithTrailingTrivia(node.GetTrailingTrivia());
    }

    private string ExtractSimplifiedMessage(ExpressionSyntax expression)
    {
        // For interpolated strings like $"Error: {ex.Message}"
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            var simplifiedParts = new List<string>();

            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                {
                    simplifiedParts.Add(text.TextToken.Text);
                }
                else if (content is InterpolationSyntax interpolation)
                {
                    // Skip exception-related interpolations
                    if (!IsExceptionReference(interpolation.Expression))
                    {
                        simplifiedParts.Add($"{{{interpolation.Expression}}}");
                    }
                }
            }

            return string.Join("", simplifiedParts).Trim();
        }

        // For string concatenation like "Error: " + ex.Message
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression))
        {
            var parts = new List<string>();
            CollectNonExceptionParts(binary, parts);
            return string.Join("", parts).Trim();
        }

        // For simple string literals
        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        // Fallback: use generic message
        return "An error occurred";
    }

    private void CollectNonExceptionParts(BinaryExpressionSyntax binary, List<string> parts)
    {
        // Recursively collect non-exception parts from concatenation
        if (binary.Left is BinaryExpressionSyntax leftBinary &&
            leftBinary.IsKind(SyntaxKind.AddExpression))
        {
            CollectNonExceptionParts(leftBinary, parts);
        }
        else if (binary.Left is LiteralExpressionSyntax leftLiteral &&
                 leftLiteral.IsKind(SyntaxKind.StringLiteralExpression))
        {
            parts.Add(leftLiteral.Token.ValueText);
        }

        if (binary.Right is BinaryExpressionSyntax rightBinary &&
            rightBinary.IsKind(SyntaxKind.AddExpression))
        {
            CollectNonExceptionParts(rightBinary, parts);
        }
        else if (binary.Right is LiteralExpressionSyntax rightLiteral &&
                 rightLiteral.IsKind(SyntaxKind.StringLiteralExpression))
        {
            parts.Add(rightLiteral.Token.ValueText);
        }
    }

    private bool IsExceptionReference(ExpressionSyntax expression)
    {
        // Check if this references the exception variable
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;

            // Check for ex.Message, ex.ToString(), etc.
            if (memberName is "Message" or "ToString" or "StackTrace")
            {
                var symbolInfo = _semanticModel.GetSymbolInfo(memberAccess.Expression);
                if (symbolInfo.Symbol is ILocalSymbol local && local.Name == _exceptionVarName)
                    return true;
                if (symbolInfo.Symbol is IParameterSymbol param && param.Name == _exceptionVarName)
                    return true;
            }
        }

        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax invokeAccess)
        {
            var symbolInfo = _semanticModel.GetSymbolInfo(invokeAccess.Expression);
            if (symbolInfo.Symbol is ILocalSymbol local && local.Name == _exceptionVarName)
                return true;
            if (symbolInfo.Symbol is IParameterSymbol param && param.Name == _exceptionVarName)
                return true;
        }

        return false;
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// The statements (or expression) that an extract-method refactoring of a code block would move
/// </summary>
internal sealed class ExtractionRegion
{
    private ExtractionRegion(SyntaxNode owner, IReadOnlyList<StatementSyntax> statements, ExpressionSyntax? expression, bool isWholeBody)
    {
        Owner = owner;
        Statements = statements;
        Expression = expression;
        IsWholeBody = isWholeBody;
    }

    /// <summary>The node the region was derived from</summary>
    public SyntaxNode Owner { get; }

    /// <summary>Statements in the region (empty for an expression-bodied member)</summary>
    public IReadOnlyList<StatementSyntax> Statements { get; }

    /// <summary>Expression body, when the region is an expression-bodied member</summary>
    public ExpressionSyntax? Expression { get; }

    /// <summary>Whether the region is the complete body of a method, constructor, accessor or function</summary>
    public bool IsWholeBody { get; }

    /// <summary>Nodes making up the region</summary>
    public IEnumerable<SyntaxNode> Nodes => Expression != null ? [Expression] : Statements;

    /// <summary>Start of the region in the source text</summary>
    public int SpanStart => Expression?.SpanStart ?? (Statements.Count > 0 ? Statements[0].SpanStart : Owner.SpanStart);

    /// <summary>End of the region in the source text</summary>
    public int SpanEnd => Expression?.Span.End ?? (Statements.Count > 0 ? Statements[^1].Span.End : Owner.Span.End);

    /// <summary>
    /// Determines the extractable region of a node, or <c>null</c> when the node cannot be extracted into a method.
    /// </summary>
    public static ExtractionRegion? From(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax { Body: { } body } => new ExtractionRegion(node, body.Statements, null, true),
        BaseMethodDeclarationSyntax { ExpressionBody: { } arrow } => new ExtractionRegion(node, [], arrow.Expression, true),
        BlockSyntax block => new ExtractionRegion(node, block.Statements, null, IsFunctionBody(block)),
        _ => null
    };

    private static bool IsFunctionBody(BlockSyntax block) => block.Parent is
        BaseMethodDeclarationSyntax or
        AccessorDeclarationSyntax or
        LocalFunctionStatementSyntax or
        AnonymousFunctionExpressionSyntax;

    /// <summary>
    /// Whether a node lies within the region
    /// </summary>
    public bool Contains(SyntaxNode node) => node.SpanStart >= SpanStart && node.Span.End <= SpanEnd;
}

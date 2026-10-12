using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MCPsharp.Models.Refactoring;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Resolving the selected text span to complete statements or a single expression.
/// </summary>
public partial class ExtractMethodService
{
    private static readonly List<string> SelectionSuggestions =
        ["Select complete statement(s) within a method, or pass start/end columns that cover exactly one expression"];

    /// <summary>
    /// The code being extracted: contiguous sibling statements, or one expression.
    /// </summary>
    private sealed class ExtractionSelection
    {
        public required MemberDeclarationSyntax ContainingMember { get; init; }
        public IReadOnlyList<StatementSyntax> Statements { get; init; } = [];
        public ExpressionSyntax? Expression { get; init; }

        public SyntaxNode FirstNode => (SyntaxNode?)Expression ?? Statements[0];

        public IEnumerable<SyntaxNode> Nodes =>
            Expression != null ? [Expression] : Statements;
    }

    private static (ExtractionSelection? Selection, ExtractMethodResult? Error) ResolveSelection(
        SyntaxNode root,
        SemanticModel model,
        TextSpan span)
    {
        var statements = UnwrapBodyBlock(GetSelectedStatements(root, span));
        if (statements.Count > 0)
        {
            return BuildStatementSelection(statements);
        }

        var expression = FindSelectedExpression(root, model, span);
        if (expression != null)
        {
            return BuildSelection(expression, [], expression);
        }

        return (null, ExtractMethodResult.CreateError(
            "No complete statements selected", "INCOMPLETE_SELECTION", SelectionSuggestions));
    }

    private static List<StatementSyntax> GetSelectedStatements(SyntaxNode root, TextSpan selection)
    {
        var statements = root.DescendantNodes(n => n.Span.IntersectsWith(selection))
            .OfType<StatementSyntax>()
            .Where(s => selection.Contains(s.Span))
            .ToList();

        // Keep only the outermost statements inside the selection
        return statements
            .Where(stmt => !statements.Exists(other => other != stmt && other.Span.Contains(stmt.Span)))
            .ToList();
    }

    /// <summary>
    /// Selecting a whole member body (braces included) means its statements.
    /// </summary>
    private static List<StatementSyntax> UnwrapBodyBlock(List<StatementSyntax> statements)
    {
        if (statements is [BlockSyntax block] && block.Parent is not (StatementSyntax or SwitchSectionSyntax))
        {
            return block.Statements.ToList();
        }

        return statements;
    }

    private static (ExtractionSelection? Selection, ExtractMethodResult? Error) BuildStatementSelection(
        List<StatementSyntax> statements)
    {
        var parent = statements[0].Parent;
        if (statements.Exists(s => s.Parent != parent))
        {
            return (null, ExtractMethodResult.CreateError(
                "Selected statements must belong to the same block", "NON_CONTIGUOUS_SELECTION", SelectionSuggestions));
        }

        var siblings = parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default
        };

        if (siblings.Count == 0 ? statements.Count != 1 : !AreContiguous(siblings, statements))
        {
            return (null, ExtractMethodResult.CreateError(
                "Selected statements must be contiguous", "NON_CONTIGUOUS_SELECTION", SelectionSuggestions));
        }

        var hasGoto = statements.Exists(s =>
            s.DescendantNodesAndSelf().Any(n => n is GotoStatementSyntax or LabeledStatementSyntax));
        if (hasGoto)
        {
            return (null, ExtractMethodResult.CreateError(
                "Cannot extract code containing goto statements or labels", "GOTO_LABEL_PRESENT"));
        }

        return BuildSelection(statements[0], statements, null);
    }

    private static bool AreContiguous(SyntaxList<StatementSyntax> siblings, List<StatementSyntax> statements)
    {
        var first = siblings.IndexOf(statements[0]);
        return first >= 0 && statements.Select((s, i) => siblings.IndexOf(s) == first + i).All(ok => ok);
    }

    private static (ExtractionSelection? Selection, ExtractMethodResult? Error) BuildSelection(
        SyntaxNode anchor,
        IReadOnlyList<StatementSyntax> statements,
        ExpressionSyntax? expression)
    {
        var member = anchor.Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault(m => m.Parent is TypeDeclarationSyntax);

        if (member is null or BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
        {
            return (null, ExtractMethodResult.CreateError("Selection must be within a method", "NOT_IN_METHOD"));
        }

        return (new ExtractionSelection
        {
            ContainingMember = member,
            Statements = statements,
            Expression = expression
        }, null);
    }

    /// <summary>
    /// Find an expression whose span is exactly the (whitespace-trimmed) selection and that can be
    /// replaced by a method call: it must have a value and must not be an assignment target.
    /// </summary>
    private static ExpressionSyntax? FindSelectedExpression(SyntaxNode root, SemanticModel model, TextSpan span)
    {
        var trimmed = TrimWhitespace(root.GetText(), span);
        if (trimmed.IsEmpty)
        {
            return null;
        }

        var expression = root.FindNode(trimmed, getInnermostNodeForTie: true)
            .AncestorsAndSelf()
            .TakeWhile(n => n.Span.Contains(trimmed))
            .OfType<ExpressionSyntax>()
            .FirstOrDefault(e => e.Span == trimmed);

        if (expression == null || !IsExtractableExpression(expression, model))
        {
            return null;
        }

        return expression;
    }

    private static TextSpan TrimWhitespace(SourceText text, TextSpan span)
    {
        var start = span.Start;
        var end = span.End;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return TextSpan.FromBounds(start, end);
    }

    private static bool IsExtractableExpression(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is TypeSyntax && model.GetSymbolInfo(expression).Symbol is INamespaceOrTypeSymbol)
        {
            return false;
        }

        var isWriteTarget = expression.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == expression,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax =>
                expression.Parent.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
                    or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name == expression,
            MemberBindingExpressionSyntax => true,
            _ => expression is DeclarationExpressionSyntax
        };

        if (isWriteTarget)
        {
            return false;
        }

        var type = model.GetTypeInfo(expression).Type;
        return type is not null && type.SpecialType != SpecialType.System_Void;
    }
}

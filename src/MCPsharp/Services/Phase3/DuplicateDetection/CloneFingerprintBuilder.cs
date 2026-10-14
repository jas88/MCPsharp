using MCPsharp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Builds normalised token fingerprints of syntax nodes for Type-2/Type-3 clone detection.
/// Identifiers are alpha-renamed by first occurrence, literals are abstracted by category and
/// type names (including predefined type keywords) are abstracted to a single placeholder.
/// </summary>
public static class CloneFingerprintBuilder
{
    /// <summary>Placeholder used for any type name</summary>
    public const string TypePlaceholder = "$T";

    /// <summary>Shape placeholder used for any non-type identifier</summary>
    public const string IdentifierPlaceholder = "$ID";

    /// <summary>Placeholder used for string, character and interpolated text literals</summary>
    public const string StringLiteralPlaceholder = "$STR";

    /// <summary>Placeholder used for numeric literals</summary>
    public const string NumericLiteralPlaceholder = "$NUM";

    /// <summary>Prefix of positional alpha-renamed identifier placeholders</summary>
    public const string RenamedIdentifierPrefix = "$V";

    /// <summary>Maximum length of statement text kept in a fingerprint</summary>
    public const int MaxStatementTextLength = 200;

    private const char HashSeparator = '\u0001';

    /// <summary>
    /// Builds a fingerprint of the given node.
    /// </summary>
    /// <param name="node">Syntax node to fingerprint</param>
    /// <param name="semanticModel">Semantic model for the node's tree, if available, used to classify identifiers</param>
    /// <param name="typeNameCache">
    /// Optional cache of identifier classifications, shared between overlapping nodes of the same tree
    /// to avoid re-binding the same names
    /// </param>
    public static CloneFingerprint Build(
        SyntaxNode node,
        SemanticModel? semanticModel = null,
        IDictionary<SyntaxNode, bool>? typeNameCache = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        var tokens = new List<NormalizedToken>();
        var statementBuilders = new List<StatementBuilder>();
        var statementIndices = new Dictionary<SyntaxNode, int>();
        var renameMap = new Dictionary<string, int>(StringComparer.Ordinal);
        var tree = node.SyntaxTree;

        foreach (var token in node.DescendantTokens())
        {
            if (token.IsKind(SyntaxKind.EndOfFileToken) || token.Span.IsEmpty)
            {
                continue;
            }

            var tokenIndex = tokens.Count;
            var statementIndex = RegisterStatements(token, node, tokenIndex, statementBuilders, statementIndices);
            var kind = Classify(token, semanticModel, typeNameCache);
            var position = tree.GetLineSpan(token.Span).StartLinePosition;

            tokens.Add(new NormalizedToken
            {
                Kind = kind,
                Text = token.Text,
                Shape = ShapeOf(kind, token.Text),
                Renamed = RenamedOf(kind, token.Text, renameMap),
                Line = position.Line + 1,
                Column = position.Character + 1,
                StatementIndex = statementIndex
            });
        }

        var statements = statementBuilders.Select(b => b.ToStatementSpan()).ToList();
        var renamedHash = DuplicateDetectionUtils.ComputeHash(
            string.Join(HashSeparator, tokens.Select(t => t.Renamed)));

        return new CloneFingerprint
        {
            Tokens = tokens,
            Statements = statements,
            RenamedHash = renamedHash,
            UsedSemanticModel = semanticModel != null
        };
    }

    /// <summary>
    /// Classifies a token into a normalised token kind.
    /// </summary>
    public static NormalizedTokenKind Classify(
        SyntaxToken token,
        SemanticModel? semanticModel,
        IDictionary<SyntaxNode, bool>? typeNameCache = null)
    {
        var kind = token.Kind();
        switch (kind)
        {
            case SyntaxKind.NumericLiteralToken:
                return NormalizedTokenKind.NumericLiteral;
            case SyntaxKind.StringLiteralToken:
            case SyntaxKind.CharacterLiteralToken:
            case SyntaxKind.InterpolatedStringTextToken:
            case SyntaxKind.SingleLineRawStringLiteralToken:
            case SyntaxKind.MultiLineRawStringLiteralToken:
            case SyntaxKind.Utf8StringLiteralToken:
            case SyntaxKind.Utf8SingleLineRawStringLiteralToken:
            case SyntaxKind.Utf8MultiLineRawStringLiteralToken:
                return NormalizedTokenKind.StringLiteral;
            case SyntaxKind.IdentifierToken:
                return IsTypeIdentifier(token, semanticModel, typeNameCache)
                    ? NormalizedTokenKind.TypeName
                    : NormalizedTokenKind.Identifier;
        }

        if (SyntaxFacts.IsPredefinedType(kind))
        {
            return NormalizedTokenKind.TypeName;
        }

        return SyntaxFacts.IsKeywordKind(kind)
            ? NormalizedTokenKind.Keyword
            : NormalizedTokenKind.Punctuation;
    }

    private static bool IsTypeIdentifier(
        SyntaxToken token,
        SemanticModel? semanticModel,
        IDictionary<SyntaxNode, bool>? typeNameCache)
    {
        if (token.Parent is not SimpleNameSyntax simpleName)
        {
            return false;
        }

        if (typeNameCache == null)
        {
            return IsTypeName(simpleName, semanticModel);
        }

        if (!typeNameCache.TryGetValue(simpleName, out var isType))
        {
            isType = IsTypeName(simpleName, semanticModel);
            typeNameCache[simpleName] = isType;
        }

        return isType;
    }

    private static bool IsTypeName(SimpleNameSyntax simpleName, SemanticModel? semanticModel)
    {
        if (simpleName is IdentifierNameSyntax { IsVar: true })
        {
            return true;
        }

        if (semanticModel != null && semanticModel.SyntaxTree == simpleName.SyntaxTree)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(simpleName);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (symbol != null)
            {
                return symbol is ITypeSymbol or INamespaceSymbol or IAliasSymbol;
            }
        }

        return SyntaxFacts.IsInTypeOnlyContext(simpleName);
    }

    private static string ShapeOf(NormalizedTokenKind kind, string text) => kind switch
    {
        NormalizedTokenKind.Identifier => IdentifierPlaceholder,
        NormalizedTokenKind.TypeName => TypePlaceholder,
        NormalizedTokenKind.StringLiteral => StringLiteralPlaceholder,
        NormalizedTokenKind.NumericLiteral => NumericLiteralPlaceholder,
        _ => text
    };

    private static string RenamedOf(NormalizedTokenKind kind, string text, Dictionary<string, int> renameMap)
    {
        if (kind != NormalizedTokenKind.Identifier)
        {
            return ShapeOf(kind, text);
        }

        if (!renameMap.TryGetValue(text, out var index))
        {
            index = renameMap.Count;
            renameMap[text] = index;
        }

        return RenamedIdentifierPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int RegisterStatements(
        SyntaxToken token,
        SyntaxNode root,
        int tokenIndex,
        List<StatementBuilder> builders,
        Dictionary<SyntaxNode, int> indices)
    {
        // Collect enclosing statements (innermost first) strictly inside the root node
        var chain = new List<StatementSyntax>();
        for (var current = token.Parent; current != null && current != root; current = current.Parent)
        {
            if (current is StatementSyntax statement)
            {
                chain.Add(statement);
            }
        }

        // Register outermost first so parents receive lower indices than children
        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var statement = chain[i];
            if (!indices.TryGetValue(statement, out var index))
            {
                index = builders.Count;
                indices[statement] = index;
                var parentIndex = i + 1 < chain.Count ? indices[chain[i + 1]] : -1;
                builders.Add(new StatementBuilder(statement, index, parentIndex, tokenIndex));
            }

            builders[index].EndTokenIndex = tokenIndex;
        }

        return chain.Count > 0 ? indices[chain[0]] : -1;
    }

    private sealed class StatementBuilder(StatementSyntax statement, int index, int parentIndex, int startTokenIndex)
    {
        private readonly int _startTokenIndex = startTokenIndex;

        public int EndTokenIndex { get; set; } = startTokenIndex;

        public StatementSpan ToStatementSpan()
        {
            var position = statement.SyntaxTree.GetLineSpan(statement.Span).StartLinePosition;
            var text = statement.ToString().Trim();
            if (text.Length > MaxStatementTextLength)
            {
                text = string.Concat(text.AsSpan(0, MaxStatementTextLength), "...");
            }

            return new StatementSpan
            {
                Index = index,
                ParentIndex = parentIndex,
                StartTokenIndex = _startTokenIndex,
                EndTokenIndex = EndTokenIndex,
                Line = position.Line + 1,
                Column = position.Character + 1,
                Text = text
            };
        }
    }
}

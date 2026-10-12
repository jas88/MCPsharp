using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// A single cognitive-complexity increment.
/// </summary>
/// <param name="Line">1-based line of the token that caused the increment.</param>
/// <param name="Position">Absolute character position of that token.</param>
/// <param name="Amount">Points added (1 for fundamental/hybrid increments, 1 + nesting for structural ones).</param>
/// <param name="Nesting">Nesting level at the point of the increment.</param>
/// <param name="Construct">The construct responsible, e.g. <c>if</c>, <c>else</c>, <c>&amp;&amp;</c>, <c>recursion</c>.</param>
public readonly record struct ComplexityIncrement(int Line, int Position, int Amount, int Nesting, string Construct);

/// <summary>
/// A top-level control structure within a function together with the cognitive complexity it contributes.
/// </summary>
public sealed record ComplexityBlock(string Construct, int StartLine, int EndLine, int CognitiveComplexity);

/// <summary>
/// Complexity metrics for one function-like member.
/// </summary>
public sealed record FunctionComplexity
{
    /// <summary>Member name; constructors use the type name, accessors use <c>Property.get</c> style names.</summary>
    public required string Name { get; init; }

    /// <summary>Kind of member, e.g. <c>Method</c>, <c>Constructor</c>, <c>Accessor</c>.</summary>
    public required string Kind { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }

    /// <summary>McCabe cyclomatic complexity: <c>1 + DecisionPoints</c>.</summary>
    public required int CyclomaticComplexity { get; init; }

    /// <summary>SonarSource cognitive complexity.</summary>
    public required int CognitiveComplexity { get; init; }

    /// <summary>Deepest nesting level reached by a nested body (0 for straight-line code).</summary>
    public required int MaxNestingDepth { get; init; }

    public required int DecisionPoints { get; init; }

    public required IReadOnlyList<ComplexityIncrement> Increments { get; init; }

    /// <summary>Control structures starting at nesting level zero, with their cognitive contribution.</summary>
    public required IReadOnlyList<ComplexityBlock> Blocks { get; init; }
}

/// <summary>
/// Computes cyclomatic and cognitive complexity, nesting depth and decision points for C# members.
/// </summary>
/// <remarks>
/// The unit of measurement is the function-like member: methods, constructors, destructors, operators,
/// conversion operators, property/indexer/event accessors and expression-bodied properties/indexers.
/// Lambdas, anonymous methods and local functions are measured as part of their enclosing member (raising
/// nesting for cognitive complexity), so per-member results never overlap. Members without a body, field
/// initialisers and top-level statements are not measured. See <see cref="CyclomaticComplexityWalker"/> and
/// <see cref="CognitiveComplexityWalker"/> for the exact counting rules.
/// </remarks>
public static class CodeComplexityCalculator
{
    /// <summary>
    /// Measures every function-like member found under <paramref name="root"/> (in document order).
    /// </summary>
    public static IReadOnlyList<FunctionComplexity> AnalyzeMembers(SyntaxNode root, CancellationToken cancellationToken = default)
    {
        var results = new List<FunctionComplexity>();
        foreach (var node in root.DescendantNodesAndSelf(n => n == root || !IsMeasuredMember(n)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsMeasuredMember(node))
                results.Add(Analyze(node));
        }

        return results;
    }

    /// <summary>
    /// Returns true when <paramref name="node"/> is a member that <see cref="AnalyzeMembers"/> measures.
    /// </summary>
    public static bool IsMeasuredMember(SyntaxNode node) => GetBodies(node).Length > 0 && node switch
    {
        BaseMethodDeclarationSyntax or AccessorDeclarationSyntax => true,
        PropertyDeclarationSyntax or IndexerDeclarationSyntax => true,
        _ => false
    };

    /// <summary>
    /// Measures a single function-like node: any member accepted by <see cref="IsMeasuredMember"/>, a local
    /// function, or a lambda/anonymous method.
    /// </summary>
    public static FunctionComplexity Analyze(SyntaxNode function)
    {
        ArgumentNullException.ThrowIfNull(function);

        var (name, kind, parameters) = Describe(function);
        var cyclomatic = new CyclomaticComplexityWalker();
        var cognitive = new CognitiveComplexityWalker(parameters == null ? null : name, parameters);

        foreach (var body in GetBodies(function))
        {
            cyclomatic.Visit(body);
            cognitive.Visit(body);
        }

        var span = function.GetLocation().GetLineSpan();
        return new FunctionComplexity
        {
            Name = name,
            Kind = kind,
            StartLine = span.StartLinePosition.Line + 1,
            EndLine = span.EndLinePosition.Line + 1,
            CyclomaticComplexity = 1 + cyclomatic.DecisionPoints,
            CognitiveComplexity = cognitive.Total,
            MaxNestingDepth = cognitive.MaxNesting,
            DecisionPoints = cyclomatic.DecisionPoints,
            Increments = cognitive.Increments,
            Blocks = BuildBlocks(cognitive)
        };
    }

    /// <summary>
    /// The name <see cref="Analyze"/> reports for <paramref name="function"/>.
    /// </summary>
    public static string GetMemberName(SyntaxNode function) => Describe(function).Name;

    private static List<ComplexityBlock> BuildBlocks(CognitiveComplexityWalker walker)
    {
        var blocks = new List<ComplexityBlock>(walker.TopLevelStructures.Count);
        foreach (var structure in walker.TopLevelStructures)
        {
            var span = structure.Span;
            // A ?: inside an if-condition is also at nesting zero; report only the outermost structure.
            if (walker.TopLevelStructures.Any(other => other != structure && other.Span.Contains(span)))
                continue;

            var cost = walker.Increments.Where(i => span.Contains(i.Position)).Sum(i => i.Amount);
            var lines = structure.GetLocation().GetLineSpan();
            var construct = walker.Increments.First(i => span.Contains(i.Position)).Construct;
            blocks.Add(new ComplexityBlock(construct, lines.StartLinePosition.Line + 1, lines.EndLinePosition.Line + 1, cost));
        }

        return blocks;
    }

    private static SyntaxNode[] GetBodies(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax m => NonNull(m.Body, m.ExpressionBody),
        AccessorDeclarationSyntax a => NonNull(a.Body, a.ExpressionBody),
        PropertyDeclarationSyntax p => NonNull(p.ExpressionBody),
        IndexerDeclarationSyntax i => NonNull(i.ExpressionBody),
        LocalFunctionStatementSyntax l => NonNull(l.Body, l.ExpressionBody),
        AnonymousFunctionExpressionSyntax f => NonNull(f.Body),
        _ => []
    };

    private static SyntaxNode[] NonNull(params SyntaxNode?[] nodes) => nodes.Where(n => n != null).Cast<SyntaxNode>().ToArray();

    private static (string Name, string Kind, ParameterListSyntax? Parameters) Describe(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => (m.Identifier.Text, "Method", m.ParameterList),
        ConstructorDeclarationSyntax c => (c.Identifier.Text, "Constructor", c.ParameterList),
        DestructorDeclarationSyntax d => ("~" + d.Identifier.Text, "Destructor", d.ParameterList),
        OperatorDeclarationSyntax o => ("operator " + o.OperatorToken.Text, "Operator", o.ParameterList),
        ConversionOperatorDeclarationSyntax c => ("operator " + c.Type, "ConversionOperator", c.ParameterList),
        AccessorDeclarationSyntax a => (AccessorOwnerName(a) + "." + a.Keyword.Text, "Accessor", null),
        PropertyDeclarationSyntax p => (p.Identifier.Text + ".get", "Accessor", null),
        IndexerDeclarationSyntax => ("this[].get", "Accessor", null),
        LocalFunctionStatementSyntax l => (l.Identifier.Text, "Method", l.ParameterList),
        _ => ("lambda", "Lambda", null)
    };

    private static string AccessorOwnerName(AccessorDeclarationSyntax accessor) => accessor.Parent?.Parent switch
    {
        PropertyDeclarationSyntax p => p.Identifier.Text,
        EventDeclarationSyntax e => e.Identifier.Text,
        IndexerDeclarationSyntax => "this[]",
        _ => "accessor"
    };
}

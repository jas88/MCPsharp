using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Computes cognitive complexity following G. Ann Campbell's SonarSource white paper
/// "Cognitive Complexity: A new way of measuring understandability".
/// </summary>
/// <remarks>
/// <para><b>Structural increments</b> (+1 plus the current nesting level): <c>if</c> (not <c>else if</c>),
/// <c>switch</c> statements and switch expressions (once per switch, not per case), <c>for</c>,
/// <c>foreach</c>, <c>while</c>, <c>do</c>, <c>catch</c>, and the conditional operator <c>?:</c>.
/// Each of these also increases the nesting level for its body.</para>
/// <para><b>Hybrid increments</b> (+1, no nesting penalty, but nest their body): <c>else if</c> and <c>else</c>.</para>
/// <para><b>Fundamental increments</b> (+1 regardless of nesting): every <c>goto</c> (including <c>goto case</c>
/// and <c>goto default</c>; C# has no labelled <c>break</c>/<c>continue</c>), each sequence of like binary
/// logical operators (<c>&amp;&amp;</c>, <c>||</c>, and the pattern combinators <c>and</c>/<c>or</c>), and each
/// recursive call to the analysed method or an enclosing local function.</para>
/// <para><b>Nesting-only</b>: lambdas, anonymous methods and local functions add no increment but raise the
/// nesting level of their contents. <c>try</c>, <c>finally</c>, <c>using</c>, <c>lock</c>, <c>??</c>, <c>?.</c>,
/// early <c>return</c>, plain <c>break</c>/<c>continue</c> are free.</para>
/// <para>Conditions, loop headers and switch subjects are evaluated at the structure's own nesting level;
/// only bodies (and the branches of <c>?:</c>) are nested. A logical-operator sequence is broken by a change of
/// operator, so <c>a &amp;&amp; b &amp;&amp; c || d</c> scores 2. Recursion detection is syntactic: a call by
/// simple name (or <c>this.Name</c>) whose argument count is compatible with the enclosing function's
/// parameter list.</para>
/// </remarks>
internal sealed class CognitiveComplexityWalker : CSharpSyntaxWalker
{
    private readonly HashSet<SyntaxNode> _continuedLogicalSequences = new();
    private readonly List<FunctionSignature> _functions = new();
    private readonly List<ComplexityIncrement> _increments = new();
    private readonly List<SyntaxNode> _topLevelStructures = new();
    private int _nesting;

    public CognitiveComplexityWalker(string? functionName, ParameterListSyntax? parameters)
    {
        if (functionName != null)
            _functions.Add(FunctionSignature.Create(functionName, parameters));
    }

    public int Total { get; private set; }

    public int MaxNesting { get; private set; }

    public IReadOnlyList<ComplexityIncrement> Increments => _increments;

    /// <summary>Control structures that start at nesting level zero (used for block-level hotspots).</summary>
    public IReadOnlyList<SyntaxNode> TopLevelStructures => _topLevelStructures;

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        // Only reached for an "if" that is not the target of an "else"; else-if chains are walked by VisitIfChain.
        AddStructural(node, node.IfKeyword, "if");
        VisitIfChain(node);
    }

    private void VisitIfChain(IfStatementSyntax node)
    {
        Visit(node.Condition);
        VisitNested(node.Statement);

        var elseClause = node.Else;
        if (elseClause == null)
            return;

        if (elseClause.Statement is IfStatementSyntax elseIf)
        {
            AddFundamental(elseIf.IfKeyword, "else if");
            VisitIfChain(elseIf);
        }
        else
        {
            AddFundamental(elseClause.ElseKeyword, "else");
            VisitNested(elseClause.Statement);
        }
    }

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
    {
        AddStructural(node, node.SwitchKeyword, "switch");
        Visit(node.Expression);
        EnterNesting();
        foreach (var section in node.Sections)
            Visit(section);
        _nesting--;
    }

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        AddStructural(node, node.SwitchKeyword, "switch");
        Visit(node.GoverningExpression);
        EnterNesting();
        foreach (var arm in node.Arms)
            Visit(arm);
        _nesting--;
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        AddStructural(node, node.ForKeyword, "for");
        Visit(node.Declaration);
        foreach (var initializer in node.Initializers)
            Visit(initializer);
        Visit(node.Condition);
        foreach (var incrementor in node.Incrementors)
            Visit(incrementor);
        VisitNested(node.Statement);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        AddStructural(node, node.ForEachKeyword, "foreach");
        Visit(node.Expression);
        VisitNested(node.Statement);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        AddStructural(node, node.ForEachKeyword, "foreach");
        Visit(node.Variable);
        Visit(node.Expression);
        VisitNested(node.Statement);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        AddStructural(node, node.WhileKeyword, "while");
        Visit(node.Condition);
        VisitNested(node.Statement);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        AddStructural(node, node.DoKeyword, "do");
        VisitNested(node.Statement);
        Visit(node.Condition);
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        AddStructural(node, node.CatchKeyword, "catch");
        Visit(node.Declaration);
        Visit(node.Filter);
        VisitNested(node.Block);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        AddStructural(node, node.QuestionToken, "?:");
        Visit(node.Condition);
        VisitNested(node.WhenTrue);
        VisitNested(node.WhenFalse);
    }

    public override void VisitGotoStatement(GotoStatementSyntax node)
    {
        AddFundamental(node.GotoKeyword, "goto");
        base.VisitGotoStatement(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalAndExpression) || node.IsKind(SyntaxKind.LogicalOrExpression))
            CountLogicalSequence(node, node.OperatorToken, n => (n as BinaryExpressionSyntax)?.Left);
        base.VisitBinaryExpression(node);
    }

    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        CountLogicalSequence(node, node.OperatorToken, n => (n as BinaryPatternSyntax)?.Left);
        base.VisitBinaryPattern(node);
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        EnterNesting();
        base.VisitParenthesizedLambdaExpression(node);
        _nesting--;
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        EnterNesting();
        base.VisitSimpleLambdaExpression(node);
        _nesting--;
    }

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        EnterNesting();
        base.VisitAnonymousMethodExpression(node);
        _nesting--;
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        _functions.Add(FunctionSignature.Create(node.Identifier.Text, node.ParameterList));
        EnterNesting();
        base.VisitLocalFunctionStatement(node);
        _nesting--;
        _functions.RemoveAt(_functions.Count - 1);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var (name, token) = GetInvokedName(node.Expression);
        if (name != null && _functions.Exists(f => f.Matches(name, node.ArgumentList.Arguments.Count)))
            AddFundamental(token, "recursion");
        base.VisitInvocationExpression(node);
    }

    private static (string? Name, SyntaxToken Token) GetInvokedName(ExpressionSyntax expression) => expression switch
    {
        SimpleNameSyntax simple => (simple.Identifier.Text, simple.Identifier),
        MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } access =>
            (access.Name.Identifier.Text, access.Name.Identifier),
        _ => (null, default)
    };

    /// <summary>
    /// Adds one increment per run of identical logical operators. The left-leaning chain below the first
    /// operator of a run is marked so that it does not count again when visited.
    /// </summary>
    private void CountLogicalSequence(SyntaxNode node, SyntaxToken operatorToken, Func<SyntaxNode, SyntaxNode?> leftOf)
    {
        if (_continuedLogicalSequences.Remove(node))
            return;

        var left = SkipParentheses(leftOf(node));
        while (left != null && left.RawKind == node.RawKind)
        {
            _continuedLogicalSequences.Add(left);
            left = SkipParentheses(leftOf(left));
        }

        AddFundamental(operatorToken, operatorToken.Text);
    }

    private static SyntaxNode? SkipParentheses(SyntaxNode? node)
    {
        while (true)
        {
            switch (node)
            {
                case ParenthesizedExpressionSyntax expression:
                    node = expression.Expression;
                    break;
                case ParenthesizedPatternSyntax pattern:
                    node = pattern.Pattern;
                    break;
                default:
                    return node;
            }
        }
    }

    private void VisitNested(SyntaxNode? node)
    {
        if (node == null)
            return;
        EnterNesting();
        Visit(node);
        _nesting--;
    }

    private void EnterNesting()
    {
        _nesting++;
        MaxNesting = Math.Max(MaxNesting, _nesting);
    }

    private void AddStructural(SyntaxNode structure, SyntaxToken token, string construct)
    {
        if (_nesting == 0)
            _topLevelStructures.Add(structure);
        Add(token, 1 + _nesting, construct);
    }

    private void AddFundamental(SyntaxToken token, string construct) => Add(token, 1, construct);

    private void Add(SyntaxToken token, int amount, string construct)
    {
        Total += amount;
        var line = token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        _increments.Add(new ComplexityIncrement(line, token.SpanStart, amount, _nesting, construct));
    }

    private readonly record struct FunctionSignature(string Name, int RequiredParameters, int MaxParameters)
    {
        public static FunctionSignature Create(string name, ParameterListSyntax? parameters)
        {
            if (parameters == null)
                return new FunctionSignature(name, 0, int.MaxValue);

            var list = parameters.Parameters;
            var hasParamsArray = list.Any(p => p.Modifiers.Any(SyntaxKind.ParamsKeyword));
            var required = list.Count(p => p.Default == null && !p.Modifiers.Any(SyntaxKind.ParamsKeyword));
            return new FunctionSignature(name, required, hasParamsArray ? int.MaxValue : list.Count);
        }

        public bool Matches(string name, int argumentCount) =>
            string.Equals(Name, name, StringComparison.Ordinal)
            && argumentCount >= RequiredParameters
            && argumentCount <= MaxParameters;
    }
}

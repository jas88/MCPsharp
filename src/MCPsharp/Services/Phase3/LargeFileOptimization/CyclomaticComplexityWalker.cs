using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Counts McCabe decision points in a function body. Cyclomatic complexity is <c>1 + DecisionPoints</c>.
/// </summary>
/// <remarks>
/// Each of the following adds exactly one decision point:
/// <list type="bullet">
/// <item><c>if</c> (including each <c>else if</c>; a plain <c>else</c> adds nothing)</item>
/// <item>each <c>case</c> label of a switch statement (constant or pattern; <c>default:</c> adds nothing)</item>
/// <item>each switch-expression arm, except a discard (<c>_</c>) arm</item>
/// <item><c>for</c>, <c>foreach</c>, <c>while</c>, <c>do</c></item>
/// <item>each <c>catch</c> clause</item>
/// <item>the conditional operator <c>?:</c></item>
/// <item>the short-circuit operators <c>&amp;&amp;</c> and <c>||</c></item>
/// <item>the null-coalescing operators <c>??</c> and <c>??=</c></item>
/// <item>the pattern combinators <c>and</c> and <c>or</c></item>
/// </list>
/// Not counted: null-conditional access (<c>?.</c>, <c>?[]</c>), <c>goto</c> in any form (a jump, not a decision),
/// <c>when</c> filters/guards, <c>try</c>/<c>finally</c>, <c>return</c>/<c>throw</c>.
/// Nested lambdas, anonymous methods and local functions are counted as part of the enclosing function.
/// </remarks>
internal sealed class CyclomaticComplexityWalker : CSharpSyntaxWalker
{
    public int DecisionPoints { get; private set; }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitIfStatement(node);
    }

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        DecisionPoints++;
        base.VisitCaseSwitchLabel(node);
    }

    public override void VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
    {
        DecisionPoints++;
        base.VisitCasePatternSwitchLabel(node);
    }

    public override void VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        if (node.Pattern is not DiscardPatternSyntax)
            DecisionPoints++;
        base.VisitSwitchExpressionArm(node);
    }

    public override void VisitForStatement(ForStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitForStatement(node);
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitForEachStatement(node);
    }

    public override void VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitForEachVariableStatement(node);
    }

    public override void VisitWhileStatement(WhileStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitWhileStatement(node);
    }

    public override void VisitDoStatement(DoStatementSyntax node)
    {
        DecisionPoints++;
        base.VisitDoStatement(node);
    }

    public override void VisitCatchClause(CatchClauseSyntax node)
    {
        DecisionPoints++;
        base.VisitCatchClause(node);
    }

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
    {
        DecisionPoints++;
        base.VisitConditionalExpression(node);
    }

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.LogicalAndExpression)
            || node.IsKind(SyntaxKind.LogicalOrExpression)
            || node.IsKind(SyntaxKind.CoalesceExpression))
        {
            DecisionPoints++;
        }

        base.VisitBinaryExpression(node);
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            DecisionPoints++;
        base.VisitAssignmentExpression(node);
    }

    public override void VisitBinaryPattern(BinaryPatternSyntax node)
    {
        DecisionPoints++;
        base.VisitBinaryPattern(node);
    }
}

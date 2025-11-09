using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3.DuplicateDetection;

/// <summary>
/// Service for calculating code complexity metrics
/// </summary>
public class ComplexityCalculator
{
    public ComplexityMetrics CalculateComplexity(SyntaxNode node)
    {
        // Calculate cyclomatic complexity
        var cyclomaticComplexity = 1; // Base complexity

        foreach (var child in node.DescendantNodes())
        {
            if (child is IfStatementSyntax or
                WhileStatementSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                SwitchStatementSyntax or
                CatchClauseSyntax)
            {
                cyclomaticComplexity++;
            }

            if (child is ConditionalExpressionSyntax)
            {
                cyclomaticComplexity++;
            }
        }

        // Calculate cognitive complexity
        var cognitiveComplexity = CalculateCognitiveComplexity(node);

        // Count lines of code
        var linesOfCode = node.GetText().Lines.Count;
        var logicalLinesOfCode = node.DescendantNodes().Count(n => n is StatementSyntax);

        // Extract parameter count if applicable
        var parameterCount = 0;
        if (node is MethodDeclarationSyntax method)
        {
            parameterCount = method.ParameterList?.Parameters.Count ?? 0;
        }

        // Calculate nesting depth
        var nestingDepth = CalculateNestingDepth(node);

        var overallScore = (cyclomaticComplexity * 0.4) +
                          (cognitiveComplexity * 0.3) +
                          (nestingDepth * 0.2) +
                          (parameterCount * 0.1);

        return new ComplexityMetrics
        {
            CyclomaticComplexity = cyclomaticComplexity,
            CognitiveComplexity = cognitiveComplexity,
            LinesOfCode = linesOfCode,
            LogicalLinesOfCode = logicalLinesOfCode,
            ParameterCount = parameterCount,
            NestingDepth = nestingDepth,
            OverallScore = overallScore
        };
    }

    public int CalculateCognitiveComplexity(SyntaxNode node)
    {
        var complexity = 0;

        void VisitNode(SyntaxNode n, int currentNesting)
        {
            switch (n)
            {
                case IfStatementSyntax ifStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(ifStmt.Condition, currentNesting);
                    if (ifStmt.Statement != null)
                        VisitNode(ifStmt.Statement, currentNesting + 1);
                    if (ifStmt.Else?.Statement != null)
                        VisitNode(ifStmt.Else.Statement, currentNesting);
                    break;

                case WhileStatementSyntax whileStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(whileStmt.Condition, currentNesting);
                    VisitNode(whileStmt.Statement, currentNesting + 1);
                    break;

                case ForStatementSyntax forStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(forStmt.Statement, currentNesting + 1);
                    break;

                case ForEachStatementSyntax forEachStmt:
                    complexity += 1 + currentNesting;
                    VisitNode(forEachStmt.Statement, currentNesting + 1);
                    break;

                case SwitchStatementSyntax switchStmt:
                    complexity += 1 + currentNesting;
                    foreach (var section in switchStmt.Sections)
                    {
                        VisitNode(section, currentNesting + 1);
                    }
                    break;

                case CatchClauseSyntax catchClause:
                    complexity += 1;
                    VisitNode(catchClause.Block, currentNesting);
                    break;

                default:
                    foreach (var child in n.ChildNodes())
                    {
                        VisitNode(child, currentNesting);
                    }
                    break;
            }
        }

        VisitNode(node, 0);
        return complexity;
    }

    public int CalculateNestingDepth(SyntaxNode node)
    {
        var maxDepth = 0;

        void VisitNode(SyntaxNode n, int currentDepth)
        {
            maxDepth = Math.Max(maxDepth, currentDepth);

            switch (n)
            {
                case IfStatementSyntax ifStmt:
                    if (ifStmt.Statement != null)
                        VisitNode(ifStmt.Statement, currentDepth + 1);
                    if (ifStmt.Else?.Statement != null)
                        VisitNode(ifStmt.Else.Statement, currentDepth + 1);
                    break;

                case WhileStatementSyntax whileStmt:
                    VisitNode(whileStmt.Statement, currentDepth + 1);
                    break;

                case ForStatementSyntax forStmt:
                    VisitNode(forStmt.Statement, currentDepth + 1);
                    break;

                case ForEachStatementSyntax forEachStmt:
                    VisitNode(forEachStmt.Statement, currentDepth + 1);
                    break;

                case SwitchStatementSyntax switchStmt:
                    foreach (var section in switchStmt.Sections)
                    {
                        VisitNode(section, currentDepth + 1);
                    }
                    break;

                case TryStatementSyntax tryStmt:
                    VisitNode(tryStmt.Block, currentDepth + 1);
                    foreach (var catchClause in tryStmt.Catches)
                    {
                        VisitNode(catchClause, currentDepth + 1);
                    }
                    if (tryStmt.Finally != null)
                        VisitNode(tryStmt.Finally, currentDepth + 1);
                    break;

                default:
                    foreach (var child in n.ChildNodes())
                    {
                        VisitNode(child, currentDepth);
                    }
                    break;
            }
        }

        VisitNode(node, 0);
        return maxDepth;
    }
}

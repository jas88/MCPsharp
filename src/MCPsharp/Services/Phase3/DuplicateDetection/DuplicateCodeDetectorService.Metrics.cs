using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Complexity, token and AST structure measurements for code blocks
/// </summary>
public partial class DuplicateCodeDetectorService
{
    private static ComplexityMetrics CalculateComplexity(SyntaxNode node)
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

        // Calculate cognitive complexity (simplified)
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

    private static int CalculateCognitiveComplexity(SyntaxNode node)
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

    private static int CalculateNestingDepth(SyntaxNode node)
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

    private static List<CodeToken> ExtractTokens(SyntaxNode node)
    {
        var tokens = new List<CodeToken>();

        foreach (var token in node.DescendantTokens())
        {
            if (token.SyntaxTree == null)
                continue;
            var lineSpan = token.SyntaxTree.GetLineSpan(token.Span);

            tokens.Add(new CodeToken
            {
                Type = GetTokenType(token),
                Text = token.Text,
                StartPosition = token.SpanStart,
                Length = token.Span.Length,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1
            });
        }

        return tokens;
    }

    private static TokenType GetTokenType(SyntaxToken token)
    {
        if (token.IsKeyword())
            return TokenType.Keyword;

        if (token.ValueText != null && (token.Value is string or char))
            return TokenType.StringLiteral;

        if (token.Value != null && IsNumericType(token.Value.GetType()))
            return TokenType.NumericLiteral;

        if (token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken))
            return TokenType.Identifier;

        // Add more sophisticated token type detection as needed
        return TokenType.Unknown;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(sbyte) || type == typeof(byte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    private static AstStructure ExtractAstStructure(SyntaxNode node)
    {
        var nodeTypes = new List<string>();
        var controlFlowPatterns = new List<ControlFlowPattern>();
        var dataFlowPatterns = new List<DataFlowPattern>();

        void CollectNodeTypes(SyntaxNode n)
        {
            nodeTypes.Add(n.GetType().Name);

            switch (n)
            {
                case IfStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Conditional);
                    break;
                case WhileStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case DoStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Loop);
                    break;
                case SwitchStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Switch);
                    break;
                case TryStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.TryCatch);
                    break;
                case ReturnStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.Return);
                    break;
                case BreakStatementSyntax:
                case ContinueStatementSyntax:
                    controlFlowPatterns.Add(ControlFlowPattern.BreakContinue);
                    break;
            }

            foreach (var child in n.ChildNodes())
            {
                CollectNodeTypes(child);
            }
        }

        CollectNodeTypes(node);

        // Calculate structural hash
        var structuralString = string.Join("|", nodeTypes);
        var structuralHash = ComputeHash(structuralString);

        // Calculate depth and complexity
        var depth = CalculateMaxDepth(node);
        var complexity = Math.Sqrt(nodeTypes.Count) * (1 + depth * 0.1);

        return new AstStructure
        {
            StructuralHash = structuralHash,
            NodeTypes = nodeTypes,
            Depth = depth,
            NodeCount = nodeTypes.Count,
            StructuralComplexity = complexity,
            ControlFlowPatterns = controlFlowPatterns,
            DataFlowPatterns = dataFlowPatterns
        };
    }

    private static int CalculateMaxDepth(SyntaxNode node)
    {
        var maxDepth = 0;

        void VisitNode(SyntaxNode n, int currentDepth)
        {
            maxDepth = Math.Max(maxDepth, currentDepth);
            foreach (var child in n.ChildNodes())
            {
                VisitNode(child, currentDepth + 1);
            }
        }

        VisitNode(node, 0);
        return maxDepth;
    }
}

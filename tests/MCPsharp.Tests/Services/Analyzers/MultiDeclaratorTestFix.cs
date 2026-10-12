using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// Test analyzer: reports every declarator of a local declaration that declares more than one variable.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MultiDeclaratorTestAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "TST100";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Declare one variable per statement",
        "Variable '{0}' is declared together with other variables",
        "Style",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.LocalDeclarationStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var statement = (LocalDeclarationStatementSyntax)context.Node;
        if (statement.Declaration.Variables.Count < 2)
        {
            return;
        }

        foreach (var variable in statement.Declaration.Variables)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), variable.Identifier.Text));
        }
    }
}

/// <summary>
/// Test fixer: splits the whole declaration into one statement per variable, which also resolves the
/// diagnostics of the other declarators in the statement.
/// </summary>
public sealed class MultiDeclaratorTestFixer : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(MultiDeclaratorTestAnalyzer.DiagnosticId);

    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var statement = root?.FindToken(context.Span.Start).Parent?.AncestorsAndSelf()
            .OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
        if (root == null || statement == null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Split declaration",
                _ => Task.FromResult(context.Document.WithSyntaxRoot(root.ReplaceNode(statement, Split(statement)))),
                nameof(MultiDeclaratorTestFixer)),
            context.Diagnostics);
    }

    private static IEnumerable<SyntaxNode> Split(LocalDeclarationStatementSyntax statement) =>
        statement.Declaration.Variables.Select(variable =>
            SyntaxFactory.LocalDeclarationStatement(
                    SyntaxFactory.VariableDeclaration(
                        statement.Declaration.Type,
                        SyntaxFactory.SingletonSeparatedList(variable.WithoutTrivia())))
                .WithLeadingTrivia(statement.GetLeadingTrivia())
                .WithTrailingTrivia(statement.GetTrailingTrivia()));
}

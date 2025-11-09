using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Detects async methods that don't use await (CS1998 pattern)
/// Safe to remove async keyword and wrap returns with Task.FromResult
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AsyncAwaitPatternAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MCP001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Async method lacks await",
        "Method '{0}' is marked async but contains no await expressions",
        "Async",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Remove async modifier and wrap return value with Task.FromResult or Task.CompletedTask.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;

        // Check if method is async
        if (!methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Check if method contains await
        var awaitExpressions = methodDecl.DescendantNodes()
            .OfType<AwaitExpressionSyntax>();

        if (awaitExpressions.Any())
            return;

        // Check if return type is Task or Task<T>
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol == null)
            return;

        var returnType = methodSymbol.ReturnType;
        if (!IsTaskType(returnType))
            return;

        // Skip if method has cancellation token that might be used async
        // (defensive check - might have async operations we can't detect)
        if (HasCancellationTokenParameter(methodDecl))
        {
            // Only skip if the method body is complex (might use token in ways we can't analyze)
            if (IsComplexMethodBody(methodDecl))
                return;
        }

        // Skip if method uses ConfigureAwait (indicates async intent)
        if (methodDecl.DescendantNodes().Any(n =>
            n is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.Text == "ConfigureAwait"))
        {
            return;
        }

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDecl.Identifier.GetLocation(),
            methodDecl.Identifier.Text);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsTaskType(ITypeSymbol type)
    {
        if (type.Name == "Task" &&
            type.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
        {
            return true;
        }

        return false;
    }

    private static bool HasCancellationTokenParameter(MethodDeclarationSyntax methodDecl)
    {
        return methodDecl.ParameterList.Parameters.Any(p =>
        {
            var typeName = p.Type?.ToString() ?? string.Empty;
            return typeName.Contains("CancellationToken");
        });
    }

    private static bool IsComplexMethodBody(MethodDeclarationSyntax methodDecl)
    {
        if (methodDecl.Body == null)
            return false;

        // Consider complex if:
        // - More than 5 statements
        // - Contains loops
        // - Contains try-catch
        // - Contains nested methods/lambdas

        var statementCount = methodDecl.Body.Statements.Count;
        if (statementCount > 5)
            return true;

        var hasLoops = methodDecl.DescendantNodes().Any(n =>
            n is ForStatementSyntax ||
            n is WhileStatementSyntax ||
            n is ForEachStatementSyntax);

        if (hasLoops)
            return true;

        var hasTryCatch = methodDecl.DescendantNodes().Any(n => n is TryStatementSyntax);
        if (hasTryCatch)
            return true;

        return false;
    }
}

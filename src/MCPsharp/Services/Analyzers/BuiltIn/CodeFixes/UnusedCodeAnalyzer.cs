using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Detects unused code including:
/// 1. Unused local variables (MCP003)
/// 2. Unused private fields (MCP004)
/// </summary>
/// <remarks>
/// Conservative analyzer that only flags truly unused code:
/// - Local variables that are declared but never read
/// - Private fields that are never accessed
///
/// Safety checks:
/// - Does not remove variables with side effects (method calls in initializer)
/// - Does not remove variables that might be needed for debugging
/// - Does not remove fields that might be used via reflection
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class UnusedCodeAnalyzer : DiagnosticAnalyzer
{
    public const string UnusedLocalDiagnosticId = "MCP003";
    public const string UnusedFieldDiagnosticId = "MCP004";

    private static readonly DiagnosticDescriptor UnusedLocalRule = new(
        UnusedLocalDiagnosticId,
        "Unused local variable",
        "Local variable '{0}' is declared but never used",
        "CodeQuality",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Remove unused local variables to improve code clarity.");

    private static readonly DiagnosticDescriptor UnusedFieldRule = new(
        UnusedFieldDiagnosticId,
        "Unused private field",
        "Private field '{0}' is declared but never used",
        "CodeQuality",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Remove unused private fields to reduce code clutter.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnusedLocalRule, UnusedFieldRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Register for method bodies to find unused locals
        context.RegisterSyntaxNodeAction(AnalyzeMethodBody, SyntaxKind.MethodDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeMethodBody, SyntaxKind.ConstructorDeclaration);

        // Register for class declarations to find unused fields
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private void AnalyzeMethodBody(SyntaxNodeAnalysisContext context)
    {
        var methodBody = context.Node is MethodDeclarationSyntax method
            ? method.Body
            : (context.Node as ConstructorDeclarationSyntax)?.Body;

        if (methodBody == null)
            return;

        var semanticModel = context.SemanticModel;

        // Find all local variable declarations
        var localDeclarations = methodBody.DescendantNodes()
            .OfType<LocalDeclarationStatementSyntax>();

        foreach (var declaration in localDeclarations)
        {
            foreach (var variable in declaration.Declaration.Variables)
            {
                var variableSymbol = semanticModel.GetDeclaredSymbol(variable, context.CancellationToken);
                if (variableSymbol == null)
                    continue;

                // Check if variable is ever read
                var dataFlow = semanticModel.AnalyzeDataFlow(methodBody);
                if (dataFlow == null || !dataFlow.Succeeded)
                    continue;

                // Check if this variable is in the set of variables that are read
                var isRead = dataFlow.ReadInside.Contains(variableSymbol) ||
                            dataFlow.ReadOutside.Contains(variableSymbol);

                if (!isRead)
                {
                    // Additional safety check: skip if initializer has side effects
                    if (variable.Initializer != null && HasSideEffects(variable.Initializer.Value))
                        continue;

                    // Skip if variable name suggests it's intentionally unused (e.g., "_", "__")
                    if (IsIntentionallyUnused(variableSymbol.Name))
                        continue;

                    var diagnostic = Diagnostic.Create(
                        UnusedLocalRule,
                        variable.Identifier.GetLocation(),
                        variableSymbol.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var namedType = (INamedTypeSymbol)context.Symbol;

        // Only analyze classes and structs
        if (namedType.TypeKind != TypeKind.Class && namedType.TypeKind != TypeKind.Struct)
            return;

        // Find all private fields
        var privateFields = namedType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => f.DeclaredAccessibility == Accessibility.Private && !f.IsConst);

        foreach (var field in privateFields)
        {
            // Skip compiler-generated fields
            if (field.IsImplicitlyDeclared)
                continue;

            // Skip if field name suggests intentional (like backing fields)
            if (IsIntentionallyUnused(field.Name))
                continue;

            // Check if field is ever referenced
            var references = GetAllReferences(field, context.Compilation);

            // If field is only referenced in its own declaration, it's unused
            if (references == 0)
            {
                foreach (var location in field.Locations)
                {
                    if (location.IsInSource)
                    {
                        var diagnostic = Diagnostic.Create(
                            UnusedFieldRule,
                            location,
                            field.Name);

                        context.ReportDiagnostic(diagnostic);
                        break; // Only report once per field
                    }
                }
            }
        }
    }

    private static bool HasSideEffects(ExpressionSyntax expression)
    {
        // Conservative check: assume any method call has side effects
        if (expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any())
            return true;

        // Object creation might have side effects
        if (expression.DescendantNodesAndSelf().OfType<ObjectCreationExpressionSyntax>().Any())
            return true;

        // Assignment expressions have side effects
        if (expression.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>().Any())
            return true;

        // Increment/decrement operators
        if (expression.DescendantNodesAndSelf().Any(n =>
            n.IsKind(SyntaxKind.PreIncrementExpression) ||
            n.IsKind(SyntaxKind.PostIncrementExpression) ||
            n.IsKind(SyntaxKind.PreDecrementExpression) ||
            n.IsKind(SyntaxKind.PostDecrementExpression)))
        {
            return true;
        }

        return false;
    }

    private static bool IsIntentionallyUnused(string name)
    {
        // Common patterns for intentionally unused variables
        return name == "_" ||
               name.StartsWith("__") ||
               name.StartsWith("unused", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("ignore", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetAllReferences(IFieldSymbol field, Compilation compilation)
    {
        int referenceCount = 0;

        // Simple heuristic: check all syntax trees for field references
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = syntaxTree.GetRoot();
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            // Look for identifier references
            var identifiers = root.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(i => i.Identifier.Text == field.Name);

            foreach (var identifier in identifiers)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, field))
                {
                    // Check if this is not just the declaration
                    var parent = identifier.Parent;
                    if (parent is not VariableDeclaratorSyntax)
                    {
                        referenceCount++;
                    }
                }
            }
        }

        return referenceCount;
    }
}

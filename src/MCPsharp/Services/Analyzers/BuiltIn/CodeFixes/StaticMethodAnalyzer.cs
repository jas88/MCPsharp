using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Detects private methods that don't use 'this' and could be made static
/// </summary>
/// <remarks>
/// Benefits of static methods:
/// - Clearer intent: method doesn't depend on instance state
/// - Potential performance improvement: no 'this' pointer dereferencing
/// - Better testability: can be tested without instance creation
///
/// Safety checks:
/// - Only flags private methods (public API should remain unchanged)
/// - Skips methods that override base methods
/// - Skips methods that implement interface members
/// - Skips methods used as delegates if instance context matters
/// - Skips methods with attributes that might require instance context
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class StaticMethodAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MCP005";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Private method can be made static",
        "Method '{0}' does not access instance members and can be made static",
        "Performance",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Making methods static when they don't access instance state improves code clarity and may improve performance.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var methodSymbol = (IMethodSymbol)context.Symbol;

        // Only analyze private methods
        if (methodSymbol.DeclaredAccessibility != Accessibility.Private)
            return;

        // Skip if already static
        if (methodSymbol.IsStatic)
            return;

        // Skip special methods (constructors, finalizers, operators, property accessors)
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return;

        // Skip if method overrides base method
        if (methodSymbol.IsOverride)
            return;

        // Skip if method implements interface
        if (methodSymbol.ExplicitInterfaceImplementations.Any() ||
            ImplementsInterfaceMember(methodSymbol))
            return;

        // Skip if method is virtual (might be overridden)
        if (methodSymbol.IsVirtual || methodSymbol.IsAbstract)
            return;

        // Skip if method has special attributes that might require instance context
        if (HasSpecialAttributes(methodSymbol))
            return;

        // Get the method syntax
        var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken);
        if (syntax is not MethodDeclarationSyntax methodDeclaration)
            return;

        // Check if method accesses any instance members
        if (AccessesInstanceMembers(methodDeclaration, methodSymbol, context.Compilation))
            return;

        // Report diagnostic
        var diagnostic = Diagnostic.Create(
            Rule,
            methodDeclaration.Identifier.GetLocation(),
            methodSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol methodSymbol)
    {
        // Check if this method implements any interface member implicitly
        var containingType = methodSymbol.ContainingType;
        var interfaces = containingType.AllInterfaces;

        foreach (var iface in interfaces)
        {
            var interfaceMethod = iface.GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => containingType.FindImplementationForInterfaceMember(m)?.Equals(methodSymbol, SymbolEqualityComparer.Default) == true);

            if (interfaceMethod != null)
                return true;
        }

        return false;
    }

    private static bool HasSpecialAttributes(IMethodSymbol methodSymbol)
    {
        // Check for attributes that might require instance context
        var attributeNames = methodSymbol.GetAttributes()
            .Select(a => a.AttributeClass?.Name)
            .Where(n => n != null)
            .ToHashSet();

        // Common attributes that might require instance context
        var specialAttributes = new[]
        {
            "TestMethodAttribute",
            "FactAttribute",
            "TheoryAttribute",
            "TestAttribute",
            "SetUpAttribute",
            "TearDownAttribute",
            "BeforeAttribute",
            "AfterAttribute"
        };

        return specialAttributes.Any(sa => attributeNames.Contains(sa));
    }

    private static bool AccessesInstanceMembers(
        MethodDeclarationSyntax methodDeclaration,
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        var body = methodDeclaration.Body;
        var expressionBody = methodDeclaration.ExpressionBody;

        if (body == null && expressionBody == null)
            return false;

        var semanticModel = compilation.GetSemanticModel(methodDeclaration.SyntaxTree);

        // Check all identifiers and member accesses
        var descendants = body?.DescendantNodes() ?? expressionBody!.DescendantNodes();

        foreach (var node in descendants)
        {
            // Check for 'this' keyword
            if (node.IsKind(SyntaxKind.ThisExpression))
                return true;

            // Check for 'base' keyword
            if (node.IsKind(SyntaxKind.BaseExpression))
                return true;

            // Check for member access without explicit qualifier
            if (node is IdentifierNameSyntax identifier)
            {
                // Skip if this is part of a qualified name
                if (identifier.Parent is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name == identifier)
                    continue;

                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                var symbol = symbolInfo.Symbol;

                if (symbol == null)
                    continue;

                // Check if it's an instance member of the containing type
                if (symbol is IFieldSymbol field &&
                    !field.IsStatic &&
                    SymbolEqualityComparer.Default.Equals(field.ContainingType, methodSymbol.ContainingType))
                {
                    return true;
                }

                if (symbol is IPropertySymbol property &&
                    !property.IsStatic &&
                    SymbolEqualityComparer.Default.Equals(property.ContainingType, methodSymbol.ContainingType))
                {
                    return true;
                }

                if (symbol is IMethodSymbol method &&
                    !method.IsStatic &&
                    method.MethodKind == MethodKind.Ordinary &&
                    SymbolEqualityComparer.Default.Equals(method.ContainingType, methodSymbol.ContainingType))
                {
                    return true;
                }

                if (symbol is IEventSymbol evt &&
                    !evt.IsStatic &&
                    SymbolEqualityComparer.Default.Equals(evt.ContainingType, methodSymbol.ContainingType))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

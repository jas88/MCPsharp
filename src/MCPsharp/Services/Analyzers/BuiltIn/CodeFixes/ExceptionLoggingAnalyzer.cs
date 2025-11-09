using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes;

/// <summary>
/// Detects improper exception logging patterns where exception details are logged as strings
/// instead of using the proper logging API with exception parameter
/// </summary>
/// <remarks>
/// Detects patterns like:
/// - _logger.LogError($"Error: {ex.Message}")
/// - _logger.LogError("Error: " + ex.Message)
/// - _logger.LogWarning($"Warning: {ex.Message}")
///
/// And suggests proper patterns:
/// - _logger.LogError(ex, "Error")
/// - _logger.LogWarning(ex, "Warning")
///
/// This ensures:
/// 1. Full exception details (stack trace, inner exceptions) are captured
/// 2. Structured logging formats exceptions correctly
/// 3. Log analysis tools can properly parse exception data
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ExceptionLoggingAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MCP002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        "Exception logged as string instead of proper exception parameter",
        "Logger call includes exception message as string - should pass exception as parameter",
        "Logging",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Pass exception as parameter to logging methods to capture full exception details including stack trace.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Check if this is a logger method call (LogError, LogWarning, LogInformation, etc.)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (!IsLoggerMethod(methodName))
            return;

        // Get the symbol to verify it's actually a logger
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        // Check if the containing type looks like a logger (ILogger, ILogger<T>, etc.)
        if (!IsLoggerType(methodSymbol.ContainingType))
            return;

        // Check if arguments contain exception message references
        if (!invocation.ArgumentList.Arguments.Any())
            return;

        // Analyze the first argument (the message)
        var firstArg = invocation.ArgumentList.Arguments[0];

        // Check if this is already using proper exception logging (has exception as first parameter)
        if (HasExceptionAsFirstParameter(methodSymbol))
            return;

        // Look for exception message references in the argument
        var exceptionInfo = FindExceptionMessageReference(firstArg.Expression, context.SemanticModel);

        if (exceptionInfo != null)
        {
            // Found improper exception logging
            var diagnostic = Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                additionalLocations: new[] { exceptionInfo.Location },
                properties: ImmutableDictionary<string, string?>.Empty
                    .Add("ExceptionVariable", exceptionInfo.VariableName)
                    .Add("MethodName", methodName));

            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsLoggerMethod(string methodName)
    {
        return methodName is "LogTrace" or "LogDebug" or "LogInformation" or "LogWarning"
            or "LogError" or "LogCritical" or "Log";
    }

    private static bool IsLoggerType(ITypeSymbol type)
    {
        // Check for ILogger or ILogger<T>
        var typeName = type.Name;
        if (typeName.StartsWith("ILogger"))
        {
            var ns = type.ContainingNamespace?.ToDisplayString();
            return ns == "Microsoft.Extensions.Logging";
        }

        return false;
    }

    private static bool HasExceptionAsFirstParameter(IMethodSymbol methodSymbol)
    {
        // Check if the first parameter is an Exception type
        if (!methodSymbol.Parameters.Any())
            return false;

        var firstParam = methodSymbol.Parameters[0];
        return IsExceptionType(firstParam.Type);
    }

    private static bool IsExceptionType(ITypeSymbol type)
    {
        // Walk up the inheritance chain to see if this derives from Exception
        var current = type;
        while (current != null)
        {
            if (current.Name == "Exception" &&
                current.ContainingNamespace?.ToDisplayString() == "System")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static ExceptionReferenceInfo? FindExceptionMessageReference(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        // Check for interpolated string with {ex.Message}
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolationSyntax interpolation)
                {
                    var info = CheckForExceptionMessage(interpolation.Expression, semanticModel);
                    if (info != null)
                        return info;
                }
            }
        }

        // Check for string concatenation with ex.Message
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression))
        {
            var leftInfo = CheckForExceptionMessage(binary.Left, semanticModel);
            if (leftInfo != null)
                return leftInfo;

            var rightInfo = CheckForExceptionMessage(binary.Right, semanticModel);
            if (rightInfo != null)
                return rightInfo;
        }

        // Check direct reference (less common but possible)
        return CheckForExceptionMessage(expression, semanticModel);
    }

    private static ExceptionReferenceInfo? CheckForExceptionMessage(
        ExpressionSyntax expression,
        SemanticModel semanticModel)
    {
        // Look for patterns like: ex.Message, exception.Message, e.Message
        if (expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name.Identifier.Text == "Message")
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression);
            if (symbolInfo.Symbol is ILocalSymbol localSymbol)
            {
                // Check if the variable is an exception type
                if (IsExceptionType(localSymbol.Type))
                {
                    return new ExceptionReferenceInfo(
                        localSymbol.Name,
                        memberAccess.GetLocation());
                }
            }
            else if (symbolInfo.Symbol is IParameterSymbol paramSymbol)
            {
                // Check if the parameter is an exception type
                if (IsExceptionType(paramSymbol.Type))
                {
                    return new ExceptionReferenceInfo(
                        paramSymbol.Name,
                        memberAccess.GetLocation());
                }
            }
        }

        // Also check for ex.ToString() or exception.ToString()
        if (expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is MemberAccessExpressionSyntax toStringAccess &&
            toStringAccess.Name.Identifier.Text == "ToString")
        {
            var symbolInfo = semanticModel.GetSymbolInfo(toStringAccess.Expression);
            if (symbolInfo.Symbol is ILocalSymbol localSymbol && IsExceptionType(localSymbol.Type))
            {
                return new ExceptionReferenceInfo(
                    localSymbol.Name,
                    invocation.GetLocation());
            }
            else if (symbolInfo.Symbol is IParameterSymbol paramSymbol && IsExceptionType(paramSymbol.Type))
            {
                return new ExceptionReferenceInfo(
                    paramSymbol.Name,
                    invocation.GetLocation());
            }
        }

        return null;
    }

    private record ExceptionReferenceInfo(string VariableName, Location Location);
}

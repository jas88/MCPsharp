using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Refactoring;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Data flow, control flow and type analysis of the selection.
/// </summary>
public partial class ExtractMethodService
{
    private static readonly SymbolEqualityComparer SymbolComparer = SymbolEqualityComparer.Default;

    /// <summary>
    /// What the extracted method needs: parameters, returned variables, return type and modifiers.
    /// </summary>
    private sealed class ExtractionAnalysis
    {
        public required List<ISymbol> Inputs { get; init; }
        public required HashSet<ISymbol> RefInputs { get; init; }
        public required List<ISymbol> Outputs { get; init; }
        public required HashSet<ISymbol> OutputsDeclaredInside { get; init; }
        public required List<ISymbol> LocalsToDeclare { get; init; }

        /// <summary>Return type of the extracted method as declared (null = void).</summary>
        public required ITypeSymbol? ReturnType { get; init; }

        /// <summary>The selection ends with (and always leaves through) return statements of the enclosing method.</summary>
        public required bool EndsWithReturn { get; init; }

        /// <summary>In <see cref="EndsWithReturn"/> mode, whether those return statements carry a value.</summary>
        public required bool ReturnsValue { get; init; }

        public required bool IsStatic { get; init; }
        public required List<ITypeParameterSymbol> TypeParameters { get; init; }
        public required MethodCharacteristics Characteristics { get; init; }
    }

    private sealed record VariableFlow(
        List<ISymbol> Inputs,
        HashSet<ISymbol> RefInputs,
        List<ISymbol> Outputs,
        HashSet<ISymbol> OutputsDeclaredInside,
        List<ISymbol> LocalsToDeclare,
        bool WritesOuterVariables);

    private static (ExtractionAnalysis? Analysis, ExtractMethodResult? Error) Analyze(
        ExtractionSelection selection,
        SemanticModel model,
        ExtractMethodRequest request)
    {
        var dataFlow = selection.Expression != null
            ? model.AnalyzeDataFlow(selection.Expression)
            : model.AnalyzeDataFlow(selection.Statements[0], selection.Statements[^1]);

        if (dataFlow is not { Succeeded: true })
        {
            return Fail("Unable to analyze data flow of the selection", "DATA_FLOW_ERROR");
        }

        var (endsWithReturn, controlFlowError) = AnalyzeControlFlow(selection, model);
        if (controlFlowError != null)
        {
            return (null, controlFlowError);
        }

        if (FindInSelection<YieldStatementSyntax>(selection).Any())
        {
            return Fail("Cannot extract code containing yield statements", "CONTAINS_YIELD");
        }

        var flow = ClassifyVariables(dataFlow);
        var isAsync = ContainsAwait(selection);
        var flowError = ValidateFlow(selection, flow, isAsync, endsWithReturn);
        if (flowError != null)
        {
            return (null, flowError);
        }

        var (returnType, returnsValue, typeError) = DetermineReturnType(selection, model, flow, isAsync, endsWithReturn);
        if (typeError != null)
        {
            return (null, typeError);
        }

        var signatureTypes = flow.Inputs.Concat(flow.Outputs).Concat(flow.LocalsToDeclare)
            .Select(TypeOf)
            .Append(returnType)
            .ToList();
        if (signatureTypes.Exists(ContainsAnonymousType))
        {
            return Fail("Cannot extract code whose inputs or results have anonymous types", "ANONYMOUS_TYPE");
        }

        var usesInstanceMembers = UsesInstanceMembers(selection, model, dataFlow);
        var isStatic = request.MakeStatic ?? (IsStaticContext(selection) || !usesInstanceMembers);
        var typeParameters = CollectMethodTypeParameters(selection, model, signatureTypes);
        var returns = FindInSelection<ReturnStatementSyntax>(selection).ToList();

        return (new ExtractionAnalysis
        {
            Inputs = flow.Inputs,
            RefInputs = flow.RefInputs,
            Outputs = flow.Outputs,
            OutputsDeclaredInside = flow.OutputsDeclaredInside,
            LocalsToDeclare = flow.LocalsToDeclare,
            ReturnType = returnType,
            EndsWithReturn = endsWithReturn,
            ReturnsValue = returnsValue,
            IsStatic = isStatic,
            TypeParameters = typeParameters,
            Characteristics = new MethodCharacteristics
            {
                IsAsync = isAsync,
                IsStatic = isStatic,
                IsGeneric = typeParameters.Count > 0,
                HasMultipleReturns = returns.Count > 1,
                HasEarlyReturns = returns.Count > (selection.Statements.Count > 0 && selection.Statements[^1] is ReturnStatementSyntax ? 1 : 0),
                CapturesVariables = !dataFlow.Captured.IsEmpty,
                ContainsAwait = isAsync,
                ContainsYield = false
            }
        }, null);
    }

    private static (ExtractionAnalysis?, ExtractMethodResult?) Fail(string message, string code) =>
        (null, ExtractMethodResult.CreateError(message, code));

    /// <summary>
    /// Statements may only leave the selection by falling through its end, or - when the end is
    /// unreachable - through return statements of the enclosing method (the call site then returns too).
    /// </summary>
    private static (bool EndsWithReturn, ExtractMethodResult? Error) AnalyzeControlFlow(
        ExtractionSelection selection,
        SemanticModel model)
    {
        if (selection.Expression != null)
        {
            return (false, null);
        }

        var controlFlow = model.AnalyzeControlFlow(selection.Statements[0], selection.Statements[^1]);
        if (controlFlow is not { Succeeded: true })
        {
            return (false, ExtractMethodResult.CreateError("Unable to analyze control flow of the selection", "CONTROL_FLOW_ERROR"));
        }

        if (!controlFlow.EntryPoints.IsEmpty)
        {
            return (false, ExtractMethodResult.CreateError(
                "Selection has multiple entry points - cannot extract", "MULTIPLE_ENTRY_POINTS"));
        }

        if (controlFlow.ExitPoints.IsEmpty)
        {
            return (false, null);
        }

        if (!controlFlow.EndPointIsReachable && controlFlow.ExitPoints.All(p => p is ReturnStatementSyntax))
        {
            return (true, null);
        }

        return (false, ExtractMethodResult.CreateError(
            "Selection contains break, continue or return statements that jump out of it; extracting it would change control flow",
            "CONTROL_FLOW_EXITS",
            ["Extend the selection to the whole loop or to the end of the method"]));
    }

    private static VariableFlow ClassifyVariables(DataFlowAnalysis dataFlow)
    {
        var declaredInside = new HashSet<ISymbol>(dataFlow.VariablesDeclared, SymbolComparer);
        var writtenInside = new HashSet<ISymbol>(dataFlow.WrittenInside, SymbolComparer);
        var flowsOut = new HashSet<ISymbol>(dataFlow.DataFlowsOut, SymbolComparer);
        var usedOutside = new HashSet<ISymbol>(dataFlow.ReadOutside.Concat(dataFlow.WrittenOutside), SymbolComparer);

        // Constants have no data flow; pass any outer constant that is read as an argument
        var inputs = OrderByDeclaration(dataFlow.DataFlowsIn
            .Concat(dataFlow.ReadInside.Where(s => s is ILocalSymbol { IsConst: true } && !declaredInside.Contains(s)))
            .Where(IsVariable));
        var inputSet = new HashSet<ISymbol>(inputs, SymbolComparer);

        var refInputs = new HashSet<ISymbol>(
            inputs.Where(s => writtenInside.Contains(s) && flowsOut.Contains(s)), SymbolComparer);

        var outputs = OrderByDeclaration(flowsOut
            .Concat(declaredInside.Where(usedOutside.Contains))
            .Where(s => IsVariable(s) && !inputSet.Contains(s)));

        var localsToDeclare = OrderByDeclaration(dataFlow.ReadInside
            .Concat(dataFlow.WrittenInside)
            .Where(s => IsVariable(s) && !declaredInside.Contains(s) && !inputSet.Contains(s)));

        return new VariableFlow(
            inputs,
            refInputs,
            outputs,
            new HashSet<ISymbol>(outputs.Where(declaredInside.Contains), SymbolComparer),
            localsToDeclare,
            writtenInside.Any(s => IsVariable(s) && !declaredInside.Contains(s)));
    }

    private static ExtractMethodResult? ValidateFlow(
        ExtractionSelection selection,
        VariableFlow flow,
        bool isAsync,
        bool endsWithReturn)
    {
        if (selection.Expression != null && (flow.WritesOuterVariables || flow.Outputs.Count > 0))
        {
            return ExtractMethodResult.CreateError(
                "The selected expression assigns variables used elsewhere; select whole statements instead",
                "EXPRESSION_WRITES_VARIABLES");
        }

        if (isAsync && flow.RefInputs.Count > 0)
        {
            return ExtractMethodResult.CreateError(
                "Cannot extract async code that reassigns variables used after the selection (async methods cannot take ref parameters): "
                + string.Join(", ", flow.RefInputs.Select(s => s.Name)),
                "ASYNC_REF_PARAMETERS");
        }

        if (endsWithReturn && flow.Outputs.Count > 0)
        {
            return ExtractMethodResult.CreateError(
                "Selection both returns from the method and assigns variables used afterwards", "CONTROL_FLOW_EXITS");
        }

        return null;
    }

    private static (ITypeSymbol? ReturnType, bool ReturnsValue, ExtractMethodResult? Error) DetermineReturnType(
        ExtractionSelection selection,
        SemanticModel model,
        VariableFlow flow,
        bool isAsync,
        bool endsWithReturn)
    {
        var compilation = model.Compilation;
        ITypeSymbol? resultType;
        if (endsWithReturn)
        {
            if (model.GetEnclosingSymbol(selection.FirstNode.SpanStart) is not IMethodSymbol owner)
            {
                return (null, false, ExtractMethodResult.CreateError("Unable to determine the enclosing method's return type", "CONTROL_FLOW_ERROR"));
            }

            resultType = EffectiveReturnType(owner);
            if (isAsync)
            {
                // The awaits are legal only because the owner is async: keep its task-like return type
                return (owner.ReturnType, resultType != null, null);
            }

            return (resultType, resultType != null, null);
        }

        if (selection.Expression != null)
        {
            var typeInfo = model.GetTypeInfo(selection.Expression);
            resultType = typeInfo.Type ?? typeInfo.ConvertedType;
        }
        else
        {
            resultType = flow.Outputs.Count switch
            {
                0 => null,
                1 => TypeOf(flow.Outputs[0]),
                _ => compilation.CreateTupleTypeSymbol(
                    flow.Outputs.Select(TypeOf).ToImmutableArray(),
                    flow.Outputs.Select(o => (string?)o.Name).ToImmutableArray(),
                    flow.Outputs.Select(_ => (Location?)Location.None).ToImmutableArray(),
                    flow.Outputs.Select(o => TypeOf(o).NullableAnnotation).ToImmutableArray())
            };
        }

        if (!isAsync)
        {
            return (resultType, false, null);
        }

        var taskType = resultType == null
            ? compilation.GetTypeByMetadataName("System.Threading.Tasks.Task")
            : compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1")?.Construct(resultType);

        return taskType == null
            ? (null, false, ExtractMethodResult.CreateError("System.Threading.Tasks.Task is not available in this compilation", "ASYNC_UNSUPPORTED"))
            : (taskType, false, null);
    }

    /// <summary>
    /// The type a <c>return</c> statement in <paramref name="method"/> produces (null for none).
    /// </summary>
    private static ITypeSymbol? EffectiveReturnType(IMethodSymbol method)
    {
        if (method.IsAsync)
        {
            return method.ReturnType is INamedTypeSymbol { TypeArguments.Length: 1 } task ? task.TypeArguments[0] : null;
        }

        return method.ReturnsVoid ? null : method.ReturnType;
    }

    private static bool IsVariable(ISymbol symbol) =>
        symbol is ILocalSymbol or IParameterSymbol { IsThis: false };

    private static List<ISymbol> OrderByDeclaration(IEnumerable<ISymbol> symbols) =>
        symbols
            .Distinct(SymbolComparer)
            .OrderBy(s => s.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ToList();

    private static ITypeSymbol TypeOf(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        _ => throw new InvalidOperationException($"Unexpected symbol kind {symbol.Kind}")
    };

    private static bool ContainsAnonymousType(ITypeSymbol? type) => type switch
    {
        null => false,
        { IsAnonymousType: true } => true,
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsAnonymousType),
        IArrayTypeSymbol array => ContainsAnonymousType(array.ElementType),
        _ => false
    };

    private static bool IsNestedFunction(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    /// <summary>
    /// Nodes of type <typeparamref name="T"/> in the selection that belong to the selection's own
    /// function body (not to lambdas or local functions inside it).
    /// </summary>
    private static IEnumerable<T> FindInSelection<T>(ExtractionSelection selection) where T : SyntaxNode =>
        selection.Nodes
            .SelectMany(n => n.DescendantNodesAndSelf(c => !IsNestedFunction(c)))
            .Where(n => !IsNestedFunction(n))
            .OfType<T>();

    private static bool ContainsAwait(ExtractionSelection selection) =>
        FindInSelection<SyntaxNode>(selection).Any(n => n switch
        {
            AwaitExpressionSyntax => true,
            CommonForEachStatementSyntax forEach => !forEach.AwaitKeyword.IsKind(SyntaxKind.None),
            UsingStatementSyntax usingStatement => !usingStatement.AwaitKeyword.IsKind(SyntaxKind.None),
            LocalDeclarationStatementSyntax declaration => !declaration.AwaitKeyword.IsKind(SyntaxKind.None),
            _ => false
        });

    /// <summary>
    /// Whether the extracted code must be static: static members, field initializers and property
    /// initializers cannot use instance members.
    /// </summary>
    private static bool IsStaticContext(ExtractionSelection selection)
    {
        var member = selection.ContainingMember;
        if (member.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ConstKeyword)))
        {
            return true;
        }

        return member is FieldDeclarationSyntax ||
               (member is PropertyDeclarationSyntax { Initializer: { } initializer } &&
                initializer.Span.Contains(selection.FirstNode.Span));
    }

    private static bool UsesInstanceMembers(ExtractionSelection selection, SemanticModel model, DataFlowAnalysis dataFlow)
    {
        if (dataFlow.ReadInside.Concat(dataFlow.WrittenInside).Any(s => s is IParameterSymbol { IsThis: true }))
        {
            return true;
        }

        return selection.Nodes
            .SelectMany(n => n.DescendantNodesAndSelf())
            .Any(n => n switch
            {
                ThisExpressionSyntax or BaseExpressionSyntax => true,
                SimpleNameSyntax name => IsImplicitInstanceMemberAccess(name, model),
                _ => false
            });
    }

    private static bool IsImplicitInstanceMemberAccess(SimpleNameSyntax name, SemanticModel model)
    {
        var qualified = name.Parent switch
        {
            MemberAccessExpressionSyntax access => access.Name == name,
            MemberBindingExpressionSyntax or QualifiedNameSyntax or NameColonSyntax or NameEqualsSyntax => true,
            AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment => assignment.Left == name,
            _ => false
        };

        if (qualified)
        {
            return false;
        }

        return model.GetSymbolInfo(name).Symbol is
            IFieldSymbol { IsStatic: false, IsConst: false } or
            IPropertySymbol { IsStatic: false } or
            IEventSymbol { IsStatic: false } or
            IMethodSymbol { IsStatic: false, MethodKind: MethodKind.Ordinary };
    }

    private static List<ITypeParameterSymbol> CollectMethodTypeParameters(
        ExtractionSelection selection,
        SemanticModel model,
        IEnumerable<ITypeSymbol?> signatureTypes)
    {
        var found = new HashSet<ITypeParameterSymbol>(SymbolComparer);
        foreach (var type in signatureTypes)
        {
            AddTypeParameters(type, found);
        }

        foreach (var node in selection.Nodes.SelectMany(n => n.DescendantNodesAndSelf()).OfType<ExpressionSyntax>())
        {
            AddTypeParameters(model.GetTypeInfo(node).Type, found);
            switch (model.GetSymbolInfo(node).Symbol)
            {
                case ITypeSymbol type:
                    AddTypeParameters(type, found);
                    break;
                case IMethodSymbol method:
                    foreach (var argument in method.TypeArguments)
                    {
                        AddTypeParameters(argument, found);
                    }
                    break;
            }
        }

        return found.OrderBy(t => t.Ordinal).ToList();
    }

    private static void AddTypeParameters(ITypeSymbol? type, HashSet<ITypeParameterSymbol> found)
    {
        switch (type)
        {
            case ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter:
                found.Add(parameter);
                break;
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                {
                    AddTypeParameters(argument, found);
                }
                break;
            case IArrayTypeSymbol array:
                AddTypeParameters(array.ElementType, found);
                break;
            case IPointerTypeSymbol pointer:
                AddTypeParameters(pointer.PointedAtType, found);
                break;
        }
    }

    /// <summary>
    /// Use the requested name, or the first <c>ExtractedMethod</c>, <c>ExtractedMethod1</c>, ... that
    /// the containing type does not already declare.
    /// </summary>
    private static string ChooseMethodName(string? requested, ExtractionSelection selection, SemanticModel model)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        var existing = selection.ContainingMember.Parent is TypeDeclarationSyntax typeDeclaration &&
                       model.GetDeclaredSymbol(typeDeclaration) is { } type
            ? new HashSet<string>(type.MemberNames, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var name = DefaultMethodName;
        var suffix = 0;
        while (existing.Contains(name))
        {
            suffix++;
            name = DefaultMethodName + suffix.ToString(CultureInfo.InvariantCulture);
        }

        return name;
    }
}

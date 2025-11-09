# Extract Method - Roslyn API Integration Guide

## Essential Roslyn APIs for Extract Method

This document provides detailed Roslyn API usage patterns specifically for extract method refactoring implementation.

## 1. Data Flow Analysis APIs

### Basic Data Flow Analysis
```csharp
public class DataFlowAnalyzer
{
    public ExtractedVariables AnalyzeDataFlow(
        SemanticModel semanticModel,
        SyntaxNode firstStatement,
        SyntaxNode lastStatement)
    {
        // Get data flow analysis from Roslyn
        DataFlowAnalysis dataFlow = semanticModel.AnalyzeDataFlow(
            firstStatement,
            lastStatement);

        var result = new ExtractedVariables();

        // Variables that flow INTO the selection (potential parameters)
        foreach (ISymbol symbol in dataFlow.DataFlowsIn)
        {
            // Skip 'this' parameter
            if (symbol.IsThisParameter()) continue;

            // Skip constants and static fields
            if (symbol is IFieldSymbol field && (field.IsConst || field.IsStatic))
                continue;

            result.InputVariables.Add(new Variable
            {
                Symbol = symbol,
                Name = symbol.Name,
                Type = symbol.GetSymbolType(),
                NeedsRef = dataFlow.WrittenInside.Contains(symbol) &&
                          dataFlow.ReadOutside.Contains(symbol),
                NeedsOut = dataFlow.WrittenInside.Contains(symbol) &&
                          !dataFlow.ReadInside.Contains(symbol)
            });
        }

        // Variables written inside and read outside (potential returns)
        var potentialReturns = dataFlow.WrittenInside
            .Intersect(dataFlow.ReadOutside)
            .Where(s => !dataFlow.DataFlowsIn.Contains(s));

        foreach (ISymbol symbol in potentialReturns)
        {
            result.OutputVariables.Add(new Variable
            {
                Symbol = symbol,
                Name = symbol.Name,
                Type = symbol.GetSymbolType()
            });
        }

        // Captured variables (for closure detection)
        result.CapturedVariables = dataFlow.Captured.ToList();

        // Variables declared inside
        result.DeclaredVariables = dataFlow.VariablesDeclared.ToList();

        return result;
    }
}
```

### Control Flow Analysis
```csharp
public class ControlFlowAnalyzer
{
    public ControlFlowInfo AnalyzeControlFlow(
        SemanticModel semanticModel,
        SyntaxNode firstStatement,
        SyntaxNode lastStatement)
    {
        ControlFlowAnalysis controlFlow = semanticModel.AnalyzeControlFlow(
            firstStatement,
            lastStatement);

        return new ControlFlowInfo
        {
            EntryPoints = controlFlow.EntryPoints,
            ExitPoints = controlFlow.ExitPoints,
            EndPointIsReachable = controlFlow.EndPointIsReachable,
            StartPointIsReachable = controlFlow.StartPointIsReachable,
            ReturnStatements = controlFlow.ReturnStatements,

            // Check for problematic control flow
            HasMultipleEntryPoints = controlFlow.EntryPoints.Length > 1,
            HasMultipleExitPoints = controlFlow.ExitPoints.Length > 1,
            HasUnreachableCode = !controlFlow.StartPointIsReachable ||
                                 HasUnreachableStatements(firstStatement, lastStatement)
        };
    }

    private bool HasUnreachableStatements(SyntaxNode first, SyntaxNode last)
    {
        // Use Roslyn's reachability analysis
        var statements = GetStatementsBetween(first, last);
        foreach (var statement in statements)
        {
            var flow = semanticModel.AnalyzeControlFlow(statement);
            if (!flow.StartPointIsReachable)
                return true;
        }
        return false;
    }
}
```

## 2. Symbol Analysis APIs

### Type Inference
```csharp
public class TypeInferencer
{
    public string InferType(
        SemanticModel semanticModel,
        ExpressionSyntax expression)
    {
        // Get type info from Roslyn
        TypeInfo typeInfo = semanticModel.GetTypeInfo(expression);

        ITypeSymbol? type = typeInfo.Type ?? typeInfo.ConvertedType;

        if (type == null)
            return "object"; // Fallback

        // Handle special cases
        if (type.TypeKind == TypeKind.Error)
            return HandleErrorType(expression, semanticModel);

        if (type is IArrayTypeSymbol arrayType)
            return $"{GetTypeName(arrayType.ElementType)}[]";

        if (type is INamedTypeSymbol namedType && namedType.IsGenericType)
            return FormatGenericType(namedType);

        return GetTypeName(type);
    }

    private string FormatGenericType(INamedTypeSymbol type)
    {
        var baseName = type.Name;
        var typeArgs = string.Join(", ",
            type.TypeArguments.Select(GetTypeName));
        return $"{baseName}<{typeArgs}>";
    }

    private string GetTypeName(ITypeSymbol type)
    {
        // Use ToDisplayString for proper formatting
        return type.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat);
    }
}
```

### Symbol Resolution
```csharp
public class SymbolResolver
{
    public ISymbol? ResolveSymbol(
        SemanticModel semanticModel,
        SyntaxNode node)
    {
        // Try different resolution strategies
        SymbolInfo symbolInfo = semanticModel.GetSymbolInfo(node);

        // Direct symbol
        if (symbolInfo.Symbol != null)
            return symbolInfo.Symbol;

        // Candidate symbols (ambiguous)
        if (symbolInfo.CandidateSymbols.Length == 1)
            return symbolInfo.CandidateSymbols[0];

        // Try declared symbol (for declarations)
        var declared = semanticModel.GetDeclaredSymbol(node);
        if (declared != null)
            return declared;

        // Try enclosing symbol
        return semanticModel.GetEnclosingSymbol(node.SpanStart);
    }
}
```

## 3. Syntax Rewriting APIs

### Method Extraction Rewriter
```csharp
public class ExtractMethodRewriter : CSharpSyntaxRewriter
{
    private readonly TextSpan _selectionSpan;
    private readonly MethodDeclarationSyntax _newMethod;
    private readonly StatementSyntax _methodCall;
    private readonly SyntaxTriviaList _leadingTrivia;
    private readonly SyntaxTriviaList _trailingTrivia;

    public ExtractMethodRewriter(
        TextSpan selectionSpan,
        MethodDeclarationSyntax newMethod,
        StatementSyntax methodCall)
    {
        _selectionSpan = selectionSpan;
        _newMethod = newMethod;
        _methodCall = methodCall;
    }

    public override SyntaxNode? VisitMethodDeclaration(
        MethodDeclarationSyntax node)
    {
        if (!node.Span.Contains(_selectionSpan))
            return base.VisitMethodDeclaration(node);

        // Extract statements from the method body
        var body = node.Body;
        if (body == null)
            return node;

        var statements = body.Statements;
        var newStatements = new List<StatementSyntax>();
        bool replaced = false;

        foreach (var statement in statements)
        {
            if (!replaced && _selectionSpan.Contains(statement.Span))
            {
                // First statement in selection - replace with call
                newStatements.Add(_methodCall
                    .WithLeadingTrivia(statement.GetLeadingTrivia())
                    .WithTrailingTrivia(statement.GetTrailingTrivia()));
                replaced = true;
            }
            else if (_selectionSpan.Contains(statement.Span))
            {
                // Skip other statements in selection
                continue;
            }
            else
            {
                // Keep statements outside selection
                newStatements.Add(statement);
            }
        }

        // Update method body
        var newBody = body.WithStatements(
            SyntaxFactory.List(newStatements));

        return node.WithBody(newBody);
    }

    public override SyntaxNode? VisitClassDeclaration(
        ClassDeclarationSyntax node)
    {
        var visited = base.VisitClassDeclaration(node) as ClassDeclarationSyntax;
        if (visited == null)
            return node;

        // Add the new method to the class
        if (ContainsSelection(node))
        {
            var members = visited.Members.Add(_newMethod);
            return visited.WithMembers(members);
        }

        return visited;
    }

    private bool ContainsSelection(SyntaxNode node)
    {
        return node.Span.Contains(_selectionSpan);
    }
}
```

### Variable Reference Updater
```csharp
public class VariableReferenceRewriter : CSharpSyntaxRewriter
{
    private readonly Dictionary<string, string> _variableMap;
    private readonly SemanticModel _semanticModel;

    public VariableReferenceRewriter(
        Dictionary<string, string> variableMap,
        SemanticModel semanticModel)
    {
        _variableMap = variableMap;
        _semanticModel = semanticModel;
    }

    public override SyntaxNode? VisitIdentifierName(
        IdentifierNameSyntax node)
    {
        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

        if (symbol is ILocalSymbol || symbol is IParameterSymbol)
        {
            var name = symbol.Name;
            if (_variableMap.TryGetValue(name, out var newName))
            {
                return SyntaxFactory.IdentifierName(newName)
                    .WithTriviaFrom(node);
            }
        }

        return base.VisitIdentifierName(node);
    }
}
```

## 4. Semantic Queries

### Finding Containing Method
```csharp
public static class SemanticQueries
{
    public static IMethodSymbol? GetContainingMethod(
        SemanticModel model,
        SyntaxNode node)
    {
        var symbol = model.GetEnclosingSymbol(node.SpanStart);

        while (symbol != null)
        {
            if (symbol is IMethodSymbol method)
                return method;
            symbol = symbol.ContainingSymbol;
        }

        return null;
    }

    public static bool IsInAsyncContext(
        SemanticModel model,
        SyntaxNode node)
    {
        var method = GetContainingMethod(model, node);
        return method?.IsAsync ?? false;
    }

    public static bool IsInGenericContext(
        SemanticModel model,
        SyntaxNode node)
    {
        var method = GetContainingMethod(model, node);
        if (method?.IsGenericMethod ?? false)
            return true;

        var type = method?.ContainingType;
        return type?.IsGenericType ?? false;
    }
}
```

## 5. Compilation Update APIs

### Applying Changes
```csharp
public class CompilationUpdater
{
    private readonly Workspace _workspace;

    public async Task<Document> ApplyExtraction(
        Document document,
        TextSpan selection,
        MethodDeclarationSyntax extractedMethod,
        StatementSyntax callSite)
    {
        var root = await document.GetSyntaxRootAsync();
        if (root == null)
            throw new InvalidOperationException("Cannot get syntax root");

        // Create rewriter
        var rewriter = new ExtractMethodRewriter(
            selection,
            extractedMethod,
            callSite);

        // Apply changes
        var newRoot = rewriter.Visit(root);

        // Update document
        return document.WithSyntaxRoot(newRoot);
    }

    public async Task<Solution> UpdateSolution(
        Solution solution,
        DocumentId documentId,
        SyntaxNode newRoot)
    {
        var document = solution.GetDocument(documentId);
        if (document == null)
            return solution;

        var newDocument = document.WithSyntaxRoot(newRoot);
        return newDocument.Project.Solution;
    }
}
```

## 6. Diagnostic Analysis

### Pre-extraction Validation
```csharp
public class ExtractionValidator
{
    public async Task<ImmutableArray<Diagnostic>> ValidateExtraction(
        Document document,
        TextSpan selection)
    {
        var model = await document.GetSemanticModelAsync();
        if (model == null)
            return ImmutableArray<Diagnostic>.Empty;

        var diagnostics = model.GetDiagnostics(selection);

        // Filter to only errors
        var errors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        if (errors.Any())
        {
            // Cannot extract code with compilation errors
            return errors;
        }

        return ImmutableArray<Diagnostic>.Empty;
    }
}
```

## 7. Formatting APIs

### Code Formatting
```csharp
public class CodeFormatter
{
    public async Task<SyntaxNode> FormatExtractedMethod(
        Document document,
        MethodDeclarationSyntax method)
    {
        // Use Roslyn formatter
        var formatted = Formatter.Format(
            method,
            document.Project.Solution.Workspace,
            document.Project.Solution.Workspace.Options);

        // Apply additional formatting rules
        var withTrivia = formatted
            .WithLeadingTrivia(GenerateDocumentationComment(method))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        return withTrivia;
    }

    private SyntaxTriviaList GenerateDocumentationComment(
        MethodDeclarationSyntax method)
    {
        var parameters = method.ParameterList.Parameters;
        var xml = new StringBuilder();

        xml.AppendLine("/// <summary>");
        xml.AppendLine("/// Extracted method");
        xml.AppendLine("/// </summary>");

        foreach (var param in parameters)
        {
            xml.AppendLine($"/// <param name=\"{param.Identifier}\">{param.Type}</param>");
        }

        return SyntaxFactory.ParseLeadingTrivia(xml.ToString());
    }
}
```

## 8. Special Case Handlers

### Async Method Detection and Transformation
```csharp
public class AsyncMethodHandler
{
    public bool RequiresAsync(SyntaxNode node)
    {
        return node.DescendantNodes()
            .Any(n => n is AwaitExpressionSyntax);
    }

    public ITypeSymbol TransformReturnType(
        ITypeSymbol originalType,
        Compilation compilation)
    {
        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var voidTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");

        if (originalType.SpecialType == SpecialType.System_Void)
        {
            return voidTaskType ?? originalType;
        }

        if (taskType != null)
        {
            return taskType.Construct(originalType);
        }

        return originalType;
    }
}
```

## 9. Performance Optimizations

### Incremental Semantic Model
```csharp
public class IncrementalAnalyzer
{
    private SemanticModel? _cachedModel;
    private SyntaxTree? _cachedTree;

    public async Task<SemanticModel> GetSemanticModelAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var tree = await document.GetSyntaxTreeAsync(cancellationToken);

        // Return cached model if tree hasn't changed
        if (_cachedTree == tree && _cachedModel != null)
            return _cachedModel;

        _cachedTree = tree;
        _cachedModel = await document.GetSemanticModelAsync(cancellationToken);

        return _cachedModel!;
    }
}
```

## Key Roslyn Types Reference

| Type | Purpose | Key Methods |
|------|---------|-------------|
| `SemanticModel` | Semantic analysis | `AnalyzeDataFlow`, `AnalyzeControlFlow`, `GetTypeInfo` |
| `DataFlowAnalysis` | Variable flow | `DataFlowsIn`, `DataFlowsOut`, `WrittenInside` |
| `ControlFlowAnalysis` | Control flow | `EntryPoints`, `ExitPoints`, `ReturnStatements` |
| `CSharpSyntaxRewriter` | AST transformation | `Visit*` methods |
| `SymbolInfo` | Symbol resolution | `Symbol`, `CandidateSymbols` |
| `TypeInfo` | Type information | `Type`, `ConvertedType` |
| `IMethodSymbol` | Method metadata | `Parameters`, `ReturnType`, `TypeParameters` |
| `Formatter` | Code formatting | `Format` |
| `SyntaxFactory` | Node creation | All `Create*` methods |

## Testing Roslyn Integration

```csharp
[Test]
public async Task RoslynIntegration_DataFlow_CorrectAnalysis()
{
    var code = @"
class C {
    void M() {
        int x = 1;
        int y = 2;
        [|var sum = x + y;
        Console.WriteLine(sum);|]
        Console.WriteLine(x);
    }
}";

    var document = CreateDocument(code);
    var selection = GetSelection(code);
    var model = await document.GetSemanticModelAsync();

    var dataFlow = model.AnalyzeDataFlow(
        selection.First,
        selection.Last);

    Assert.That(dataFlow.DataFlowsIn.Count(), Is.EqualTo(2)); // x, y
    Assert.That(dataFlow.WrittenInside.Count(), Is.EqualTo(1)); // sum
    Assert.That(dataFlow.ReadOutside.Count(), Is.EqualTo(1)); // x
}
```

This guide provides the essential Roslyn API patterns needed for implementing extract method refactoring in MCPsharp.
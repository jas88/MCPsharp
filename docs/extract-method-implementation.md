# Extract Method Implementation Plan

## Core Service Implementation

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MCPsharp.Services.Refactoring;

public class ExtractMethodService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SelectionAnalyzer _selectionAnalyzer;
    private readonly DataFlowAnalyzer _dataFlowAnalyzer;
    private readonly MethodGenerator _methodGenerator;
    private readonly CallSiteRewriter _callSiteRewriter;

    public ExtractMethodService(
        RoslynWorkspace workspace,
        SelectionAnalyzer selectionAnalyzer,
        DataFlowAnalyzer dataFlowAnalyzer,
        MethodGenerator methodGenerator,
        CallSiteRewriter callSiteRewriter)
    {
        _workspace = workspace;
        _selectionAnalyzer = selectionAnalyzer;
        _dataFlowAnalyzer = dataFlowAnalyzer;
        _methodGenerator = methodGenerator;
        _callSiteRewriter = callSiteRewriter;
    }

    public async Task<ExtractMethodResult> ExtractMethodAsync(
        string filePath,
        Selection selection,
        string? methodName = null,
        ExtractMethodOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= ExtractMethodOptions.Default;

        try
        {
            // Step 1: Get document and semantic model
            var document = _workspace.GetDocument(filePath);
            if (document == null)
                return ExtractMethodResult.Error("Document not found");

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);

            // Step 2: Analyze selection
            var selectionSpan = GetTextSpan(syntaxRoot, selection);
            var selectedNodes = GetSelectedNodes(syntaxRoot, selectionSpan);

            var validationResult = _selectionAnalyzer.Validate(
                selectedNodes,
                semanticModel,
                options);

            if (!validationResult.IsValid)
                return ExtractMethodResult.Error(validationResult.ErrorMessage);

            // Step 3: Analyze data flow
            var dataFlowAnalysis = _dataFlowAnalyzer.Analyze(
                selectedNodes,
                semanticModel,
                validationResult.ContainingMethod);

            // Step 4: Infer parameters and return type
            var inferenceResult = InferMethodSignature(
                dataFlowAnalysis,
                selectedNodes,
                methodName,
                options);

            // Step 5: Generate extracted method
            var extractedMethod = _methodGenerator.Generate(
                inferenceResult,
                selectedNodes,
                options);

            // Step 6: Generate call site replacement
            var callSite = _callSiteRewriter.GenerateCallSite(
                inferenceResult,
                dataFlowAnalysis);

            // Step 7: Apply transformations (if not preview mode)
            if (!options.Preview)
            {
                var newRoot = ApplyExtraction(
                    syntaxRoot,
                    selectedNodes,
                    extractedMethod,
                    callSite,
                    validationResult.ContainingMethod);

                document = document.WithSyntaxRoot(newRoot);
                _workspace.UpdateDocument(document);
            }

            return ExtractMethodResult.Success(
                extractedMethod,
                callSite,
                inferenceResult,
                options.Preview ? GeneratePreview(syntaxRoot, newRoot) : null);
        }
        catch (Exception ex)
        {
            return ExtractMethodResult.Error($"Extraction failed: {ex.Message}");
        }
    }
}
```

## Selection Analyzer Implementation

```csharp
public class SelectionAnalyzer
{
    public ValidationResult Validate(
        IEnumerable<SyntaxNode> selectedNodes,
        SemanticModel semanticModel,
        ExtractMethodOptions options)
    {
        var result = new ValidationResult();

        // Check for complete statements
        if (!AreCompleteStatements(selectedNodes) &&
            options.ExtractMode == ExtractMode.Statements)
        {
            return ValidationResult.Invalid(
                "Selection must contain complete statements. " +
                "Adjust selection boundaries or use expression mode.");
        }

        // Check for single entry point
        var controlFlow = semanticModel.AnalyzeControlFlow(
            selectedNodes.First(),
            selectedNodes.Last());

        if (controlFlow.EntryPoints.Length > 1)
        {
            return ValidationResult.Invalid(
                "Selection has multiple entry points. " +
                "Cannot extract code that is entered from multiple locations.");
        }

        // Check for goto/labels
        var hasGoto = selectedNodes.Any(n =>
            n.DescendantNodes().Any(d =>
                d is GotoStatementSyntax ||
                d is LabeledStatementSyntax));

        if (hasGoto)
        {
            return ValidationResult.Invalid(
                "Cannot extract code containing goto statements or labels. " +
                "Refactor goto/label usage before extraction.");
        }

        // Find containing method
        var containingMethod = selectedNodes.First()
            .Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (containingMethod == null)
        {
            return ValidationResult.Invalid(
                "Selection must be within a method body.");
        }

        result.ContainingMethod = containingMethod;
        result.IsValid = true;

        // Detect special characteristics
        result.ContainsAwait = selectedNodes.Any(n =>
            n.DescendantNodes().Any(d => d is AwaitExpressionSyntax));

        result.ContainsYield = selectedNodes.Any(n =>
            n.DescendantNodes().Any(d => d is YieldStatementSyntax));

        result.HasMultipleExits = controlFlow.ExitPoints.Length > 1;

        return result;
    }

    private bool AreCompleteStatements(IEnumerable<SyntaxNode> nodes)
    {
        return nodes.All(n => n is StatementSyntax);
    }
}
```

## Data Flow Analyzer Implementation

```csharp
public class DataFlowAnalyzer
{
    public DataFlowAnalysisResult Analyze(
        IEnumerable<SyntaxNode> selectedNodes,
        SemanticModel semanticModel,
        MethodDeclarationSyntax containingMethod)
    {
        var firstStatement = selectedNodes.First();
        var lastStatement = selectedNodes.Last();

        // Perform Roslyn data flow analysis
        var dataFlow = semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
        var controlFlow = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);

        var result = new DataFlowAnalysisResult();

        // Analyze input parameters
        foreach (var symbol in dataFlow.DataFlowsIn)
        {
            if (ShouldBeParameter(symbol, dataFlow))
            {
                var parameter = new ExtractedParameter
                {
                    Name = symbol.Name,
                    Type = GetTypeString(symbol),
                    Symbol = symbol,
                    Modifier = DetermineModifier(symbol, dataFlow)
                };
                result.Parameters.Add(parameter);
            }
        }

        // Analyze return values
        var writtenAndRead = dataFlow.WrittenInside
            .Intersect(dataFlow.ReadOutside)
            .Where(s => !dataFlow.DataFlowsIn.Contains(s));

        if (writtenAndRead.Count() == 1)
        {
            var returnSymbol = writtenAndRead.First();
            result.ReturnType = GetTypeString(returnSymbol);
            result.ReturnVariable = returnSymbol.Name;
            result.ReturnStrategy = ReturnStrategy.Single;
        }
        else if (writtenAndRead.Count() > 1)
        {
            result.ReturnStrategy = DetermineMultiReturnStrategy(writtenAndRead);

            if (result.ReturnStrategy == ReturnStrategy.Tuple)
            {
                result.ReturnType = GenerateTupleType(writtenAndRead);
                result.ReturnVariables = writtenAndRead.Select(s => s.Name).ToList();
            }
            else // Out parameters
            {
                foreach (var symbol in writtenAndRead)
                {
                    var param = result.Parameters
                        .FirstOrDefault(p => p.Symbol.Equals(symbol));

                    if (param != null)
                    {
                        param.Modifier = ParameterModifier.Out;
                    }
                    else
                    {
                        result.Parameters.Add(new ExtractedParameter
                        {
                            Name = symbol.Name,
                            Type = GetTypeString(symbol),
                            Symbol = symbol,
                            Modifier = ParameterModifier.Out
                        });
                    }
                }
                result.ReturnType = "void";
            }
        }

        // Handle control flow exits
        AnalyzeControlFlowExits(controlFlow, result);

        // Detect captured variables
        result.CapturedVariables = dataFlow.Captured.ToList();

        return result;
    }

    private bool ShouldBeParameter(ISymbol symbol, DataFlowAnalysis dataFlow)
    {
        // Don't make 'this' a parameter
        if (symbol.IsThisParameter())
            return false;

        // Don't make constants parameters
        if (symbol is IFieldSymbol field && field.IsConst)
            return false;

        // Don't make static members parameters
        if (symbol.IsStatic)
            return false;

        return true;
    }

    private ParameterModifier DetermineModifier(ISymbol symbol, DataFlowAnalysis dataFlow)
    {
        var isWritten = dataFlow.WrittenInside.Contains(symbol);
        var isRead = dataFlow.ReadInside.Contains(symbol);
        var isReadAfter = dataFlow.ReadOutside.Contains(symbol);

        if (isWritten && isReadAfter)
        {
            return ParameterModifier.Ref;
        }
        else if (isWritten && !isRead)
        {
            return ParameterModifier.Out;
        }

        return ParameterModifier.None;
    }
}
```

## Method Generator Implementation

```csharp
public class MethodGenerator
{
    public MethodDeclarationSyntax Generate(
        MethodSignature signature,
        IEnumerable<SyntaxNode> bodyNodes,
        ExtractMethodOptions options)
    {
        var method = SyntaxFactory.MethodDeclaration(
            SyntaxFactory.ParseTypeName(signature.ReturnType),
            signature.Name);

        // Add modifiers
        var modifiers = new List<SyntaxToken>();

        modifiers.Add(SyntaxFactory.Token(
            GetAccessibilityKind(signature.Accessibility)));

        if (signature.IsStatic)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

        if (signature.IsAsync)
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));

        method = method.WithModifiers(SyntaxFactory.TokenList(modifiers));

        // Add type parameters
        if (signature.TypeParameters.Any())
        {
            var typeParams = signature.TypeParameters
                .Select(tp => SyntaxFactory.TypeParameter(tp))
                .ToArray();

            method = method.WithTypeParameterList(
                SyntaxFactory.TypeParameterList(
                    SyntaxFactory.SeparatedList(typeParams)));
        }

        // Add parameters
        var parameters = signature.Parameters
            .Select(p => CreateParameter(p))
            .ToArray();

        method = method.WithParameterList(
            SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(parameters)));

        // Add constraints
        if (signature.Constraints.Any())
        {
            var constraints = signature.Constraints
                .Select(c => ParseConstraintClause(c))
                .ToArray();

            method = method.WithConstraintClauses(
                SyntaxFactory.List(constraints));
        }

        // Transform and add body
        var transformedBody = TransformBody(
            bodyNodes,
            signature,
            options);

        method = method.WithBody(
            SyntaxFactory.Block(transformedBody));

        // Add XML documentation
        var xmlDoc = GenerateXmlDocumentation(signature);
        method = method.WithLeadingTrivia(xmlDoc);

        return method;
    }

    private IEnumerable<StatementSyntax> TransformBody(
        IEnumerable<SyntaxNode> originalNodes,
        MethodSignature signature,
        ExtractMethodOptions options)
    {
        var statements = originalNodes.Cast<StatementSyntax>().ToList();

        // Handle multiple returns transformation
        if (signature.HasMultipleReturns)
        {
            statements = TransformMultipleReturns(statements, signature);
        }

        // Handle early returns
        if (signature.HasEarlyReturns)
        {
            statements = TransformEarlyReturns(statements, signature);
        }

        // Update variable references for parameters
        var rewriter = new VariableToParameterRewriter(signature.Parameters);
        statements = statements.Select(s => (StatementSyntax)rewriter.Visit(s)).ToList();

        return statements;
    }
}
```

## Edge Case Handlers

```csharp
public class EdgeCaseHandlers
{
    public class AsyncAwaitHandler
    {
        public bool CanHandle(SyntaxNode node)
        {
            return node.DescendantNodes().Any(n => n is AwaitExpressionSyntax);
        }

        public MethodSignature TransformSignature(MethodSignature original)
        {
            var transformed = original.Clone();
            transformed.IsAsync = true;

            // Wrap return type in Task if needed
            if (!original.ReturnType.StartsWith("Task"))
            {
                if (original.ReturnType == "void")
                {
                    transformed.ReturnType = "Task";
                }
                else
                {
                    transformed.ReturnType = $"Task<{original.ReturnType}>";
                }
            }

            return transformed;
        }
    }

    public class YieldReturnHandler
    {
        public bool CanHandle(SyntaxNode node)
        {
            return node.DescendantNodes().Any(n => n is YieldStatementSyntax);
        }

        public MethodSignature TransformSignature(MethodSignature original)
        {
            var transformed = original.Clone();

            // Transform return type to enumerable
            if (!original.ReturnType.StartsWith("IEnumerable"))
            {
                transformed.ReturnType = $"IEnumerable<{original.ReturnType}>";
            }

            return transformed;
        }
    }

    public class UsingStatementHandler
    {
        public TransformResult HandleUsing(
            IEnumerable<SyntaxNode> nodes,
            SemanticModel model)
        {
            var usingStatements = nodes
                .OfType<UsingStatementSyntax>()
                .ToList();

            if (!usingStatements.Any())
                return TransformResult.NoChange();

            // Check if entire using block is selected
            foreach (var usingStmt in usingStatements)
            {
                if (!nodes.Contains(usingStmt.Statement))
                {
                    return TransformResult.Error(
                        "Partial using statement selection. " +
                        "Include entire using block or extract inner statements only.");
                }
            }

            return TransformResult.Success();
        }
    }

    public class ExceptionHandler
    {
        public TransformResult HandleTryCatch(
            IEnumerable<SyntaxNode> nodes,
            SemanticModel model)
        {
            var tryStatements = nodes
                .OfType<TryStatementSyntax>()
                .ToList();

            foreach (var tryStmt in tryStatements)
            {
                // Check if entire try-catch is selected
                var allCatchesIncluded = tryStmt.Catches
                    .All(c => nodes.Contains(c));

                var finallyIncluded = tryStmt.Finally == null ||
                    nodes.Contains(tryStmt.Finally);

                if (!allCatchesIncluded || !finallyIncluded)
                {
                    return TransformResult.Warning(
                        "Partial try-catch selection may change exception handling behavior.");
                }
            }

            return TransformResult.Success();
        }
    }

    public class LockStatementHandler
    {
        public TransformResult HandleLock(
            IEnumerable<SyntaxNode> nodes,
            SemanticModel model)
        {
            var lockStatements = nodes
                .OfType<LockStatementSyntax>()
                .ToList();

            foreach (var lockStmt in lockStatements)
            {
                // Check if lock object is accessible
                var lockObject = model.GetSymbolInfo(lockStmt.Expression).Symbol;

                if (lockObject != null && lockObject.IsLocal())
                {
                    return TransformResult.Error(
                        "Cannot extract lock statement with local lock object. " +
                        "Pass lock object as parameter.");
                }
            }

            return TransformResult.Success();
        }
    }
}
```

## Test Strategy Implementation

```csharp
[TestFixture]
public class ExtractMethodTests
{
    private ExtractMethodService _service;

    [SetUp]
    public void Setup()
    {
        // Setup test workspace and services
        _service = CreateTestService();
    }

    [Test]
    [TestCase("SimpleStatements")]
    [TestCase("WithParameters")]
    [TestCase("WithReturnValue")]
    [TestCase("MultipleReturns")]
    public async Task ExtractMethod_BasicScenarios_Success(string scenario)
    {
        var testData = GetTestData(scenario);
        var result = await _service.ExtractMethodAsync(
            testData.FilePath,
            testData.Selection,
            testData.MethodName);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction.Method.Name, Is.EqualTo(testData.ExpectedName));
        Assert.That(result.Extraction.Parameters.Count, Is.EqualTo(testData.ExpectedParameterCount));
    }

    [Test]
    public async Task ExtractMethod_AsyncAwait_CreatesAsyncMethod()
    {
        var code = @"
class Service {
    async Task ProcessAsync() {
        var data = GetData();
        [|var result = await FetchAsync(data);
        await SaveAsync(result);|]
        LogComplete();
    }
}";

        var result = await ExtractFromCode(code);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction.Method.Signature, Contains.Substring("async Task"));
        Assert.That(result.Extraction.CallSite.Code, Contains.Substring("await"));
    }

    [Test]
    public async Task ExtractMethod_YieldReturn_CreatesIterator()
    {
        var code = @"
class Generator {
    IEnumerable<int> Generate() {
        var list = GetList();
        [|foreach(var item in list) {
            if(item > 0)
                yield return item * 2;
        }|]
    }
}";

        var result = await ExtractFromCode(code);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction.ReturnType, Contains.Substring("IEnumerable"));
        Assert.That(result.Extraction.Characteristics.ContainsYield, Is.True);
    }

    [Test]
    public async Task ExtractMethod_RefParameter_HandlesCorrectly()
    {
        var code = @"
class Calculator {
    void Calculate() {
        int value = 10;
        [|value *= 2;
        value += 5;|]
        Console.WriteLine(value);
    }
}";

        var result = await ExtractFromCode(code);

        Assert.That(result.Success, Is.True);
        var refParam = result.Extraction.Parameters.First(p => p.Name == "value");
        Assert.That(refParam.Modifier, Is.EqualTo("ref"));
    }

    [Test]
    [TestCase("GotoLabel", "GOTO_LABEL_PRESENT")]
    [TestCase("IncompleteSelection", "INCOMPLETE_SELECTION")]
    [TestCase("MultipleEntryPoints", "MULTIPLE_ENTRY_POINTS")]
    public async Task ExtractMethod_EdgeCases_ReturnsExpectedError(
        string scenario,
        string expectedError)
    {
        var testData = GetEdgeCaseData(scenario);
        var result = await _service.ExtractMethodAsync(
            testData.FilePath,
            testData.Selection);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error.Code, Is.EqualTo(expectedError));
    }

    [Test]
    public async Task ExtractMethod_ComplexScenario_FullIntegration()
    {
        var code = @"
class ComplexService {
    async Task<(bool success, string message)> ProcessDataAsync(string input) {
        try {
            var data = ParseInput(input);

            [|// Complex extraction
            if (data == null) {
                return (false, ""Invalid input"");
            }

            var validationResult = await ValidateAsync(data);
            if (!validationResult.IsValid) {
                LogError(validationResult.Error);
                return (false, validationResult.Error);
            }

            using (var connection = GetConnection()) {
                var result = await connection.ExecuteAsync(data);
                if (result.Success) {
                    await NotifySuccessAsync(result);
                    return (true, result.Message);
                }
            }

            return (false, ""Processing failed"");|]
        }
        catch (Exception ex) {
            LogException(ex);
            throw;
        }
    }
}";

        var result = await ExtractFromCode(code);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction.Characteristics.IsAsync, Is.True);
        Assert.That(result.Extraction.Characteristics.HasMultipleReturns, Is.True);
        Assert.That(result.Extraction.ReturnType, Contains.Substring("(bool"));
        Assert.That(result.Extraction.Parameters.Any(p => p.Name == "data"), Is.True);
        Assert.That(result.Warnings, Is.Not.Empty); // Should warn about using statement
    }
}
```

## Performance Benchmarks

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class ExtractMethodBenchmarks
{
    private ExtractMethodService _service;
    private string _largeFile;
    private Selection _selection;

    [GlobalSetup]
    public void Setup()
    {
        _service = CreateService();
        _largeFile = GenerateLargeFile(1000); // 1000 line file
        _selection = new Selection { StartLine = 450, EndLine = 550 };
    }

    [Benchmark]
    public async Task ExtractMethod_SimpleSelection()
    {
        await _service.ExtractMethodAsync(
            "simple.cs",
            new Selection { StartLine = 10, EndLine = 15 });
    }

    [Benchmark]
    public async Task ExtractMethod_ComplexDataFlow()
    {
        await _service.ExtractMethodAsync(
            "complex.cs",
            new Selection { StartLine = 50, EndLine = 100 });
    }

    [Benchmark]
    public async Task ExtractMethod_LargeFile()
    {
        await _service.ExtractMethodAsync(_largeFile, _selection);
    }

    [Benchmark]
    public async Task ExtractMethod_WithPreview()
    {
        await _service.ExtractMethodAsync(
            "preview.cs",
            new Selection { StartLine = 20, EndLine = 30 },
            options: new ExtractMethodOptions { Preview = true });
    }
}
```

## Success Metrics

1. **Correctness**: 100% of extractions preserve program semantics
2. **Performance**: 95% of extractions complete in < 500ms
3. **Coverage**: Handle 95% of real-world extraction scenarios
4. **Error Messages**: 100% of errors have actionable messages
5. **Edge Cases**: All 30+ identified edge cases handled properly

## Rollout Plan

### Week 1: Foundation
- Core extraction logic
- Basic parameter inference
- Simple test cases

### Week 2: Data Flow
- Complete data flow analysis
- Ref/out parameter detection
- Return value inference

### Week 3: Edge Cases
- Async/await handling
- Multiple returns
- Exception handling

### Week 4: Advanced Features
- Generic methods
- Iterator methods
- Complex control flow

### Week 5: Polish
- Performance optimization
- Error messages
- Preview mode
- Integration tests
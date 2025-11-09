# Extract Method Refactoring - Architecture Design

## Executive Summary

Extract Method is a critical refactoring operation that transforms selected code into a new method while preserving program semantics. This design provides a comprehensive solution handling 30+ edge cases with robust data flow analysis and Roslyn integration.

## 1. Architecture Overview

### Component Structure

```
┌─────────────────────────────────────────────────────┐
│              Extract Method Controller              │
│  • Orchestrates entire extraction process           │
│  • Coordinates validation, analysis, and generation │
└─────────────────────────────────────────────────────┘
                          │
        ┌─────────────────┴────────────────┬────────────────┐
        ▼                                   ▼                ▼
┌──────────────────┐          ┌──────────────────┐  ┌──────────────────┐
│ Selection        │          │ Data Flow        │  │ Code Generator   │
│ Analyzer         │          │ Analyzer         │  │                  │
│ • Valid range    │          │ • Parameters     │  │ • Method code    │
│ • Complete stmts │          │ • Return values  │  │ • Call site      │
│ • Control flow   │          │ • Captures       │  │ • Formatting     │
└──────────────────┘          └──────────────────┘  └──────────────────┘
        │                                   │                │
        └───────────────────┬───────────────┘                │
                           ▼                                 ▼
                ┌──────────────────┐              ┌──────────────────┐
                │ Semantic Model   │              │ Syntax Rewriter  │
                │ (Roslyn)         │              │ (Roslyn)         │
                └──────────────────┘              └──────────────────┘
```

### Data Flow Pipeline

```
1. Selection Input
   ├─ Line range (start, end)
   ├─ Character range (optional)
   └─ File path

2. Validation Phase
   ├─ Syntax completeness check
   ├─ Control flow analysis
   └─ Extract feasibility

3. Analysis Phase
   ├─ Data flow analysis (in/out variables)
   ├─ Control flow analysis (returns, breaks)
   └─ Semantic context (async, generic)

4. Generation Phase
   ├─ Method signature generation
   ├─ Method body transformation
   └─ Call site replacement

5. Application Phase
   ├─ Insert new method
   ├─ Replace original code
   └─ Format and cleanup
```

## 2. Selection Analysis Design

### Selection Specification

```csharp
public class ExtractionSelection
{
    public string FilePath { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int? StartColumn { get; set; }  // Optional for precise selection
    public int? EndColumn { get; set; }    // Optional for precise selection
    public SelectionGranularity Granularity { get; set; }
}

public enum SelectionGranularity
{
    Statements,      // Complete statements only (default)
    Expression,      // Allow expression extraction
    PartialBlock     // Allow partial block extraction with fixup
}
```

### Validation Rules

```csharp
public class SelectionValidator
{
    public ValidationResult Validate(SyntaxNode selection)
    {
        // 1. Completeness check
        if (!IsCompleteConstruct(selection))
            return ValidationResult.Error("Selection must contain complete statements");

        // 2. Single entry/exit validation
        var controlFlow = GetControlFlowAnalysis(selection);
        if (controlFlow.EntryPoints.Count > 1)
            return ValidationResult.Error("Selection has multiple entry points");

        // 3. Reachability check
        if (HasUnreachableCode(selection))
            return ValidationResult.Warning("Selection contains unreachable code");

        // 4. Special constructs
        if (ContainsGotoOrLabel(selection))
            return ValidationResult.Error("Cannot extract code with goto/labels");

        return ValidationResult.Success();
    }
}
```

## 3. Parameter Inference Algorithm

### Data Flow Analysis

```csharp
public class ParameterInferencer
{
    public InferenceResult InferParameters(
        SemanticModel model,
        DataFlowAnalysis dataFlow,
        SyntaxNode selection,
        SyntaxNode containingMethod)
    {
        var result = new InferenceResult();

        // Phase 1: Identify input parameters
        var variablesUsed = dataFlow.DataFlowsIn
            .Where(v => !v.IsThis && !v.IsImplicitlyDeclared);

        foreach (var variable in variablesUsed)
        {
            var parameter = new ExtractedParameter
            {
                Name = variable.Name,
                Type = variable.Type,
                Modifier = DetermineModifier(variable, dataFlow)
            };
            result.Parameters.Add(parameter);
        }

        // Phase 2: Identify return values
        var variablesAssigned = dataFlow.DataFlowsOut
            .Where(v => dataFlow.WrittenInside.Contains(v));

        if (variablesAssigned.Count() == 1)
        {
            // Single return value
            result.ReturnType = variablesAssigned.First().Type;
            result.ReturnVariable = variablesAssigned.First().Name;
        }
        else if (variablesAssigned.Count() > 1)
        {
            // Multiple values - use out parameters or tuple
            if (UseOutParameters(variablesAssigned))
            {
                foreach (var variable in variablesAssigned)
                {
                    var param = result.Parameters
                        .FirstOrDefault(p => p.Name == variable.Name);
                    if (param != null)
                        param.Modifier = ParameterModifier.Out;
                    else
                        result.Parameters.Add(new ExtractedParameter
                        {
                            Name = variable.Name,
                            Type = variable.Type,
                            Modifier = ParameterModifier.Out
                        });
                }
                result.ReturnType = "void";
            }
            else
            {
                // Use tuple return
                result.ReturnType = GenerateTupleType(variablesAssigned);
                result.ReturnVariables = variablesAssigned.Select(v => v.Name).ToList();
            }
        }

        // Phase 3: Handle special cases
        HandleAsyncContext(result, selection);
        HandleGenericContext(result, containingMethod);
        HandleRefParameters(result, dataFlow);

        return result;
    }

    private ParameterModifier DetermineModifier(
        ISymbol variable,
        DataFlowAnalysis dataFlow)
    {
        if (dataFlow.WrittenInside.Contains(variable) &&
            dataFlow.ReadOutside.Contains(variable))
        {
            return ParameterModifier.Ref;
        }
        return ParameterModifier.None;
    }
}
```

## 4. Edge Case Handling Matrix

### Control Flow Edge Cases

| Edge Case | Detection | Handling Strategy |
|-----------|-----------|-------------------|
| Multiple returns | `ControlFlowAnalysis.ExitPoints > 1` | Transform to single exit with result variable |
| Early returns | Return statements not at end | Wrap in result tracking pattern |
| Yield return | Contains `YieldStatementSyntax` | Extract as iterator method |
| Async/await | Contains `AwaitExpressionSyntax` | Extract as async method |
| Exception handling | Try/catch in selection | Include entire try/catch or extract inner logic |
| Using statements | Contains `UsingStatementSyntax` | Extract with using or move resource management |
| Lock statements | Contains `LockStatementSyntax` | Extract with lock or synchronize at call site |
| Goto/labels | Contains `GotoStatementSyntax` | Reject extraction |
| Switch expressions | Modern C# switch | Extract as expression-bodied method |
| Local functions | Contains local function | Move to class level or keep nested |

### Variable Scope Edge Cases

| Edge Case | Detection | Handling Strategy |
|-----------|-----------|-------------------|
| Captured variables | Closure analysis | Pass as parameters or use delegate |
| Out variables | `out var` declarations | Declare before extraction |
| Pattern variables | Pattern matching vars | Extract entire pattern or declare outside |
| Tuple deconstruction | `(var a, var b) = ...` | Pass tuple or individual parameters |
| Ref locals | `ref var` | Pass as ref parameter |
| Anonymous types | `var x = new { ... }` | Generate named type or use dynamic |
| LINQ variables | Query comprehension vars | Extract entire query |
| Loop variables | For/foreach variables | Pass as parameters if needed |

### Type System Edge Cases

| Edge Case | Detection | Handling Strategy |
|-----------|-----------|-------------------|
| Generic type parameters | Method has type parameters | Propagate to extracted method |
| Constraints | Generic constraints | Copy constraints to new method |
| Dynamic types | Uses `dynamic` | Preserve dynamic typing |
| Nullable reference types | C# 8.0+ nullable annotations | Preserve nullability |
| ValueTuple returns | Multiple return values | Use named tuples |
| Anonymous delegates | Lambda expressions | Extract containing expression |
| Extension method context | Uses `this` parameter | Make instance method or pass explicit |

## 5. Method Signature Generation

### Signature Builder

```csharp
public class MethodSignatureBuilder
{
    public MethodSignature Build(ExtractionContext context)
    {
        var signature = new MethodSignature
        {
            Name = GenerateName(context),
            Accessibility = DetermineAccessibility(context),
            Modifiers = DetermineModifiers(context),
            ReturnType = context.InferredReturnType,
            Parameters = context.InferredParameters,
            TypeParameters = context.TypeParameters,
            Constraints = context.TypeConstraints
        };

        // Special modifiers
        if (context.ContainsAwait)
            signature.Modifiers.Add("async");

        if (context.CanBeStatic)
            signature.Modifiers.Add("static");

        if (context.ContainsYield)
        {
            // Transform return type for iterator
            signature.ReturnType = WrapInEnumerable(signature.ReturnType);
        }

        return signature;
    }

    private string GenerateName(ExtractionContext context)
    {
        // Smart name generation based on content
        if (context.UserProvidedName != null)
            return ValidateName(context.UserProvidedName);

        // Analyze selection for naming hints
        var verbs = ExtractVerbs(context.Selection);
        var nouns = ExtractNouns(context.Selection);

        return CombineToMethodName(verbs, nouns);
    }

    private AccessibilityLevel DetermineAccessibility(ExtractionContext context)
    {
        // Default to most restrictive
        if (context.CalledFromOtherClasses)
            return AccessibilityLevel.Public;
        if (context.CalledFromDerivedClasses)
            return AccessibilityLevel.Protected;
        if (context.CalledFromAssembly)
            return AccessibilityLevel.Internal;
        return AccessibilityLevel.Private;
    }
}
```

## 6. Code Generation Templates

### Extracted Method Template

```csharp
public class ExtractedMethodGenerator
{
    public string GenerateMethod(MethodSignature signature, string body)
    {
        var template = new StringBuilder();

        // XML documentation
        template.AppendLine("/// <summary>");
        template.AppendLine($"/// Extracted from {signature.OriginalLocation}");
        template.AppendLine("/// </summary>");

        // Method signature
        var modifiers = string.Join(" ", signature.Modifiers);
        template.Append($"{signature.Accessibility} {modifiers} {signature.ReturnType} {signature.Name}");

        // Type parameters
        if (signature.TypeParameters.Any())
        {
            template.Append($"<{string.Join(", ", signature.TypeParameters)}>");
        }

        // Parameters
        template.Append("(");
        template.Append(string.Join(", ", signature.Parameters.Select(FormatParameter)));
        template.Append(")");

        // Constraints
        foreach (var constraint in signature.Constraints)
        {
            template.Append($" where {constraint}");
        }

        // Body
        template.AppendLine();
        template.AppendLine("{");
        template.Append(IndentBody(body));
        template.AppendLine("}");

        return template.ToString();
    }
}
```

### Call Site Template

```csharp
public class CallSiteGenerator
{
    public string GenerateCallSite(MethodSignature signature, CallContext context)
    {
        var call = new StringBuilder();

        // Handle return value
        if (signature.ReturnType != "void")
        {
            if (context.ReturnVariables.Count == 1)
            {
                call.Append($"var {context.ReturnVariables[0]} = ");
            }
            else if (context.ReturnVariables.Count > 1)
            {
                // Tuple deconstruction
                var vars = string.Join(", ", context.ReturnVariables);
                call.Append($"({vars}) = ");
            }
        }

        // Method call
        if (!signature.IsStatic && context.NeedsThisQualifier)
        {
            call.Append("this.");
        }

        call.Append(signature.Name);

        // Type arguments (if generic)
        if (context.TypeArguments.Any())
        {
            call.Append($"<{string.Join(", ", context.TypeArguments)}>");
        }

        // Arguments
        call.Append("(");
        var args = context.Arguments.Select(a => FormatArgument(a));
        call.Append(string.Join(", ", args));
        call.Append(");");

        return call.ToString();
    }
}
```

## 7. Implementation Phases

### Phase 1: MVP (Week 1)
- Basic statement extraction
- Simple parameter inference
- Single return value
- No async/generic support
- Basic name generation

### Phase 2: Advanced Control Flow (Week 2)
- Multiple return handling
- Early return transformation
- Exception handling
- Using/lock statements
- Async/await support

### Phase 3: Complex Types (Week 3)
- Generic method extraction
- Type parameter inference
- Constraint propagation
- Anonymous type handling
- Tuple returns

### Phase 4: Edge Cases (Week 4)
- Yield return iterators
- Ref/out parameters
- Pattern matching
- LINQ expressions
- Local functions

### Phase 5: Polish (Week 5)
- Smart naming
- Formatting preservation
- Comment migration
- Preview generation
- Undo support

## 8. Roslyn API Integration

### Key APIs

```csharp
// Data flow analysis
var dataFlow = model.AnalyzeDataFlow(selection);

// Control flow analysis
var controlFlow = model.AnalyzeControlFlow(selection);

// Symbol information
var symbols = model.GetSymbolInfo(node);

// Type inference
var typeInfo = model.GetTypeInfo(expression);

// Syntax rewriting
var rewriter = new ExtractMethodRewriter(extractedMethod);
var newRoot = rewriter.Visit(root);

// Document updates
var newDocument = document.WithSyntaxRoot(newRoot);
```

### Custom Rewriter

```csharp
public class ExtractMethodRewriter : CSharpSyntaxRewriter
{
    private readonly TextSpan _selection;
    private readonly MethodDeclarationSyntax _extractedMethod;
    private readonly ExpressionStatementSyntax _methodCall;

    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (ContainsSelection(node))
        {
            // Replace selection with method call
            var newBody = ReplaceSelectionWithCall(node.Body);

            // Insert extracted method after current method
            var updatedNode = node.WithBody(newBody);

            return SyntaxFactory.List(new[] {
                updatedNode,
                _extractedMethod
            });
        }

        return base.VisitMethodDeclaration(node);
    }
}
```

## 9. Testing Strategy

### Test Categories

1. **Basic Extraction Tests**
   - Simple statement extraction
   - Expression extraction
   - Multi-statement extraction

2. **Parameter Tests**
   - No parameters
   - Value parameters
   - Ref/out parameters
   - Multiple return values

3. **Control Flow Tests**
   - Single return
   - Multiple returns
   - Early returns
   - Exception paths

4. **Async Tests**
   - Async method extraction
   - Await preservation
   - Task return types

5. **Generic Tests**
   - Generic method extraction
   - Type parameter inference
   - Constraint preservation

6. **Edge Case Tests**
   - All 30+ edge cases
   - Error scenarios
   - Boundary conditions

### Test Example

```csharp
[Test]
public async Task ExtractMethod_WithAsyncAwait_CreatesAsyncMethod()
{
    var code = @"
class C {
    async Task M() {
        var x = 1;
        [|var result = await GetDataAsync();
        ProcessData(result);|]
        Console.WriteLine(x);
    }
}";

    var expected = @"
class C {
    async Task M() {
        var x = 1;
        await ExtractedMethod();
        Console.WriteLine(x);
    }

    private async Task ExtractedMethod() {
        var result = await GetDataAsync();
        ProcessData(result);
    }
}";

    await VerifyExtraction(code, expected);
}
```

## 10. Error Handling

### User-Friendly Messages

```csharp
public class ExtractionError
{
    public static readonly ExtractionError GotoLabel = new(
        "Cannot extract code containing goto statements or labels",
        "The selected code contains control flow that cannot be extracted");

    public static readonly ExtractionError IncompleteSelection = new(
        "Selection must contain complete statements",
        "Please adjust your selection to include entire statements");

    public static readonly ExtractionError MultipleEntryPoints = new(
        "Selection has multiple entry points",
        "The selected code is entered from multiple locations");
}
```

## 11. Performance Considerations

- **Lazy Analysis**: Only perform deep analysis when needed
- **Caching**: Cache semantic model queries
- **Incremental**: Support incremental compilation updates
- **Cancellation**: Support cancellation tokens throughout

## 12. Future Enhancements

1. **Extract to Interface**: Extract as interface method
2. **Extract to Base Class**: Move to base class
3. **Extract to Extension Method**: Convert to extension
4. **Extract and Generalize**: Make method more generic
5. **Extract with Tests**: Generate unit tests
6. **Batch Extraction**: Extract multiple selections
7. **Cross-File Extraction**: Extract to different file
8. **Extract Chain**: Extract multiple related methods
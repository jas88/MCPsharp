using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MCPsharp.Models.Refactoring;
using System.Text;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for extracting methods from selected code using Roslyn
/// MVP: Handles simple statement extraction with basic parameter inference
/// </summary>
public class ExtractMethodService
{
    private readonly RoslynWorkspace _workspace;

    public ExtractMethodService(RoslynWorkspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Extract method from selected code range
    /// </summary>
    public async Task<ExtractMethodResult> ExtractMethodAsync(
        ExtractMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Get document and validate
            var document = _workspace.GetDocument(request.FilePath);
            if (document == null)
            {
                return ExtractMethodResult.CreateError("Document not found: " + request.FilePath);
            }

            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (syntaxRoot == null)
            {
                return ExtractMethodResult.CreateError("Unable to get syntax root for document");
            }

            if (semanticModel == null)
            {
                return ExtractMethodResult.CreateError("Unable to get semantic model for document");
            }

            // Validate request parameters
            if (request.StartLine < 1 || request.EndLine < 1)
            {
                return ExtractMethodResult.CreateError("Line numbers must be greater than 0");
            }

            if (request.StartLine > request.EndLine)
            {
                return ExtractMethodResult.CreateError("Start line cannot be greater than end line");
            }

            // Validate line numbers are within document bounds
            var lineCount = syntaxRoot.GetText().Lines.Count;
            if (request.EndLine > lineCount)
            {
                return ExtractMethodResult.CreateError($"Line numbers exceed document length (document has {lineCount} lines)");
            }

            // Step 2: Get selected statements
            var selection = GetTextSpan(syntaxRoot, request);
            var selectedNodes = GetSelectedStatements(syntaxRoot, selection);

            if (!selectedNodes.Any())
            {
                return ExtractMethodResult.CreateError(
                    "No complete statements selected",
                    "INCOMPLETE_SELECTION",
                    new List<string> { "Select complete statement(s) within a method" });
            }

            // Step 3: Validate selection
            var validation = ValidateSelection(selectedNodes, semanticModel);
            if (!validation.IsValid)
            {
                return ExtractMethodResult.CreateError(validation.ErrorMessage!, validation.ErrorCode);
            }

            // Step 4: Analyze data flow
            var dataFlow = await AnalyzeDataFlowAsync(
                selectedNodes,
                semanticModel,
                validation.ContainingMethod!,
                cancellationToken);

            // Step 5: Generate method name
            var methodName = request.MethodName ?? GenerateMethodName(selectedNodes);

            // Step 6: Infer parameters and return type
            var parameters = InferParameters(dataFlow, semanticModel);
            var returnInfo = InferReturnType(dataFlow, semanticModel, selectedNodes);

            // Step 7: Detect method characteristics
            var characteristics = DetectCharacteristics(selectedNodes, dataFlow);

            // Step 8: Generate method signature
            var accessibility = request.Accessibility ?? "private";
            var isStatic = request.MakeStatic ?? CanBeStatic(dataFlow);

            var signature = GenerateSignature(
                methodName,
                accessibility,
                isStatic,
                characteristics,
                parameters,
                returnInfo.returnType);

            // Step 9: Generate method body
            var methodBody = GenerateMethodBody(selectedNodes, returnInfo);

            // Step 10: Generate call site
            var callSite = GenerateCallSite(
                methodName,
                parameters,
                returnInfo,
                isStatic);

            // Step 11: Create result
            var extractedMethod = new ExtractedMethodInfo
            {
                Method = new MethodInfo
                {
                    Name = methodName,
                    Signature = signature,
                    Body = methodBody,
                    Location = GetInsertLocation(validation.ContainingMethod!)
                },
                CallSite = new CallSiteInfo
                {
                    Code = callSite,
                    Location = new LocationInfo
                    {
                        FilePath = request.FilePath,
                        Line = request.StartLine,
                        Column = request.StartColumn ?? 1
                    }
                },
                Parameters = parameters,
                ReturnType = returnInfo.returnType,
                ReturnVariables = returnInfo.returnVariables,
                Characteristics = characteristics
            };

            // Step 12: Generate preview if requested
            PreviewInfo? preview = null;
            if (request.Preview)
            {
                preview = await GeneratePreviewAsync(
                    document,
                    validation.ContainingMethod!,
                    selectedNodes,
                    extractedMethod,
                    cancellationToken);
            }
            else
            {
                // Apply the extraction
                await ApplyExtractionAsync(
                    document,
                    validation.ContainingMethod!,
                    selectedNodes,
                    extractedMethod,
                    cancellationToken);
            }

            return ExtractMethodResult.CreateSuccess(extractedMethod, preview);
        }
        catch (Exception ex)
        {
            return ExtractMethodResult.CreateError($"Extraction failed: {ex.Message}");
        }
    }

    private TextSpan GetTextSpan(SyntaxNode root, ExtractMethodRequest request)
    {
        var text = root.GetText();
        if (text == null)
        {
            throw new InvalidOperationException("Unable to get text from syntax root");
        }

        // Validate line indices
        if (request.StartLine < 1 || request.EndLine < 1 || request.StartLine > text.Lines.Count || request.EndLine > text.Lines.Count)
        {
            throw new ArgumentException($"Invalid line range: {request.StartLine}-{request.EndLine}, document has {text.Lines.Count} lines");
        }

        var startLine = text.Lines[request.StartLine - 1];
        var endLine = text.Lines[request.EndLine - 1];

        var startColumn = Math.Max(1, request.StartColumn ?? 1);
        var endColumn = request.EndColumn ?? endLine.Span.Length;

        // Validate column positions
        if (startColumn < 1 || startColumn > startLine.Span.Length + 1)
        {
            throw new ArgumentException($"Invalid start column: {startColumn} for line {request.StartLine} (line length: {startLine.Span.Length + 1})");
        }

        if (endColumn < 1 || endColumn > endLine.Span.Length + 1)
        {
            throw new ArgumentException($"Invalid end column: {endColumn} for line {request.EndLine} (line length: {endLine.Span.Length + 1})");
        }

        var start = startLine.Start + startColumn - 1;
        var end = endLine.Start + endColumn;

        // Validate final span bounds
        if (start < 0 || start > text.Length || end < 0 || end > text.Length || start > end)
        {
            throw new ArgumentException($"Invalid text span calculated: start={start}, end={end}, text length={text.Length}");
        }

        return TextSpan.FromBounds(start, end);
    }

    private List<StatementSyntax> GetSelectedStatements(SyntaxNode root, TextSpan selection)
    {
        var statements = root.DescendantNodes()
            .OfType<StatementSyntax>()
            .Where(s => selection.Contains(s.Span))
            .ToList();

        // Filter to get only top-level statements within selection
        var result = new List<StatementSyntax>();
        foreach (var stmt in statements)
        {
            var isNested = statements.Any(other =>
                other != stmt &&
                other.Span.Contains(stmt.Span));

            if (!isNested)
            {
                result.Add(stmt);
            }
        }

        return result;
    }

    private (bool IsValid, string? ErrorMessage, string? ErrorCode, MethodDeclarationSyntax? ContainingMethod)
        ValidateSelection(List<StatementSyntax> statements, SemanticModel semanticModel)
    {
        if (statements.Count == 0)
        {
            return (false, "No statements selected", "EMPTY_SELECTION", null);
        }

        // Find containing method
        var containingMethod = statements.First()
            .Ancestors()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();

        if (containingMethod == null)
        {
            return (false, "Selection must be within a method", "NOT_IN_METHOD", null);
        }

        // Check for goto/labels (MVP: reject these)
        var hasGoto = statements.Any(s =>
            s.DescendantNodesAndSelf()
                .Any(n => n is GotoStatementSyntax or LabeledStatementSyntax));

        if (hasGoto)
        {
            return (false,
                "Cannot extract code containing goto statements or labels",
                "GOTO_LABEL_PRESENT",
                null);
        }

        // Check control flow (basic check)
        try
        {
            var firstStmt = statements.First();
            var lastStmt = statements.Last();
            var controlFlow = semanticModel.AnalyzeControlFlow(firstStmt, lastStmt);
            if (controlFlow == null)
                throw new InvalidOperationException("Control flow analysis returned null");

            if (!controlFlow.Succeeded)
            {
                return (false, "Unable to analyze control flow", "CONTROL_FLOW_ERROR", null);
            }

            if (controlFlow.EntryPoints.Length > 1)
            {
                return (false,
                    "Selection has multiple entry points - cannot extract",
                    "MULTIPLE_ENTRY_POINTS",
                    null);
            }
        }
        catch
        {
            // Control flow analysis failed - continue anyway for MVP
        }

        return (true, null, null, containingMethod);
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<DataFlowAnalysisResult> AnalyzeDataFlowAsync(
        List<StatementSyntax> statements,
        SemanticModel semanticModel,
        MethodDeclarationSyntax containingMethod,
        CancellationToken cancellationToken)
    {
        var result = new DataFlowAnalysisResult();

        try
        {
            var firstStmt = statements.First();
            var lastStmt = statements.Last();

            var dataFlow = semanticModel.AnalyzeDataFlow(firstStmt, lastStmt);
            if (dataFlow == null)
                return result;

            if (!dataFlow.Succeeded)
            {
                return result;
            }

            // Input parameters: variables read that are declared outside
            result.InputParameters.AddRange(
                dataFlow.DataFlowsIn
                    .Where(s => !s.IsImplicitlyDeclared &&
                               s.Kind != Microsoft.CodeAnalysis.SymbolKind.Property &&
                               s.Kind != Microsoft.CodeAnalysis.SymbolKind.Field &&
                               !IsThisParameter(s)));

            // Output variables: variables written inside and read outside
            var writtenAndReadOutside = dataFlow.WrittenInside
                .Intersect(dataFlow.ReadOutside)
                .Where(s => !dataFlow.DataFlowsIn.Contains(s));

            result.OutputVariables.AddRange(writtenAndReadOutside);

            // Ref parameters: variables read and written inside, also read outside
            var refCandidates = dataFlow.ReadInside
                .Intersect(dataFlow.WrittenInside)
                .Intersect(dataFlow.ReadOutside)
                .Where(s => dataFlow.DataFlowsIn.Contains(s));

            result.RefParameters.AddRange(refCandidates);

            // Captured variables
            result.CapturedVariables.AddRange(dataFlow.Captured);

            // Detect multiple returns
            var returnStatements = statements
                .SelectMany(s => s.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>())
                .ToList();

            var hasMultipleReturns = returnStatements.Count > 1;
            var hasEarlyReturns = returnStatements.Any() &&
                returnStatements.Last() != statements.Last().DescendantNodesAndSelf().LastOrDefault();

            // Create new result with all properties set
            return new DataFlowAnalysisResult
            {
                InputParameters = result.InputParameters,
                OutputVariables = result.OutputVariables,
                RefParameters = result.RefParameters,
                CapturedVariables = result.CapturedVariables,
                HasMultipleReturns = hasMultipleReturns,
                HasEarlyReturns = hasEarlyReturns
            };
        }
        catch
        {
            // Data flow analysis failed - return empty result
        }

        return result;
    }

    private bool IsThisParameter(ISymbol symbol)
    {
        return symbol is IParameterSymbol param && param.IsThis;
    }

    private List<ParameterInfo> InferParameters(
        DataFlowAnalysisResult dataFlow,
        SemanticModel semanticModel)
    {
        var parameters = new List<ParameterInfo>();

        // Add input parameters
        foreach (var symbol in dataFlow.InputParameters)
        {
            var modifier = dataFlow.RefParameters.Contains(symbol) ? "ref" : null;

            parameters.Add(new ParameterInfo
            {
                Name = symbol.Name,
                Type = GetTypeDisplayString(symbol),
                Modifier = modifier
            });
        }

        // Add out parameters for output variables
        foreach (var symbol in dataFlow.OutputVariables)
        {
            if (!dataFlow.InputParameters.Contains(symbol))
            {
                parameters.Add(new ParameterInfo
                {
                    Name = symbol.Name,
                    Type = GetTypeDisplayString(symbol),
                    Modifier = "out"
                });
            }
        }

        return parameters;
    }

    private (string returnType, List<string>? returnVariables) InferReturnType(
        DataFlowAnalysisResult dataFlow,
        SemanticModel semanticModel,
        List<StatementSyntax> statements)
    {
        var outputVars = dataFlow.OutputVariables
            .Where(s => !dataFlow.InputParameters.Contains(s))
            .ToList();

        // Check if this is an async method that returns the result of await expressions
        var awaitExpressions = statements
            .SelectMany(s => s.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>())
            .ToList();

        if (awaitExpressions.Any())
        {
            // For async methods with await expressions, we need to check if there's a clear return value
            var lastStatement = statements.LastOrDefault();

            if (lastStatement is LocalDeclarationStatementSyntax localDecl)
            {
                // The last statement declares a variable - check if it comes from an await
                var variableName = localDecl.Declaration.Variables.FirstOrDefault()?.Identifier.Text;
                var initializer = localDecl.Declaration.Variables.FirstOrDefault()?.Initializer;

                if (initializer?.Value is AwaitExpressionSyntax awaitExpr)
                {
                    var awaitTypeInfo = semanticModel.GetTypeInfo(awaitExpr);
                    var awaitedType = awaitTypeInfo.Type;

                    if (awaitedType != null)
                    {
                        var typeString = awaitedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                        // For async methods, we want the awaited type, not the Task<T>
                        if (typeString.StartsWith("Task<"))
                        {
                            typeString = typeString.Substring(5, typeString.Length - 6); // Remove "Task<" and ">"
                        }

                        if (!string.IsNullOrEmpty(variableName))
                        {
                            return (typeString, new List<string> { variableName });
                        }
                        return (typeString, null);
                    }
                }
            }

            // If we have await expressions but no clear return variable,
            // check the last await expression to infer type
            var lastAwait = awaitExpressions.LastOrDefault();
            if (lastAwait != null)
            {
                var awaitTypeInfo = semanticModel.GetTypeInfo(lastAwait);
                var awaitedType = awaitTypeInfo.Type;

                if (awaitedType != null)
                {
                    var typeString = awaitedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

                    // For async methods, we want the awaited type, not the Task<T>
                    if (typeString.StartsWith("Task<"))
                    {
                        typeString = typeString.Substring(5, typeString.Length - 6); // Remove "Task<" and ">"
                    }

                    // For async methods without clear return variable, return the awaited type
                    return (typeString, null);
                }
            }

            // Default for async methods - void (Task)
            return ("void", null);
        }

        if (outputVars.Count == 0)
        {
            return ("void", null);
        }
        else if (outputVars.Count == 1)
        {
            return (GetTypeDisplayString(outputVars[0]), new List<string> { outputVars[0].Name });
        }
        else
        {
            // Multiple return values - use tuple (MVP: simplified)
            var tupleElements = outputVars
                .Select(s => $"{GetTypeDisplayString(s)} {s.Name}")
                .ToList();

            var tupleType = $"({string.Join(", ", tupleElements)})";
            var varNames = outputVars.Select(s => s.Name).ToList();

            return (tupleType, varNames);
        }
    }

    private string GetTypeDisplayString(ISymbol symbol)
    {
        if (symbol is ILocalSymbol local)
        {
            return local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }
        if (symbol is IParameterSymbol param)
        {
            return param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return "object";
    }

    private MethodCharacteristics DetectCharacteristics(
        List<StatementSyntax> statements,
        DataFlowAnalysisResult dataFlow)
    {
        if (statements == null || !statements.Any())
        {
            return new MethodCharacteristics
            {
                IsAsync = false,
                IsStatic = false,
                IsGeneric = false,
                HasMultipleReturns = false,
                HasEarlyReturns = false,
                CapturesVariables = false,
                ContainsAwait = false,
                ContainsYield = false
            };
        }

        var hasAwait = statements.Any(s =>
            s?.DescendantNodesAndSelf().Any(n => n is AwaitExpressionSyntax) == true);

        var hasYield = statements.Any(s =>
            s?.DescendantNodesAndSelf().Any(n => n is YieldStatementSyntax) == true);

        return new MethodCharacteristics
        {
            IsAsync = hasAwait,
            IsStatic = false, // Will be set by CanBeStatic
            IsGeneric = false, // MVP: no generic support yet
            HasMultipleReturns = dataFlow?.HasMultipleReturns ?? false,
            HasEarlyReturns = dataFlow?.HasEarlyReturns ?? false,
            CapturesVariables = dataFlow?.CapturedVariables?.Any() ?? false,
            ContainsAwait = hasAwait,
            ContainsYield = hasYield
        };
    }

    private bool CanBeStatic(DataFlowAnalysisResult dataFlow)
    {
        // Can be static if no instance members are accessed
        return !dataFlow.CapturedVariables.Any();
    }

    private string GenerateMethodName(List<StatementSyntax> statements)
    {
        // MVP: simple default name
        return "ExtractedMethod";
    }

    private string GenerateSignature(
        string methodName,
        string accessibility,
        bool isStatic,
        MethodCharacteristics characteristics,
        List<ParameterInfo> parameters,
        string returnType)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = "ExtractedMethod";
        }

        if (string.IsNullOrWhiteSpace(accessibility))
        {
            accessibility = "private";
        }

        if (string.IsNullOrWhiteSpace(returnType))
        {
            returnType = "void";
        }

        var sb = new StringBuilder();

        sb.Append(accessibility.Trim());

        if (isStatic)
        {
            sb.Append(" static");
        }

        if (characteristics?.IsAsync == true)
        {
            sb.Append(" async");

            // Wrap return type in Task if not already
            if (returnType == "void")
            {
                returnType = "Task";
            }
            else if (!returnType.StartsWith("Task"))
            {
                returnType = $"Task<{returnType}>";
            }
        }

        if (characteristics?.ContainsYield == true)
        {
            // Wrap in IEnumerable
            if (!returnType.StartsWith("IEnumerable"))
            {
                returnType = $"IEnumerable<{returnType}>";
            }
        }

        sb.Append($" {returnType} {methodName}(");

        // Add parameters safely
        if (parameters != null && parameters.Any())
        {
            var paramStrings = parameters.Select(p =>
            {
                var modifier = !string.IsNullOrWhiteSpace(p.Modifier) ? p.Modifier.Trim() + " " : "";
                var type = !string.IsNullOrWhiteSpace(p.Type) ? p.Type : "object";
                var name = !string.IsNullOrWhiteSpace(p.Name) ? p.Name : "param";
                return $"{modifier}{type} {name}";
            });

            sb.Append(string.Join(", ", paramStrings));
        }

        sb.Append(")");

        return sb.ToString();
    }

    private string GenerateMethodBody(
        List<StatementSyntax> statements,
        (string returnType, List<string>? returnVariables) returnInfo)
    {
        var sb = new StringBuilder();

        foreach (var stmt in statements)
        {
            sb.AppendLine(stmt.ToFullString());
        }

        // Add return statement if needed
        if (returnInfo.returnVariables != null && returnInfo.returnVariables.Count > 0)
        {
            if (returnInfo.returnVariables.Count == 1)
            {
                sb.AppendLine($"return {returnInfo.returnVariables[0]};");
            }
            else
            {
                var tupleReturn = string.Join(", ", returnInfo.returnVariables);
                sb.AppendLine($"return ({tupleReturn});");
            }
        }

        return sb.ToString();
    }

    private string GenerateCallSite(
        string methodName,
        List<ParameterInfo> parameters,
        (string returnType, List<string>? returnVariables) returnInfo,
        bool isStatic)
    {
        var sb = new StringBuilder();

        // Handle return value assignment
        if (returnInfo.returnType != "void" && returnInfo.returnVariables != null)
        {
            if (returnInfo.returnVariables.Count == 1)
            {
                sb.Append($"var {returnInfo.returnVariables[0]} = ");
            }
            else
            {
                var vars = string.Join(", ", returnInfo.returnVariables);
                sb.Append($"({vars}) = ");
            }
        }

        // Method call
        if (!isStatic)
        {
            // Instance method - no qualifier needed in same class
        }

        sb.Append($"{methodName}(");

        // Add arguments
        var args = parameters
            .Where(p => p.Modifier != "out") // out params don't need values passed
            .Select(p =>
            {
                var modifier = p.Modifier ?? "";
                if (!string.IsNullOrEmpty(modifier))
                {
                    modifier += " ";
                }
                return $"{modifier}{p.Name}";
            });

        sb.Append(string.Join(", ", args));
        sb.Append(");");

        return sb.ToString();
    }

    private LocationInfo GetInsertLocation(MethodDeclarationSyntax containingMethod)
    {
        var location = containingMethod.GetLocation();
        var lineSpan = location.GetLineSpan();

        return new LocationInfo
        {
            FilePath = lineSpan.Path,
            Line = lineSpan.EndLinePosition.Line + 2, // After method
            Column = 1
        };
    }

    private async Task<PreviewInfo> GeneratePreviewAsync(
        Document document,
        MethodDeclarationSyntax containingMethod,
        List<StatementSyntax> selectedStatements,
        ExtractedMethodInfo extraction,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (root == null)
        {
            return new PreviewInfo
            {
                OriginalCode = containingMethod.ToFullString(),
                ModifiedCode = "Error generating preview"
            };
        }

        var originalCode = containingMethod.ToFullString();

        // Generate modified method
        var modifiedMethod = GenerateModifiedMethod(
            containingMethod,
            selectedStatements,
            extraction);

        var modifiedCode = modifiedMethod;

        return new PreviewInfo
        {
            OriginalCode = originalCode,
            ModifiedCode = modifiedCode
        };
    }

    private string GenerateModifiedMethod(
        MethodDeclarationSyntax containingMethod,
        List<StatementSyntax> selectedStatements,
        ExtractedMethodInfo extraction)
    {
        var sb = new StringBuilder();

        // Original method with call site
        var methodBody = containingMethod.Body?.Statements ?? new SyntaxList<StatementSyntax>();

        sb.AppendLine(containingMethod.ToFullString()
            .Replace(
                string.Join("\n", selectedStatements.Select(s => s.ToFullString())),
                extraction.CallSite.Code));

        sb.AppendLine();

        // New extracted method
        sb.AppendLine($"{extraction.Method.Signature}");
        sb.AppendLine("{");
        sb.Append(extraction.Method.Body);
        sb.AppendLine("}");

        return sb.ToString();
    }

    private async Task ApplyExtractionAsync(
        Document document,
        MethodDeclarationSyntax containingMethod,
        List<StatementSyntax> selectedStatements,
        ExtractedMethodInfo extraction,
        CancellationToken cancellationToken)
    {
        // MVP: Preview only for now
        // Actual application would require more complex syntax rewriting
        await Task.CompletedTask;
    }
}

using System.Globalization;
using MCPsharp.Models.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Building the rewritten syntax tree (new method + call site), formatting and compiling it.
/// </summary>
public partial class ExtractMethodService
{
    private static readonly SyntaxAnnotation MethodAnnotation = new("MCPsharp.ExtractMethod.Method");
    private static readonly SyntaxAnnotation CallSiteAnnotation = new("MCPsharp.ExtractMethod.CallSite");

    private sealed class ExtractionPlan
    {
        public required Document Document { get; init; }
        public required SyntaxNode Root { get; init; }
        public required SemanticModel Model { get; init; }
        public required ExtractionSelection Selection { get; init; }
        public required ExtractionAnalysis Analysis { get; init; }
        public required string MethodName { get; init; }
        public required string Accessibility { get; init; }
        public required int SelectionStart { get; init; }

        public int Position => Selection.ContainingMember.SpanStart;

        public string NewLine => Root.DescendantTrivia().FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia)).ToFullString() is { Length: > 0 } nl
            ? nl
            : "\n";

        public string Display(ITypeSymbol? type) =>
            type == null ? "void" : type.ToMinimalDisplayString(Model, Position);
    }

    private sealed record RewriteOutcome(Document NewDocument, SyntaxNode NewRoot, SourceText NewText);

    private async Task<ExtractMethodResult> RewriteAsync(
        ExtractionPlan plan,
        ExtractMethodRequest request,
        CancellationToken ct)
    {
        var method = BuildMethod(plan);
        var callNodes = BuildCallSite(plan);
        if (method == null || callNodes == null)
        {
            return ExtractMethodResult.CreateError(
                "Unable to generate valid syntax for the extracted method", "INVALID_SIGNATURE");
        }

        var unformatted = plan.Document.WithSyntaxRoot(ReplaceSelection(plan, callNodes, method));
        // Line breaks the formatter adds (e.g. before constraint clauses) must match the file's style
        var options = (await unformatted.GetOptionsAsync(ct))
            .WithChangedOption(FormattingOptions.NewLine, LanguageNames.CSharp, plan.NewLine);
        var newDocument = await Formatter.FormatAsync(unformatted, Formatter.Annotation, options, ct);
        var outcome = new RewriteOutcome(
            newDocument,
            (await newDocument.GetSyntaxRootAsync(ct))!,
            await newDocument.GetTextAsync(ct));

        var newDiagnostics = await GetNewDiagnosticsAsync(plan, newDocument, ct);
        var filePath = plan.Document.FilePath ?? request.FilePath;
        var extraction = DescribeExtraction(plan, outcome.NewRoot, filePath);
        var errors = newDiagnostics.Where(d => d.Severity == nameof(DiagnosticSeverity.Error)).ToList();

        if (request.Preview)
        {
            var warnings = errors.Count == 0
                ? new List<WarningInfo>()
                : [new WarningInfo { Code = "COMPILATION_ERRORS", Message = "Applying this extraction would introduce compilation errors: " + string.Join("; ", errors) }];

            return ExtractMethodResult.CreateSuccess(extraction, BuildPreview(plan, outcome.NewRoot), warnings) with
            {
                FilePath = filePath,
                NewDiagnostics = newDiagnostics
            };
        }

        if (errors.Count > 0)
        {
            return ExtractMethodResult.CreateError(
                "Extraction refused: the rewritten file would not compile. " + string.Join("; ", errors),
                "COMPILATION_ERRORS",
                errors.Select(e => e.ToString()).ToList()) with
            {
                FilePath = filePath,
                NewDiagnostics = newDiagnostics
            };
        }

        return await ApplyAsync(plan, outcome, extraction, filePath, newDiagnostics, ct);
    }

    // ===== Generated method =====

    private static MethodDeclarationSyntax? BuildMethod(ExtractionPlan plan)
    {
        var analysis = plan.Analysis;
        var modifiers = plan.Accessibility
            + (analysis.IsStatic ? " static" : string.Empty)
            + (analysis.Characteristics.IsAsync ? " async" : string.Empty);
        var parameters = string.Join(", ", analysis.Inputs.Select(s =>
            RefPrefix(analysis, s) + plan.Display(TypeOf(s)) + " " + EscapeIdentifier(s.Name)));
        var constraints = string.Concat(analysis.TypeParameters
            .Select(t => ConstraintClause(t, plan))
            .Where(c => c != null)
            .Select(c => " " + c));

        var header = $"{modifiers} {plan.Display(analysis.ReturnType)} {plan.MethodName}{TypeParameterList(analysis)}({parameters}){constraints}{{}}";
        if (SyntaxFactory.ParseMemberDeclaration(header) is not MethodDeclarationSyntax parsed || parsed.ContainsDiagnostics)
        {
            return null;
        }

        var body = BuildMethodBody(plan);
        if (body == null)
        {
            return null;
        }

        var newLine = SyntaxFactory.EndOfLine(plan.NewLine);
        var block = SyntaxFactory.Block(
            SyntaxFactory.Token(SyntaxFactory.TriviaList(newLine), SyntaxKind.OpenBraceToken, SyntaxFactory.TriviaList(newLine)),
            SyntaxFactory.List(body),
            SyntaxFactory.Token(SyntaxTriviaList.Empty, SyntaxKind.CloseBraceToken, SyntaxFactory.TriviaList(newLine)));

        return parsed
            .WithBody(block)
            .WithLeadingTrivia(newLine)
            .WithAdditionalAnnotations(Formatter.Annotation, MethodAnnotation);
    }

    private static List<StatementSyntax>? BuildMethodBody(ExtractionPlan plan)
    {
        var analysis = plan.Analysis;
        var selection = plan.Selection;
        var declarations = analysis.LocalsToDeclare
            .Select(l => ParseGeneratedStatement($"{plan.Display(TypeOf(l))} {EscapeIdentifier(l.Name)};", plan.NewLine))
            .ToList();

        StatementSyntax? returnStatement = null;
        var body = new List<StatementSyntax>(declarations);
        if (selection.Expression != null)
        {
            returnStatement = ParseGeneratedStatement($"return {selection.Expression.WithoutTrivia().ToFullString()};", plan.NewLine);
        }
        else
        {
            var first = selection.Statements[0];
            var insideTrivia = first.GetLeadingTrivia().Where(t => t.SpanStart >= plan.SelectionStart);
            body.Add(first.WithLeadingTrivia(insideTrivia));
            body.AddRange(selection.Statements.Skip(1));
            EnsureTrailingNewLine(body, plan.NewLine);

            var returned = analysis.Outputs.Select(o => EscapeIdentifier(o.Name)).ToList();
            if (returned.Count > 0)
            {
                var value = returned.Count == 1 ? returned[0] : $"({string.Join(", ", returned)})";
                returnStatement = ParseGeneratedStatement($"return {value};", plan.NewLine);
            }
        }

        if (returnStatement != null)
        {
            body.Add(returnStatement);
            declarations.Add(returnStatement);
        }

        // Only the generated statements are new; the selected ones come from the parsed file
        return declarations.Exists(s => s.ContainsDiagnostics) ? null : body;
    }

    private static void EnsureTrailingNewLine(List<StatementSyntax> statements, string newLine)
    {
        var last = statements[^1];
        if (!last.GetTrailingTrivia().Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            statements[^1] = last.WithTrailingTrivia(last.GetTrailingTrivia().Add(SyntaxFactory.EndOfLine(newLine)));
        }
    }

    private static StatementSyntax ParseGeneratedStatement(string text, string newLine) =>
        SyntaxFactory.ParseStatement(text).WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));

    private static string TypeParameterList(ExtractionAnalysis analysis) =>
        analysis.TypeParameters.Count == 0
            ? string.Empty
            : "<" + string.Join(", ", analysis.TypeParameters.Select(t => EscapeIdentifier(t.Name))) + ">";

    private static string? ConstraintClause(ITypeParameterSymbol typeParameter, ExtractionPlan plan)
    {
        var parts = new List<string>();
        if (typeParameter.HasUnmanagedTypeConstraint)
        {
            parts.Add("unmanaged");
        }
        else if (typeParameter.HasValueTypeConstraint)
        {
            parts.Add("struct");
        }
        else if (typeParameter.HasReferenceTypeConstraint)
        {
            parts.Add(typeParameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
        }
        else if (typeParameter.HasNotNullConstraint)
        {
            parts.Add("notnull");
        }

        parts.AddRange(typeParameter.ConstraintTypes.Select(plan.Display));
        if (typeParameter.HasConstructorConstraint)
        {
            parts.Add("new()");
        }

        return parts.Count == 0 ? null : $"where {EscapeIdentifier(typeParameter.Name)} : {string.Join(", ", parts)}";
    }

    private static string RefPrefix(ExtractionAnalysis analysis, ISymbol symbol) =>
        analysis.RefInputs.Contains(symbol) ? "ref " : string.Empty;

    private static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    // ===== Call site =====

    private static List<SyntaxNode>? BuildCallSite(ExtractionPlan plan)
    {
        var analysis = plan.Analysis;
        var arguments = string.Join(", ", analysis.Inputs.Select(s => RefPrefix(analysis, s) + EscapeIdentifier(s.Name)));
        var invocation = (analysis.Characteristics.IsAsync ? "await " : string.Empty)
            + $"{plan.MethodName}{TypeParameterList(analysis)}({arguments})";

        if (plan.Selection.Expression != null)
        {
            var expression = SyntaxFactory.ParseExpression(invocation);
            return expression.ContainsDiagnostics ? null : [expression];
        }

        var statements = CallStatementTexts(analysis, invocation)
            .Select(t => SyntaxFactory.ParseStatement(t))
            .ToList();

        return statements.Exists(s => s.ContainsDiagnostics) ? null : [.. statements];
    }

    private static IEnumerable<string> CallStatementTexts(ExtractionAnalysis analysis, string invocation)
    {
        if (analysis.EndsWithReturn)
        {
            return analysis.ReturnsValue ? [$"return {invocation};"] : [$"{invocation};", "return;"];
        }

        var outputs = analysis.Outputs;
        string Target(ISymbol output) =>
            (analysis.OutputsDeclaredInside.Contains(output) ? "var " : string.Empty) + EscapeIdentifier(output.Name);

        return outputs.Count switch
        {
            0 => [$"{invocation};"],
            1 => [$"{Target(outputs[0])} = {invocation};"],
            _ when outputs.All(analysis.OutputsDeclaredInside.Contains) =>
                [$"var ({string.Join(", ", outputs.Select(o => EscapeIdentifier(o.Name)))}) = {invocation};"],
            _ => [$"({string.Join(", ", outputs.Select(Target))}) = {invocation};"]
        };
    }

    // ===== Tree surgery =====

    private static SyntaxNode ReplaceSelection(ExtractionPlan plan, List<SyntaxNode> callNodes, MethodDeclarationSyntax method)
    {
        var selection = plan.Selection;
        var tracked = plan.Root.TrackNodes(selection.Nodes.Append(selection.ContainingMember));

        SyntaxNode replaced;
        if (selection.Expression != null)
        {
            var current = tracked.GetCurrentNode(selection.Expression)!;
            replaced = tracked.ReplaceNode(current, callNodes[0]
                .WithTriviaFrom(current)
                .WithAdditionalAnnotations(Formatter.Annotation, CallSiteAnnotation));
        }
        else
        {
            replaced = ReplaceStatements(plan, tracked, callNodes.Cast<StatementSyntax>().ToList());
        }

        var member = replaced.GetCurrentNode(selection.ContainingMember)!;
        return replaced.InsertNodesAfter(member, [method]);
    }

    private static SyntaxNode ReplaceStatements(ExtractionPlan plan, SyntaxNode tracked, List<StatementSyntax> calls)
    {
        var statements = plan.Selection.Statements;
        var current = statements.Select(s => tracked.GetCurrentNode(s)!).ToList();
        var newLine = SyntaxFactory.EndOfLine(plan.NewLine);

        // Trivia before the selection (e.g. a comment above it) stays at the call site
        var outsideTrivia = statements[0].GetLeadingTrivia().Where(t => t.SpanStart < plan.SelectionStart);
        var lastTrailing = statements[^1].GetTrailingTrivia()
            .Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia) || t.IsKind(SyntaxKind.WhitespaceTrivia));

        var annotated = calls.Select((call, i) => call
                .WithLeadingTrivia(i == 0 ? SyntaxFactory.TriviaList(outsideTrivia) : SyntaxTriviaList.Empty)
                .WithTrailingTrivia(i == calls.Count - 1 ? SyntaxFactory.TriviaList(lastTrailing) : SyntaxFactory.TriviaList(newLine))
                .WithAdditionalAnnotations(Formatter.Annotation, CallSiteAnnotation))
            .ToList();

        var parent = current[0].Parent!;
        SyntaxNode? newParent = parent switch
        {
            BlockSyntax block => block.WithStatements(Splice(block.Statements, current, annotated)),
            SwitchSectionSyntax section => section.WithStatements(Splice(section.Statements, current, annotated)),
            _ => null
        };

        if (newParent != null)
        {
            return tracked.ReplaceNode(parent, newParent);
        }

        StatementSyntax replacement = annotated.Count == 1
            ? annotated[0]
            : SyntaxFactory.Block(annotated).WithTriviaFrom(current[0]).WithAdditionalAnnotations(Formatter.Annotation);
        return tracked.ReplaceNode(current[0], replacement);
    }

    private static SyntaxList<StatementSyntax> Splice(
        SyntaxList<StatementSyntax> list,
        List<StatementSyntax> removed,
        List<StatementSyntax> inserted)
    {
        var index = list.IndexOf(removed[0]);
        return SyntaxFactory.List(list.Take(index).Concat(inserted).Concat(list.Skip(index + removed.Count)));
    }

    // ===== Compilation check =====

    /// <summary>
    /// Errors and warnings in the rewritten document that were not already present before (compared
    /// as a multiset of id + message, so diagnostics that merely moved with the code are ignored).
    /// </summary>
    private static async Task<List<ExtractionDiagnostic>> GetNewDiagnosticsAsync(
        ExtractionPlan plan,
        Document newDocument,
        CancellationToken ct)
    {
        static bool Relevant(Diagnostic d) => d.Severity >= DiagnosticSeverity.Warning;
        static string Key(Diagnostic d) => d.Id + ":" + d.GetMessage(CultureInfo.InvariantCulture);

        var remaining = plan.Model.GetDiagnostics(cancellationToken: ct)
            .Where(Relevant)
            .GroupBy(Key)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var newModel = await newDocument.GetSemanticModelAsync(ct)
            ?? throw new InvalidOperationException("Unable to get semantic model for the rewritten document");

        var added = new List<ExtractionDiagnostic>();
        foreach (var diagnostic in newModel.GetDiagnostics(cancellationToken: ct).Where(Relevant))
        {
            var key = Key(diagnostic);
            if (remaining.TryGetValue(key, out var count) && count > 0)
            {
                remaining[key] = count - 1;
                continue;
            }

            var position = diagnostic.Location.GetLineSpan().StartLinePosition;
            added.Add(new ExtractionDiagnostic
            {
                Id = diagnostic.Id,
                Severity = diagnostic.Severity.ToString(),
                Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
                Line = position.Line + 1,
                Column = position.Character + 1
            });
        }

        return added;
    }

    // ===== Result description =====

    private static ExtractedMethodInfo DescribeExtraction(ExtractionPlan plan, SyntaxNode newRoot, string filePath)
    {
        var analysis = plan.Analysis;
        var method = newRoot.GetAnnotatedNodes(MethodAnnotation).OfType<MethodDeclarationSyntax>().First();
        var callNodes = newRoot.GetAnnotatedNodes(CallSiteAnnotation).ToList();
        var methodLine = method.Identifier.GetLocation().GetLineSpan().StartLinePosition;
        var callLine = callNodes[0].GetLocation().GetLineSpan().StartLinePosition;

        return new ExtractedMethodInfo
        {
            Method = new MethodInfo
            {
                Name = plan.MethodName,
                Signature = SingleLine(method.ToString()[..(method.Body!.SpanStart - method.SpanStart)]),
                Body = method.Body.ToFullString(),
                Location = new LocationInfo { FilePath = filePath, Line = methodLine.Line + 1, Column = 1 }
            },
            CallSite = new CallSiteInfo
            {
                Code = string.Join(plan.NewLine, callNodes.Select(n => n.ToString())),
                Location = new LocationInfo { FilePath = filePath, Line = callLine.Line + 1, Column = callLine.Character + 1 }
            },
            Parameters = analysis.Inputs.Select(s => new ParameterInfo
            {
                Name = s.Name,
                Type = plan.Display(TypeOf(s)),
                Modifier = analysis.RefInputs.Contains(s) ? "ref" : null
            }).ToList(),
            ReturnType = plan.Display(analysis.ReturnType),
            ReturnVariables = analysis.Outputs.Count > 0 ? analysis.Outputs.Select(o => o.Name).ToList() : null,
            Characteristics = analysis.Characteristics
        };
    }

    private static string SingleLine(string text) =>
        string.Join(' ', text.Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    private static PreviewInfo BuildPreview(ExtractionPlan plan, SyntaxNode newRoot)
    {
        var method = newRoot.GetAnnotatedNodes(MethodAnnotation).First();
        var member = newRoot.GetCurrentNode(plan.Selection.ContainingMember);

        return new PreviewInfo
        {
            OriginalCode = plan.Selection.ContainingMember.ToFullString(),
            ModifiedCode = (member?.ToFullString() ?? string.Empty) + method.ToFullString()
        };
    }
}

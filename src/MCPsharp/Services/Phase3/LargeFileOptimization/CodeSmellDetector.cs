using MCPsharp.Models.LargeFileOptimization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3.LargeFileOptimization;

/// <summary>
/// Service for detecting various code smell anti-patterns
/// </summary>
internal static class CodeSmellDetector
{
    private const int MaxParameters = 7;
    private const int GodMethodThreshold = 50;
    private const int GodClassThreshold = 20;
    private const int MaxConditionOperators = 3;
    private const int MaxConditionalNesting = 4;
    private const int MinDuplicateStatements = 3;
    private const int MagicNumberThreshold = 3;
    private const int FeatureEnvyThreshold = 5;
    private const int DataClumpMinParameters = 3;
    private const int DataClumpMinOccurrences = 3;

    private static readonly HashSet<string> AllowedNumericLiterals = new(StringComparer.Ordinal) { "0", "1", "2", "-1", "0.0", "1.0", "0d", "1d", "0m", "1m", "0f", "1f" };

    public static List<CodeSmell> AnalyzeCodeSmells(
        SyntaxNode root,
        SemanticModel semanticModel,
        string filePath,
        CancellationToken cancellationToken)
    {
        var detectors = new Func<List<CodeSmell>>[]
        {
            () => DetectLongParameterListSmells(root, filePath),
            () => DetectLongMethodSmells(root, filePath),
            () => DetectLargeClassSmells(root, filePath),
            () => DetectComplexConditionalSmells(root, filePath),
            () => DetectDuplicateCodeSmells(root, filePath),
            () => DetectMagicNumberSmells(root, filePath),
            () => DetectFeatureEnvySmells(root, semanticModel, filePath, cancellationToken),
            () => DetectDataClumpsSmells(root, filePath),
        };

        var codeSmells = new List<CodeSmell>();
        foreach (var detector in detectors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            codeSmells.AddRange(detector());
        }

        return codeSmells.OrderByDescending(cs => cs.ImpactScore).ToList();
    }

    private static List<CodeSmell> DetectLongParameterListSmells(SyntaxNode root, string filePath)
    {
        return root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => method.ParameterList.Parameters.Count > MaxParameters)
            .Select(method => CreateSmell(
                method,
                filePath,
                "Long Parameter List",
                $"Method has {method.ParameterList.Parameters.Count} parameters (threshold: {MaxParameters})",
                method.ParameterList.Parameters.Count - MaxParameters,
                MaxParameters,
                "Consider using a parameter object or reducing the number of parameters",
                CreatePattern("Introduce Parameter Object", "Group related parameters into a parameter object",
                    ["Create parameter object class", "Replace parameters with object", "Update method calls"], 0.9, 3)))
            .ToList();
    }

    private static List<CodeSmell> DetectLongMethodSmells(SyntaxNode root, string filePath)
    {
        var codeSmells = new List<CodeSmell>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var lineCount = method.GetText().Lines.Count;
            if (lineCount <= GodMethodThreshold)
                continue;

            codeSmells.Add(CreateSmell(
                method,
                filePath,
                "Long Method",
                $"Method has {lineCount} lines (threshold: {GodMethodThreshold})",
                lineCount - GodMethodThreshold,
                GodMethodThreshold,
                "Consider extracting smaller methods from this large method",
                CreatePattern("Extract Method", "Extract logical sections into separate methods",
                    ["Identify logical groups", "Extract into new methods", "Replace with method calls"], 0.95, 2)));
        }

        return codeSmells;
    }

    private static List<CodeSmell> DetectLargeClassSmells(SyntaxNode root, string filePath)
    {
        var codeSmells = new List<CodeSmell>();
        foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var methodCount = classDecl.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
            if (methodCount <= GodClassThreshold)
                continue;

            codeSmells.Add(CreateSmell(
                classDecl,
                filePath,
                "Large Class",
                $"Class has {methodCount} methods (threshold: {GodClassThreshold})",
                methodCount - GodClassThreshold,
                GodClassThreshold,
                "Consider splitting this class into smaller, more focused classes",
                CreatePattern("Extract Class", "Extract related functionality into separate classes",
                    ["Identify responsibilities", "Create new classes", "Move methods and fields", "Update references"], 0.9, 8)));
        }

        return codeSmells;
    }

    /// <summary>
    /// Flags conditions with too many boolean operators and if-statements nested too deeply.
    /// </summary>
    private static List<CodeSmell> DetectComplexConditionalSmells(SyntaxNode root, string filePath)
    {
        var codeSmells = new List<CodeSmell>();
        foreach (var ifStatement in root.DescendantNodes().OfType<IfStatementSyntax>())
        {
            var operatorCount = ifStatement.Condition.DescendantNodesAndSelf()
                .Count(n => n.IsKind(SyntaxKind.LogicalAndExpression) || n.IsKind(SyntaxKind.LogicalOrExpression));
            // An else-if chain is not nesting: only count enclosing if-bodies and else-bodies that are not themselves an if.
            var nesting = ifStatement.Ancestors()
                .TakeWhile(a => a is not MemberDeclarationSyntax)
                .Count(a => (a is IfStatementSyntax outer && outer.Statement.Span.Contains(ifStatement.Span))
                    || (a is ElseClauseSyntax elseClause && elseClause.Statement is not IfStatementSyntax));

            if (operatorCount > MaxConditionOperators)
            {
                codeSmells.Add(CreateSmell(
                    ifStatement,
                    filePath,
                    "Complex Conditional",
                    $"Condition combines {operatorCount} boolean operators (threshold: {MaxConditionOperators})",
                    operatorCount - MaxConditionOperators,
                    MaxConditionOperators,
                    "Consider extracting the condition into a well-named method or variable",
                    CreatePattern("Decompose Conditional", "Extract complex conditions into descriptive methods",
                        ["Identify sub-conditions", "Extract each into a named method or variable", "Replace the original condition"], 0.85, 1)));
            }
            else if (nesting >= MaxConditionalNesting)
            {
                codeSmells.Add(CreateSmell(
                    ifStatement,
                    filePath,
                    "Complex Conditional",
                    $"Conditional is nested {nesting + 1} levels deep (threshold: {MaxConditionalNesting})",
                    nesting + 1 - MaxConditionalNesting,
                    MaxConditionalNesting,
                    "Consider using guard clauses or extracting nested logic into separate methods",
                    CreatePattern("Replace Nested Conditional with Guard Clauses", "Return early to flatten nested conditionals",
                        ["Identify special cases", "Invert conditions into early returns", "Flatten the remaining logic"], 0.8, 2)));
            }
        }

        return codeSmells;
    }

    /// <summary>
    /// Flags members within the file whose bodies are textually identical once trivia is ignored.
    /// </summary>
    private static List<CodeSmell> DetectDuplicateCodeSmells(SyntaxNode root, string filePath)
    {
        var bodies = root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>()
            .Where(m => m.Body != null && m.Body.Statements.Count >= MinDuplicateStatements)
            .GroupBy(m => m.Body!.WithoutTrivia().NormalizeWhitespace().ToFullString(), StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        var codeSmells = new List<CodeSmell>();
        foreach (var group in bodies)
        {
            var members = group.ToList();
            var names = string.Join(", ", members.Select(GetMemberName));
            foreach (var duplicate in members.Skip(1))
            {
                codeSmells.Add(CreateSmell(
                    duplicate,
                    filePath,
                    "Duplicate Code",
                    $"Body of '{GetMemberName(duplicate)}' is identical to other members in this file ({names})",
                    members.Count - 1,
                    1,
                    "Consider extracting the shared logic into a single reusable method",
                    CreatePattern("Extract Method", "Move the duplicated logic into one shared method",
                        ["Extract the common body into a method", "Replace each duplicate with a call", "Remove the redundant copies"], 0.9, 2)));
            }
        }

        return codeSmells;
    }

    /// <summary>
    /// Flags members that use several distinct unexplained numeric literals.
    /// </summary>
    private static List<CodeSmell> DetectMagicNumberSmells(SyntaxNode root, string filePath)
    {
        var codeSmells = new List<CodeSmell>();
        foreach (var member in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            var magicNumbers = member.DescendantNodes().OfType<LiteralExpressionSyntax>()
                .Where(l => l.IsKind(SyntaxKind.NumericLiteralExpression) && !IsAllowedNumericLiteral(l))
                .Select(l => l.Token.Text)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (magicNumbers.Count < MagicNumberThreshold)
                continue;

            codeSmells.Add(CreateSmell(
                member,
                filePath,
                "Magic Numbers",
                $"'{GetMemberName(member)}' uses {magicNumbers.Count} unexplained numeric literals: {string.Join(", ", magicNumbers)}",
                magicNumbers.Count - MagicNumberThreshold + 1,
                MagicNumberThreshold,
                "Consider replacing numeric literals with named constants",
                CreatePattern("Replace Magic Number with Symbolic Constant", "Introduce named constants for literal values",
                    ["Identify the meaning of each literal", "Declare a named constant", "Replace the literals with the constant"], 0.9, 1)));
        }

        return codeSmells;
    }

    /// <summary>
    /// Flags methods that use members of one other (non-framework) type more than members of their own type.
    /// </summary>
    private static List<CodeSmell> DetectFeatureEnvySmells(SyntaxNode root, SemanticModel semanticModel, string filePath, CancellationToken cancellationToken)
    {
        var codeSmells = new List<CodeSmell>();
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (semanticModel.GetDeclaredSymbol(method, cancellationToken)?.ContainingType is not { } ownType)
                continue;

            var ownAccesses = 0;
            var foreignAccesses = new Dictionary<INamedTypeSymbol, int>(SymbolEqualityComparer.Default);
            foreach (var access in method.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
            {
                var symbol = semanticModel.GetSymbolInfo(access, cancellationToken).Symbol;
                if (symbol is null || symbol.IsStatic || symbol.ContainingType is not { } type)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(type, ownType))
                    ownAccesses++;
                else if (!IsFrameworkType(type))
                    foreignAccesses[type] = foreignAccesses.GetValueOrDefault(type) + 1;
            }

            if (foreignAccesses.Count == 0)
                continue;

            var (envied, count) = foreignAccesses.MaxBy(kvp => kvp.Value);
            if (count <= FeatureEnvyThreshold || count <= ownAccesses)
                continue;

            codeSmells.Add(CreateSmell(
                method,
                filePath,
                "Feature Envy",
                $"Method '{method.Identifier.Text}' accesses '{envied.Name}' {count} times but its own type only {ownAccesses} times",
                count - FeatureEnvyThreshold,
                FeatureEnvyThreshold,
                $"Consider moving this method (or part of it) to '{envied.Name}'",
                CreatePattern("Move Method", "Move behaviour closer to the data it uses",
                    ["Identify the envied type", "Move or extract the method into that type", "Update callers"], 0.75, 3)));
        }

        return codeSmells;
    }

    /// <summary>
    /// Flags groups of parameters that repeatedly appear together across methods in the file.
    /// </summary>
    private static List<CodeSmell> DetectDataClumpsSmells(SyntaxNode root, string filePath)
    {
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.ParameterList.Parameters.Count >= DataClumpMinParameters)
            .Select(m => (Method: m, Parameters: m.ParameterList.Parameters
                .Select(p => $"{p.Type} {p.Identifier.Text}")
                .ToHashSet(StringComparer.Ordinal)))
            .ToList();

        var clumps = new Dictionary<string, (List<string> Parameters, List<MethodDeclarationSyntax> Methods)>(StringComparer.Ordinal);
        for (var i = 0; i < methods.Count; i++)
        {
            for (var j = i + 1; j < methods.Count; j++)
            {
                var shared = methods[i].Parameters.Intersect(methods[j].Parameters, StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (shared.Count < DataClumpMinParameters)
                    continue;

                var users = methods.Where(m => shared.All(m.Parameters.Contains)).Select(m => m.Method).ToList();
                clumps.TryAdd(string.Join("|", shared), (shared, users));
            }
        }

        return clumps.Values
            .Where(c => c.Methods.Count >= DataClumpMinOccurrences)
            .Select(c => CreateSmell(
                c.Methods[0],
                filePath,
                "Data Clumps",
                $"Parameters ({string.Join(", ", c.Parameters)}) appear together in {c.Methods.Count} methods: {string.Join(", ", c.Methods.Select(m => m.Identifier.Text))}",
                c.Methods.Count - DataClumpMinOccurrences + 1,
                DataClumpMinOccurrences,
                "Consider grouping these parameters into a dedicated type",
                CreatePattern("Introduce Parameter Object", "Replace the recurring parameter group with a single object",
                    ["Create a type holding the grouped values", "Replace the parameters with the new type", "Update method calls"], 0.85, 3)))
            .ToList();
    }

    public static CodeSmellSeverity DetermineCodeSmellSeverity(int excess, int threshold)
    {
        var ratio = excess / (double)threshold;

        if (ratio <= 0.2)
            return CodeSmellSeverity.Minor;
        if (ratio <= 0.5)
            return CodeSmellSeverity.Major;
        if (ratio <= 1.0)
            return CodeSmellSeverity.Critical;
        return CodeSmellSeverity.Blocker;
    }

    private static CodeSmell CreateSmell(
        SyntaxNode node,
        string filePath,
        string smellType,
        string description,
        int excess,
        int threshold,
        string suggestion,
        RefactoringPattern pattern)
    {
        var span = node.GetLocation().GetLineSpan();
        return new CodeSmell
        {
            SmellType = smellType,
            Description = description,
            Severity = DetermineCodeSmellSeverity(excess, threshold),
            FilePath = filePath,
            StartLine = span.StartLinePosition.Line,
            EndLine = span.EndLinePosition.Line,
            AffectedCode = node.GetText().ToString(),
            Suggestion = suggestion,
            ImpactScore = Math.Min(1.0, excess / (double)threshold),
            RefactoringPatterns = [pattern]
        };
    }

    private static RefactoringPattern CreatePattern(string name, string description, List<string> steps, double applicability, int effort) =>
        new()
        {
            PatternName = name,
            Description = description,
            Steps = steps,
            Applicability = applicability,
            EstimatedEffort = effort
        };

    private static bool IsAllowedNumericLiteral(LiteralExpressionSyntax literal)
    {
        var text = literal.Parent is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression }
            ? "-" + literal.Token.Text
            : literal.Token.Text;
        if (AllowedNumericLiterals.Contains(text))
            return true;

        // Literals that already have a name (constants, enum values, attribute arguments) are not magic.
        return literal.Ancestors().Any(a =>
            a is EnumMemberDeclarationSyntax or AttributeArgumentSyntax
            || (a is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword))
            || (a is LocalDeclarationStatementSyntax local && local.IsConst));
    }

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns.Equals("System", StringComparison.Ordinal)
            || ns.StartsWith("System.", StringComparison.Ordinal)
            || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    private static string GetMemberName(BaseMethodDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
        OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
        ConversionOperatorDeclarationSyntax c => "operator " + c.Type,
        _ => member.Kind().ToString()
    };
}

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MCPsharp.Models;
using MCPsharp.Services.Phase3.DuplicateDetection;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Roslyn-based dependency validation of refactoring suggestions
/// </summary>
public partial class DuplicateCodeDetectorService
{
    /// <summary>Maximum number of blocks per group analysed for extraction safety and callers</summary>
    internal const int MaxBlocksAnalysedPerGroup = 10;

    /// <summary>Maximum number of duplicate groups remembered for later validation</summary>
    internal const int MaxRegisteredGroups = 10_000;

    // Groups produced by this service instance, so suggestions can be validated against their code later
    private readonly ConcurrentDictionary<string, DuplicateGroup> _groupRegistry = new(StringComparer.Ordinal);

    private void RegisterGroups(IEnumerable<DuplicateGroup> groups)
    {
        if (_groupRegistry.Count > MaxRegisteredGroups)
        {
            _groupRegistry.Clear();
        }

        foreach (var group in groups)
        {
            _groupRegistry[group.GroupId] = group;
        }
    }

    private static bool ExtractsCode(RefactoringType refactoringType) => refactoringType is
        RefactoringType.ExtractMethod or
        RefactoringType.ParameterizeMethod or
        RefactoringType.UtilityClass or
        RefactoringType.TemplateMethod;

    /// <summary>
    /// Analyses whether the blocks of a group can be extracted into a shared method, or <c>null</c>
    /// for element kinds (classes, properties) where extract-method does not apply.
    /// </summary>
    private ExtractionSafetyResult? AnalyzeGroupExtraction(DuplicateGroup group, bool requireStaticTarget)
    {
        var analyses = AnalyzeGroupBlocks(group);
        return analyses == null ? null : ExtractionDependencyAnalyzer.AnalyzeGroup(analyses, requireStaticTarget);
    }

    /// <summary>
    /// Per-block extraction analyses of a group, or <c>null</c> when extract-method does not apply to its element kind.
    /// </summary>
    private List<(CodeBlock Block, BlockExtractionAnalysis Analysis)>? AnalyzeGroupBlocks(DuplicateGroup group)
    {
        if (group.DuplicationType is not (DuplicateType.Method or DuplicateType.Constructor or DuplicateType.CodeBlock))
        {
            return null;
        }

        return group.CodeBlocks
            .Take(MaxBlocksAnalysedPerGroup)
            .Select(block => (block, AnalyzeBlockExtraction(block)))
            .ToList();
    }

    private BlockExtractionAnalysis AnalyzeBlockExtraction(CodeBlock block)
    {
        var (node, model) = ResolveBlockNode(block);
        node ??= ParseStandalone(block);

        if (node == null)
        {
            return ExtractionAnalysisBuilder.NotApplicable(block.FilePath, block.StartLine, block.ElementType.ToString());
        }

        return ExtractionDependencyAnalyzer.Analyze(node, model, block.FilePath, block.StartLine);
    }

    /// <summary>
    /// Locates a block's syntax node, preferring the workspace compilation (which supplies a semantic
    /// model) and falling back to re-parsing the file (syntax only).
    /// </summary>
    private (SyntaxNode? Node, SemanticModel? Model) ResolveBlockNode(CodeBlock block)
    {
        var compilation = string.IsNullOrEmpty(block.FilePath) ? null : _workspace.GetCompilationForFile(block.FilePath);
        var tree = compilation?.SyntaxTrees.FirstOrDefault(t => PathsEqual(t.FilePath, block.FilePath));
        if (compilation != null && tree != null)
        {
            var node = FindBlockNode(tree.GetRoot(), block);
            if (node != null)
            {
                return (node, compilation.GetSemanticModel(tree));
            }
        }

        if (!string.IsNullOrEmpty(block.FilePath) && File.Exists(block.FilePath))
        {
            var parsed = CSharpSyntaxTree.ParseText(File.ReadAllText(block.FilePath), path: block.FilePath);
            return (FindBlockNode(parsed.GetRoot(), block), null);
        }

        return (null, null);
    }

    private static SyntaxNode? FindBlockNode(SyntaxNode root, CodeBlock block)
    {
        bool Matches(SyntaxNode node) =>
            block.ElementType switch
            {
                CodeElementType.Method => node is MethodDeclarationSyntax,
                CodeElementType.Constructor => node is ConstructorDeclarationSyntax,
                CodeElementType.CodeBlock => node is BlockSyntax,
                CodeElementType.Class => node is ClassDeclarationSyntax,
                CodeElementType.Property => node is PropertyDeclarationSyntax,
                _ => false
            } && string.Equals(node.ToFullString(), block.SourceCode, StringComparison.Ordinal);

        if (block.SpanStart is { } start && block.SpanLength is { } length && start >= 0 && start + length <= root.FullSpan.End)
        {
            var span = new TextSpan(start, length);
            var found = root.FindNode(span).AncestorsAndSelf().FirstOrDefault(n => n.Span == span && Matches(n));
            if (found != null)
            {
                return found;
            }
        }

        // Fall back to the starting line when offsets are unknown or stale
        return root.DescendantNodes()
            .FirstOrDefault(n => n.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == block.StartLine && Matches(n));
    }

    private static SyntaxNode? ParseStandalone(CodeBlock block) => block.ElementType switch
    {
        CodeElementType.Method or CodeElementType.Constructor => SyntaxFactory.ParseMemberDeclaration(block.SourceCode),
        CodeElementType.CodeBlock => SyntaxFactory.ParseStatement(block.SourceCode) as BlockSyntax,
        _ => null
    };

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrEmpty(first) || string.IsNullOrEmpty(second))
        {
            return false;
        }

        return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates one duplicate group referenced by a suggestion: extraction safety and caller impact.
    /// </summary>
    private RefactoringRisk ValidateGroupDependencies(
        string groupId,
        RefactoringSuggestion suggestion,
        List<ValidationIssue> issues,
        List<DependencyImpact> dependencyImpacts,
        CancellationToken cancellationToken)
    {
        var risk = RefactoringRisk.Low;
        if (!_groupRegistry.TryGetValue(groupId, out var group))
        {
            issues.Add(new ValidationIssue
            {
                Type = ValidationIssueType.DependencyIssue,
                Severity = ValidationSeverity.Warning,
                Description = $"Duplicate group {groupId} is not known to this session; run duplicate detection first so its dependencies can be validated"
            });
            return suggestion.ExtractionSafety is { IsSafe: false } ? RefactoringRisk.High : risk;
        }

        if (ExtractsCode(suggestion.RefactoringType))
        {
            var safety = AnalyzeGroupExtraction(group, suggestion.RefactoringType == RefactoringType.UtilityClass);
            if (safety != null)
            {
                AddExtractionIssues(safety, issues);
                if (!safety.IsSafe)
                {
                    risk = RefactoringRisk.High;
                }
            }
        }

        var impacts = AnalyzeCallerImpact(group, suggestion, cancellationToken);
        dependencyImpacts.AddRange(impacts);
        if (impacts.Exists(i => i.IsBreakingChange))
        {
            risk = RefactoringRisk.High;
        }

        return risk;
    }

    private static void AddExtractionIssues(ExtractionSafetyResult safety, List<ValidationIssue> issues)
    {
        foreach (var reason in safety.UnsafeReasons)
        {
            issues.Add(new ValidationIssue
            {
                Type = ValidationIssueType.DependencyIssue,
                Severity = ValidationSeverity.Error,
                Description = reason,
                IsBlocking = true,
                SuggestedFix = "Restructure the duplicated code so it has a single entry and exit before extracting it"
            });
        }

        foreach (var warning in safety.Warnings)
        {
            issues.Add(new ValidationIssue
            {
                Type = ValidationIssueType.DependencyIssue,
                Severity = ValidationSeverity.Warning,
                Description = warning
            });
        }
    }

    /// <summary>
    /// Finds callers of duplicated methods across the compilation and reports per-file impact.
    /// </summary>
    private List<DependencyImpact> AnalyzeCallerImpact(DuplicateGroup group, RefactoringSuggestion suggestion, CancellationToken cancellationToken)
    {
        var impacts = new List<DependencyImpact>();
        if (group.DuplicationType != DuplicateType.Method)
        {
            return impacts;
        }

        var compilations = _workspace.GetCompilations();
        var methods = new List<IMethodSymbol>();
        foreach (var block in group.CodeBlocks.Take(MaxBlocksAnalysedPerGroup))
        {
            var (node, model) = ResolveBlockNode(block);
            if (node is MethodDeclarationSyntax declaration && model?.GetDeclaredSymbol(declaration, cancellationToken) is { } symbol)
            {
                methods.Add(symbol);
            }
            else
            {
                impacts.Add(new DependencyImpact
                {
                    DependentFile = block.FilePath,
                    ImpactType = DependencyImpactType.Minor,
                    Description = $"Callers of {block.ElementName} could not be resolved without a semantic model",
                    IsBreakingChange = false
                });
            }
        }

        if (compilations.Count == 0 || methods.Count == 0)
        {
            return impacts;
        }

        var references = FindMethodReferences(compilations, methods, cancellationToken);
        foreach (var method in methods)
        {
            var display = method.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            var byFile = references
                .Where(r => ReferenceEquals(r.Method, method.OriginalDefinition))
                .GroupBy(r => r.FilePath, StringComparer.Ordinal);

            foreach (var file in byFile)
            {
                impacts.Add(new DependencyImpact
                {
                    DependentFile = file.Key,
                    ImpactType = suggestion.IsBreakingChange ? DependencyImpactType.BreakingChange : DependencyImpactType.Minor,
                    Description = suggestion.IsBreakingChange
                        ? $"{file.Count()} call site(s) of {display} must be updated"
                        : $"{file.Count()} call site(s) of {display} keep working; the method body delegates to the shared method",
                    IsBreakingChange = suggestion.IsBreakingChange
                });
            }

            if (suggestion.IsBreakingChange &&
                method.DeclaredAccessibility is Microsoft.CodeAnalysis.Accessibility.Public or Microsoft.CodeAnalysis.Accessibility.Protected or Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal)
            {
                impacts.Add(new DependencyImpact
                {
                    DependentFile = method.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? string.Empty,
                    ImpactType = DependencyImpactType.BreakingChange,
                    Description = $"{display} is externally visible; callers outside the analysed compilation cannot be verified",
                    IsBreakingChange = true
                });
            }
        }

        return impacts;
    }

    private static List<(IMethodSymbol Method, string FilePath)> FindMethodReferences(
        IReadOnlyList<Compilation> compilations,
        List<IMethodSymbol> methods,
        CancellationToken cancellationToken)
    {
        var names = methods.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var targets = methods.Select(m => m.OriginalDefinition).ToList();
        var references = new List<(IMethodSymbol, string)>();
        var seen = new HashSet<(IMethodSymbol, string, int)>();

        foreach (var compilation in compilations)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = tree.GetRoot(cancellationToken)
                    .DescendantNodes()
                    .OfType<SimpleNameSyntax>()
                    .Where(n => names.Contains(n.Identifier.ValueText))
                    .ToList();

                if (candidates.Count == 0)
                {
                    continue;
                }

                var model = compilation.GetSemanticModel(tree);
                foreach (var candidate in candidates)
                {
                    if (model.GetSymbolInfo(candidate, cancellationToken).Symbol is not IMethodSymbol referenced)
                    {
                        continue;
                    }

                    // Symbols from different project compilations are distinct instances; match by declaration.
                    var target = targets.Find(t => CrossProjectSymbolComparer.AreEquivalent(t, referenced.OriginalDefinition));
                    if (target != null && seen.Add((target, tree.FilePath, candidate.SpanStart)))
                    {
                        references.Add((target, tree.FilePath));
                    }
                }
            }
        }

        return references;
    }
}

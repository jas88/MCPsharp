using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Applies Roslyn code fixes to a document one diagnostic at a time, in memory.
/// </summary>
/// <remarks>
/// Each fix is computed against the current state of the document: after a fix is applied the
/// diagnostics are recomputed and the remaining targets are mapped through the text changes, so
/// fixes that shift positions do not misplace later ones. A target whose diagnostic is no longer
/// reported when its turn comes is skipped. Nothing is written to disk; the result carries the
/// original and new text of every changed document.
/// </remarks>
public sealed partial class RoslynCodeFixApplier
{
    private static readonly CSharpCompilationOptions AdhocCompilationOptions =
        CSharpCompilation.Create("CodeFixProject").Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary);

    private readonly ILogger _logger;

    public RoslynCodeFixApplier(ILogger<RoslynCodeFixApplier>? logger = null)
    {
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Create a single-document C# project in <paramref name="workspace"/> for a file that is not part
    /// of a loaded project, with the same core references the analyzer adapters compile against.
    /// </summary>
    public static Document CreateAdhocDocument(AdhocWorkspace workspace, string filePath, string content)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            "CodeFixProject",
            "CodeFixProject",
            LanguageNames.CSharp,
            compilationOptions: AdhocCompilationOptions,
            metadataReferences: GetCoreReferences());

        var project = workspace.AddProject(projectInfo);
        var documentInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            Path.GetFileName(filePath),
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From(content), VersionStamp.Create(), filePath)),
            filePath: filePath);

        return workspace.AddDocument(documentInfo);
    }

    /// <summary>
    /// Diagnostics with one of <paramref name="diagnosticIds"/> that <paramref name="analyzers"/> report
    /// for <paramref name="document"/>, in document order.
    /// </summary>
    public static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        Document document,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        IReadOnlyCollection<string> diagnosticIds,
        CancellationToken cancellationToken = default)
    {
        var compilation = await document.Project.GetCompilationAsync(cancellationToken);
        var tree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (compilation == null || tree == null || analyzers.IsDefaultOrEmpty)
        {
            return Array.Empty<Diagnostic>();
        }

        var withAnalyzers = compilation.WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(
                new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        return diagnostics
            .Where(d => !d.IsSuppressed && d.Location.SourceTree == tree && diagnosticIds.Contains(d.Id))
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Fix the diagnostics in <paramref name="document"/>. When <paramref name="targets"/> is null every
    /// diagnostic with one of <paramref name="diagnosticIds"/> reported at the start is a target.
    /// </summary>
    public async Task<CodeFixRunResult> ApplyAsync(
        Document document,
        ImmutableArray<DiagnosticAnalyzer> analyzers,
        IReadOnlyList<RoslynCodeFixAdapter> providers,
        IReadOnlyCollection<string> diagnosticIds,
        IReadOnlyList<CodeFixTarget>? targets = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(diagnosticIds);

        var originalSolution = document.Project.Solution;
        var documentId = document.Id;
        var originalText = await document.GetTextAsync(cancellationToken);

        var diagnostics = await GetDiagnosticsAsync(document, analyzers, diagnosticIds, cancellationToken);
        var pending = targets == null
            ? diagnostics.Select(d => PendingTarget.FromDiagnostic(d)).ToList()
            : targets.Select(t => PendingTarget.FromTarget(t, originalText)).ToList();

        var outcomes = new List<CodeFixOutcome>();
        var solution = originalSolution;
        var diagnosticsCurrent = true;

        foreach (var target in pending.OrderBy(t => t.Position))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target.Position < 0)
            {
                outcomes.Add(target.Outcome(CodeFixOutcomeStatus.Skipped, "Location is outside the document"));
                continue;
            }

            var current = solution.GetDocument(documentId)!;
            if (!diagnosticsCurrent)
            {
                diagnostics = await GetDiagnosticsAsync(current, analyzers, diagnosticIds, cancellationToken);
                diagnosticsCurrent = true;
            }

            var diagnostic = target.FindMatch(diagnostics);
            if (diagnostic == null)
            {
                outcomes.Add(target.Outcome(
                    CodeFixOutcomeStatus.Skipped,
                    "Diagnostic is no longer reported at this location (resolved or invalidated by an earlier fix)"));
                continue;
            }

            var (outcome, changedSolution) = await FixOneAsync(current, diagnostic, target, providers, cancellationToken);
            outcomes.Add(outcome);
            if (changedSolution == null)
            {
                continue;
            }

            var changes = await changedSolution.GetDocument(documentId)!.GetTextChangesAsync(current, cancellationToken);
            var orderedChanges = changes.OrderBy(c => c.Span.Start).ToList();
            foreach (var other in pending)
            {
                other.MapThrough(orderedChanges);
            }

            solution = changedSolution;
            diagnosticsCurrent = false;
        }

        var changedDocuments = await CollectChangedDocumentsAsync(originalSolution, solution, cancellationToken);
        return new CodeFixRunResult { Outcomes = outcomes, ChangedDocuments = changedDocuments };
    }

    private async Task<(CodeFixOutcome Outcome, Solution? ChangedSolution)> FixOneAsync(
        Document document,
        Diagnostic diagnostic,
        PendingTarget target,
        IReadOnlyList<RoslynCodeFixAdapter> providers,
        CancellationToken cancellationToken)
    {
        var message = diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        var reasons = new List<string>();

        foreach (var provider in providers.Where(p => p.CanFix(diagnostic.Id)))
        {
            try
            {
                var computation = await provider.ComputeFixAsync(document, diagnostic, cancellationToken);
                if (!computation.Succeeded)
                {
                    reasons.Add(computation.Error ?? $"{provider.Name} produced no change");
                    continue;
                }

                var unsupported = DescribeUnsupportedChanges(document.Project.Solution, computation.ChangedSolution!);
                if (unsupported != null)
                {
                    reasons.Add($"{provider.Name}: {unsupported}");
                    continue;
                }

                return (target.Outcome(CodeFixOutcomeStatus.Applied, null, message, computation.Title, provider.Name),
                    computation.ChangedSolution);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogFixFailed(_logger, ex, provider.Name, diagnostic.Id);
                reasons.Add($"{provider.Name} failed: {ex.Message}");
            }
        }

        var reason = reasons.Count == 0
            ? $"No loaded code fix provider fixes {diagnostic.Id}"
            : string.Join("; ", reasons);
        return (target.Outcome(CodeFixOutcomeStatus.Failed, reason, message), null);
    }

    /// <summary>
    /// Only text changes to existing documents are applied; fixes that add, remove or reconfigure
    /// documents or projects are rejected.
    /// </summary>
    private static string? DescribeUnsupportedChanges(Solution before, Solution after)
    {
        var changes = after.GetChanges(before);
        if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any())
        {
            return "the fix adds or removes projects, which is not supported";
        }

        foreach (var projectChanges in changes.GetProjectChanges())
        {
            if (projectChanges.GetAddedDocuments().Any() || projectChanges.GetRemovedDocuments().Any() ||
                projectChanges.GetAddedAdditionalDocuments().Any() || projectChanges.GetRemovedAdditionalDocuments().Any() ||
                projectChanges.GetChangedAdditionalDocuments().Any())
            {
                return "the fix adds, removes or changes files other than existing source documents, which is not supported";
            }

            var changedDocuments = projectChanges.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true);
            if (changedDocuments.Any(id => string.IsNullOrEmpty(after.GetDocument(id)?.FilePath)))
            {
                return "the fix changes a document that has no file path";
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<ChangedDocumentText>> CollectChangedDocumentsAsync(
        Solution original,
        Solution changed,
        CancellationToken cancellationToken)
    {
        var result = new List<ChangedDocumentText>();
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var projectChanges in changed.GetChanges(original).GetProjectChanges())
        {
            foreach (var id in projectChanges.GetChangedDocuments(onlyGetDocumentsWithTextChanges: true))
            {
                var before = original.GetDocument(id)!;
                var after = changed.GetDocument(id)!;
                var path = after.FilePath!;
                if (!seenPaths.Add(path))
                {
                    continue;
                }

                var oldText = await before.GetTextAsync(cancellationToken);
                var newText = await after.GetTextAsync(cancellationToken);
                var newString = newText.ToString();
                var oldString = oldText.ToString();
                if (string.Equals(oldString, newString, StringComparison.Ordinal))
                {
                    continue;
                }

                var textChanges = await after.GetTextChangesAsync(before, cancellationToken);
                result.Add(new ChangedDocumentText(path, oldString, newString, UnifiedDiff.Create(path, oldText, textChanges)));
            }
        }

        return result;
    }

    private static ImmutableArray<MetadataReference> GetCoreReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(Enumerable).Assembly,
            typeof(List<>).Assembly
        };

        return assemblies
            .Select(a => a.Location)
            .Where(location => !string.IsNullOrEmpty(location) && File.Exists(location))
            .Distinct(StringComparer.Ordinal)
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToImmutableArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Code fix provider {Provider} failed for diagnostic {RuleId}")]
    private static partial void LogFixFailed(ILogger logger, Exception exception, string provider, string ruleId);

    /// <summary>
    /// A diagnostic still to be fixed, tracked by its start position in the current document text.
    /// </summary>
    private sealed class PendingTarget
    {
        private readonly CodeFixTarget _original;
        private TextSpan? _rewrittenRegion;

        private PendingTarget(CodeFixTarget original, int position, string? message)
        {
            _original = original;
            Position = position;
            Message = message;
        }

        /// <summary>Start position in the current text, or -1 when the location is invalid</summary>
        public int Position { get; private set; }

        private string? Message { get; }

        public static PendingTarget FromDiagnostic(Diagnostic diagnostic)
        {
            var start = diagnostic.Location.GetLineSpan().StartLinePosition;
            return new PendingTarget(
                new CodeFixTarget(diagnostic.Id, start.Line + 1, start.Character + 1),
                diagnostic.Location.SourceSpan.Start,
                diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        }

        public static PendingTarget FromTarget(CodeFixTarget target, SourceText text)
        {
            var lineIndex = target.Line - 1;
            var position = -1;
            if (lineIndex >= 0 && lineIndex < text.Lines.Count)
            {
                var line = text.Lines[lineIndex];
                var column = Math.Max(0, target.Column - 1);
                if (column <= line.End - line.Start)
                {
                    position = line.Start + column;
                }
            }

            return new PendingTarget(target, position, null);
        }

        /// <summary>
        /// The diagnostic with this target's rule at its current position. When an earlier fix rewrote
        /// the text around the target, a diagnostic of the same rule starting inside the rewritten
        /// region is accepted instead.
        /// </summary>
        public Diagnostic? FindMatch(IReadOnlyList<Diagnostic> diagnostics)
        {
            var sameRule = diagnostics.Where(d => d.Id == _original.RuleId).ToList();
            var exact = sameRule.FirstOrDefault(d => d.Location.SourceSpan.Start == Position);
            if (exact != null || _rewrittenRegion is not { } region)
            {
                return exact;
            }

            return sameRule.FirstOrDefault(d =>
                d.Location.SourceSpan.Start >= region.Start && d.Location.SourceSpan.Start <= region.End);
        }

        /// <summary>
        /// Map the position (and any remembered rewritten region) through text changes that are ordered,
        /// non-overlapping and in the coordinates of the text before the changes.
        /// </summary>
        public void MapThrough(IReadOnlyList<TextChange> changes)
        {
            if (Position < 0)
            {
                return;
            }

            if (_rewrittenRegion is { } region)
            {
                var start = MapPosition(region.Start, changes, out _);
                var end = MapPosition(region.End, changes, out var endTouched);
                if (endTouched is { } endRegion)
                {
                    end = endRegion.End;
                }

                _rewrittenRegion = TextSpan.FromBounds(start, Math.Max(start, end));
            }

            Position = MapPosition(Position, changes, out var touched);
            if (touched != null)
            {
                _rewrittenRegion = touched;
            }
        }

        /// <summary>
        /// Map a position through text changes. A position inside replaced text maps to the start of
        /// the replacement, which is reported as <paramref name="rewritten"/>.
        /// </summary>
        private static int MapPosition(int position, IReadOnlyList<TextChange> changes, out TextSpan? rewritten)
        {
            rewritten = null;
            var delta = 0;
            foreach (var change in changes)
            {
                var span = change.Span;
                var newLength = change.NewText?.Length ?? 0;
                if (span.End <= position)
                {
                    delta += newLength - span.Length;
                    continue;
                }

                if (span.Start <= position)
                {
                    rewritten = new TextSpan(span.Start + delta, newLength);
                    return span.Start + delta;
                }

                break;
            }

            return position + delta;
        }

        public CodeFixOutcome Outcome(
            CodeFixOutcomeStatus status,
            string? reason,
            string? message = null,
            string? fixTitle = null,
            string? provider = null) => new()
            {
                RuleId = _original.RuleId,
                Line = _original.Line,
                Column = _original.Column,
                Message = message ?? Message,
                Status = status,
                FixTitle = fixTitle,
                Provider = provider,
                Reason = reason
            };
    }
}

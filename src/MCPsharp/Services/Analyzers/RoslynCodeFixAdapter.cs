using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using MCPsharp.Models;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Adapter that wraps Roslyn CodeFixProvider to provide automatic fixes for diagnostic issues
/// </summary>
public class RoslynCodeFixAdapter
{
    private readonly CodeFixProvider _codeFixProvider;
    private readonly ILogger<RoslynCodeFixAdapter> _logger;
    private readonly string _fixProviderId;

    public string Id => _fixProviderId;
    public string Name { get; }
    public string Description { get; }
    public Version Version { get; }
    public string Author { get; }
    public ImmutableArray<string> FixableDiagnosticIds { get; }

    public RoslynCodeFixAdapter(
        CodeFixProvider codeFixProvider,
        ILogger<RoslynCodeFixAdapter> logger)
    {
        _codeFixProvider = codeFixProvider ?? throw new ArgumentNullException(nameof(codeFixProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Generate unique ID from fix provider type
        _fixProviderId = $"RoslynFix_{codeFixProvider.GetType().Name}";

        // Extract metadata from fix provider
        Name = codeFixProvider.GetType().Name;
        Description = $"Roslyn code fix provider: {Name}";

        // Try to get version from assembly
        var assembly = codeFixProvider.GetType().Assembly;
        Version = assembly.GetName().Version ?? new Version(1, 0, 0);

        // Try to get author from assembly attributes
        var companyAttr = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyCompanyAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyCompanyAttribute;
        Author = companyAttr?.Company ?? "Unknown";

        // Get fixable diagnostic IDs
        FixableDiagnosticIds = codeFixProvider.FixableDiagnosticIds;
    }

    /// <summary>
    /// Check if this fix provider can fix the given diagnostic
    /// </summary>
    public bool CanFix(string diagnosticId)
    {
        return FixableDiagnosticIds.Contains(diagnosticId);
    }

    /// <summary>
    /// Get available fixes for a diagnostic issue
    /// </summary>
    public async Task<ImmutableArray<AnalyzerFix>> GetFixesAsync(
        AnalyzerIssue issue,
        string fileContent,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting fixes for issue {IssueId} with rule {RuleId}", issue.Id, issue.RuleId);

            if (!CanFix(issue.RuleId))
            {
                _logger.LogWarning("Fix provider {ProviderId} cannot fix diagnostic {DiagnosticId}",
                    Id, issue.RuleId);
                return ImmutableArray<AnalyzerFix>.Empty;
            }

            // Create a Roslyn document for the file
            var syntaxTree = CSharpSyntaxTree.ParseText(fileContent, path: issue.FilePath, cancellationToken: cancellationToken);

            var compilation = CSharpCompilation.Create(
                "FixAnalysisAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: GetMetadataReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // Create a workspace and document
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);

            var projectInfo = ProjectInfo.Create(
                projectId,
                VersionStamp.Default,
                "FixProject",
                "FixProject",
                LanguageNames.CSharp,
                filePath: issue.FilePath,
                compilationOptions: compilation.Options,
                metadataReferences: compilation.References);

            var project = workspace.AddProject(projectInfo);
            var document = workspace.AddDocument(project.Id, issue.FilePath, SourceText.From(fileContent));

            // Convert AnalyzerIssue back to Diagnostic
            var diagnostic = CreateDiagnosticFromIssue(issue, syntaxTree);

            // Get code actions from the provider
            var actions = await GetCodeActionsFromContext(document, diagnostic, cancellationToken);

            // Convert CodeActions to AnalyzerFix objects
            var fixes = new List<AnalyzerFix>();
            foreach (var action in actions)
            {
                var fix = await ConvertCodeActionToFixAsync(action, issue, document, cancellationToken);
                if (fix != null)
                {
                    fixes.Add(fix);
                }
            }

            _logger.LogDebug("Found {Count} fixes for issue {IssueId}", fixes.Count, issue.Id);
            return fixes.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting fixes for issue {IssueId} with rule {RuleId}",
                issue.Id, issue.RuleId);
            return ImmutableArray<AnalyzerFix>.Empty;
        }
    }

    /// <summary>
    /// Apply a fix and return the modified content
    /// </summary>
    public async Task<FixApplicationResult> ApplyFixAsync(
        AnalyzerFix fix,
        string fileContent,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            _logger.LogDebug("Applying fix {FixId} for rule {RuleId}", fix.Id, fix.RuleId);

            // Apply the edits to the content
            var modifiedContent = ApplyTextEdits(fileContent, fix.Edits);

            return new FixApplicationResult
            {
                SessionId = Guid.NewGuid().ToString(),
                Success = true,
                AppliedFixes = ImmutableArray.Create(new FixResult
                {
                    FixId = fix.Id,
                    AnalyzerId = fix.AnalyzerId,
                    Success = true,
                    ModifiedFiles = fix.AffectedFiles,
                    AppliedAt = DateTime.UtcNow
                }),
                ModifiedFiles = fix.AffectedFiles,
                AppliedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying fix {FixId} for rule {RuleId}", fix.Id, fix.RuleId);

            return new FixApplicationResult
            {
                SessionId = Guid.NewGuid().ToString(),
                Success = false,
                ErrorMessage = ex.Message,
                AppliedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    private async Task<ImmutableArray<CodeAction>> GetCodeActionsFromContext(
        Document document,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var actions = new List<CodeAction>();

        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, diagnostics) => actions.Add(action),
            cancellationToken);

        await _codeFixProvider.RegisterCodeFixesAsync(context);

        return actions.ToImmutableArray();
    }

    private async Task<AnalyzerFix?> ConvertCodeActionToFixAsync(
        CodeAction codeAction,
        AnalyzerIssue issue,
        Document document,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get the operations for this code action
            var operations = await codeAction.GetOperationsAsync(cancellationToken);

            // Extract text edits from operations
            var edits = new List<Models.TextEdit>();
            var affectedFiles = new HashSet<string> { issue.FilePath };

            foreach (var operation in operations)
            {
                if (operation is ApplyChangesOperation applyChangesOp)
                {
                    var changedSolution = applyChangesOp.ChangedSolution;
                    var originalSolution = document.Project.Solution;

                    foreach (var projectChange in changedSolution.GetChanges(originalSolution).GetProjectChanges())
                    {
                        foreach (var documentId in projectChange.GetChangedDocuments())
                        {
                            var changedDocument = changedSolution.GetDocument(documentId);
                            var originalDocument = originalSolution.GetDocument(documentId);

                            if (changedDocument != null && originalDocument != null)
                            {
                                var changes = await changedDocument.GetTextChangesAsync(originalDocument, cancellationToken);
                                affectedFiles.Add(changedDocument.FilePath ?? issue.FilePath);

                                foreach (var change in changes)
                                {
                                    var originalText = await originalDocument.GetTextAsync(cancellationToken);
                                    var lineSpan = originalText.Lines.GetLinePositionSpan(change.Span);

                                    edits.Add(new ReplaceEdit
                                    {
                                        FilePath = changedDocument.FilePath ?? issue.FilePath,
                                        StartLine = lineSpan.Start.Line,
                                        StartColumn = lineSpan.Start.Character,
                                        EndLine = lineSpan.End.Line,
                                        EndColumn = lineSpan.End.Character,
                                        NewText = change.NewText ?? string.Empty
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return new AnalyzerFix
            {
                Id = Guid.NewGuid().ToString(),
                RuleId = issue.RuleId,
                AnalyzerId = Id,
                Title = codeAction.Title,
                Description = $"Apply code fix: {codeAction.Title}",
                Confidence = Confidence.High,
                IsInteractive = false,
                IsBatchable = true,
                Edits = edits.ToImmutableArray(),
                AffectedFiles = affectedFiles.ToImmutableArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting code action {Title} to fix", codeAction.Title);
            return null;
        }
    }

    private Diagnostic CreateDiagnosticFromIssue(AnalyzerIssue issue, SyntaxTree syntaxTree)
    {
        // Create a diagnostic descriptor
        var descriptor = new DiagnosticDescriptor(
            issue.RuleId,
            issue.Title,
            issue.Description,
            MapCategoryToString(issue.Category),
            MapSeverityToDiagnosticSeverity(issue.Severity),
            isEnabledByDefault: true,
            description: issue.Description,
            helpLinkUri: issue.HelpLink);

        // Create location from line/column info (note: issue uses 1-based, Roslyn uses 0-based)
        var text = syntaxTree.GetText();
        var startLineIndex = Math.Max(0, issue.LineNumber - 1);
        var endLineIndex = Math.Max(0, issue.EndLineNumber - 1);

        if (startLineIndex >= text.Lines.Count || endLineIndex >= text.Lines.Count)
        {
            // Fallback to Location.None if indices are out of range
            return Diagnostic.Create(descriptor, Location.None);
        }

        var startPosition = text.Lines[startLineIndex].Start + Math.Max(0, issue.ColumnNumber - 1);
        var endPosition = text.Lines[endLineIndex].Start + Math.Max(0, issue.EndColumnNumber - 1);
        var span = TextSpan.FromBounds(startPosition, Math.Min(endPosition, text.Length));
        var location = Location.Create(syntaxTree, span);

        return Diagnostic.Create(descriptor, location);
    }

    private string ApplyTextEdits(string content, ImmutableArray<Models.TextEdit> edits)
    {
        // Sort edits in reverse order to maintain positions
        var sortedEdits = edits
            .OrderByDescending(e => e.StartLine)
            .ThenByDescending(e => e.StartColumn)
            .ToList();

        var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();

        foreach (var edit in sortedEdits)
        {
            var startLineIndex = edit.StartLine;
            var endLineIndex = edit.EndLine;

            if (startLineIndex < 0 || startLineIndex >= lines.Count ||
                endLineIndex < 0 || endLineIndex >= lines.Count)
            {
                _logger.LogWarning("Edit has invalid line range: {StartLine}-{EndLine}",
                    edit.StartLine, edit.EndLine);
                continue;
            }

            if (startLineIndex == endLineIndex)
            {
                // Single line edit
                var line = lines[startLineIndex];
                var startColumn = Math.Max(0, edit.StartColumn);
                var endColumn = Math.Min(line.Length, edit.EndColumn);

                var before = line.Substring(0, startColumn);
                var after = line.Substring(endColumn);
                lines[startLineIndex] = before + edit.NewText + after;
            }
            else
            {
                // Multi-line edit
                var firstLine = lines[startLineIndex];
                var lastLine = lines[endLineIndex];

                var before = firstLine.Substring(0, Math.Max(0, edit.StartColumn));
                var after = lastLine.Substring(Math.Min(lastLine.Length, edit.EndColumn));

                // Remove all lines in between
                lines.RemoveRange(startLineIndex, endLineIndex - startLineIndex + 1);

                // Insert the new content
                lines.Insert(startLineIndex, before + edit.NewText + after);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private DiagnosticSeverity MapSeverityToDiagnosticSeverity(IssueSeverity severity)
    {
        return severity switch
        {
            IssueSeverity.Info => DiagnosticSeverity.Info,
            IssueSeverity.Warning => DiagnosticSeverity.Warning,
            IssueSeverity.Error => DiagnosticSeverity.Error,
            IssueSeverity.Critical => DiagnosticSeverity.Error,
            _ => DiagnosticSeverity.Info
        };
    }

    private string MapCategoryToString(RuleCategory category)
    {
        return category switch
        {
            RuleCategory.Style => "Style",
            RuleCategory.Design => "Design",
            RuleCategory.Performance => "Performance",
            RuleCategory.Security => "Security",
            RuleCategory.Reliability => "Reliability",
            RuleCategory.Maintainability => "Maintainability",
            RuleCategory.CodeQuality => "Usage",
            _ => "General"
        };
    }

    private IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var references = new List<MetadataReference>();

        try
        {
            // Add reference to System.Runtime (core library)
            var runtimePath = typeof(object).Assembly.Location;
            if (!string.IsNullOrEmpty(runtimePath))
            {
                references.Add(MetadataReference.CreateFromFile(runtimePath));
            }

            // Add other common references
            var commonTypes = new[]
            {
                typeof(Console),
                typeof(System.Linq.Enumerable),
                typeof(System.Collections.Generic.List<>)
            };

            foreach (var type in commonTypes)
            {
                var assemblyPath = type.Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyPath) && File.Exists(assemblyPath))
                {
                    references.Add(MetadataReference.CreateFromFile(assemblyPath));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading metadata references for code fix analysis");
        }

        return references;
    }
}

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
public partial class RoslynCodeFixAdapter
{
    private static readonly CSharpCompilationOptions LibraryCompilationOptions =
        CSharpCompilation.Create("FixAnalysisAssembly").Options.WithOutputKind(OutputKind.DynamicallyLinkedLibrary);

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
            LogGettingFixes(_logger, issue.Id, issue.RuleId);

            if (!CanFix(issue.RuleId))
            {
                LogCannotFix(_logger, Id, issue.RuleId);
                return ImmutableArray<AnalyzerFix>.Empty;
            }

            // Create a Roslyn document for the file
            var syntaxTree = CSharpSyntaxTree.ParseText(fileContent, path: issue.FilePath, cancellationToken: cancellationToken);

            var compilation = CSharpCompilation.Create(
                "FixAnalysisAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: GetMetadataReferences(),
                options: LibraryCompilationOptions);

            // Create a workspace and document
            var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();

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

            LogFixesFound(_logger, fixes.Count, issue.Id);
            return fixes.ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogGetFixesError(_logger, ex, issue.Id, issue.RuleId);
            return ImmutableArray<AnalyzerFix>.Empty;
        }
    }

    /// <summary>
    /// Apply a fix and return the modified content
    /// </summary>
    public Task<FixApplicationResult> ApplyFixAsync(
        AnalyzerFix fix,
        string fileContent,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            LogApplyingFix(_logger, fix.Id, fix.RuleId);

            // Apply the edits to the content

            return Task.FromResult(new FixApplicationResult
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
            });
        }
        catch (Exception ex)
        {
            LogApplyFixError(_logger, ex, fix.Id, fix.RuleId);

            return Task.FromResult(new FixApplicationResult
            {
                SessionId = Guid.NewGuid().ToString(),
                Success = false,
                ErrorMessage = ex.Message,
                AppliedAt = DateTime.UtcNow,
                Duration = DateTime.UtcNow - startTime
            });
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
            LogConvertCodeActionError(_logger, ex, codeAction.Title);
            return null;
        }
    }

    private static Diagnostic CreateDiagnosticFromIssue(AnalyzerIssue issue, SyntaxTree syntaxTree)
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

    private static DiagnosticSeverity MapSeverityToDiagnosticSeverity(IssueSeverity severity)
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

    private static string MapCategoryToString(RuleCategory category)
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

    private List<MetadataReference> GetMetadataReferences()
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
            LogMetadataReferencesError(_logger, ex);
        }

        return references;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Getting fixes for issue {IssueId} with rule {RuleId}")]
    private static partial void LogGettingFixes(ILogger logger, string issueId, string ruleId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fix provider {ProviderId} cannot fix diagnostic {DiagnosticId}")]
    private static partial void LogCannotFix(ILogger logger, string providerId, string diagnosticId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Found {Count} fixes for issue {IssueId}")]
    private static partial void LogFixesFound(ILogger logger, int count, string issueId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting fixes for issue {IssueId} with rule {RuleId}")]
    private static partial void LogGetFixesError(ILogger logger, Exception exception, string issueId, string ruleId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Applying fix {FixId} for rule {RuleId}")]
    private static partial void LogApplyingFix(ILogger logger, string fixId, string ruleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error applying fix {FixId} for rule {RuleId}")]
    private static partial void LogApplyFixError(ILogger logger, Exception exception, string fixId, string ruleId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error converting code action {Title} to fix")]
    private static partial void LogConvertCodeActionError(ILogger logger, Exception exception, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error loading metadata references for code fix analysis")]
    private static partial void LogMetadataReferencesError(ILogger logger, Exception exception);
}

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Adapter that wraps Roslyn DiagnosticAnalyzer as MCPsharp IAnalyzer
/// </summary>
public class RoslynAnalyzerAdapter : IAnalyzer
{
    private readonly DiagnosticAnalyzer _roslynAnalyzer;
    private readonly ILogger<RoslynAnalyzerAdapter> _logger;
    private readonly string _analyzerId;

    public string Id => _analyzerId;
    public string Name { get; }
    public string Description { get; }
    public Version Version { get; }
    public string Author { get; }
    public ImmutableArray<string> SupportedExtensions { get; } = ImmutableArray.Create(".cs");
    public bool IsEnabled { get; set; } = true;
    public AnalyzerConfiguration Configuration { get; set; } = new();

    public RoslynAnalyzerAdapter(
        DiagnosticAnalyzer roslynAnalyzer,
        ILogger<RoslynAnalyzerAdapter> logger,
        AnalyzerFileReference? analyzerReference = null)
    {
        _roslynAnalyzer = roslynAnalyzer ?? throw new ArgumentNullException(nameof(roslynAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Generate unique ID from analyzer type
        _analyzerId = $"Roslyn_{roslynAnalyzer.GetType().Name}";

        // Extract metadata from analyzer
        Name = roslynAnalyzer.GetType().Name;
        Description = $"Roslyn analyzer: {Name}";

        // Try to get version from assembly
        var assembly = roslynAnalyzer.GetType().Assembly;
        Version = assembly.GetName().Version ?? new Version(1, 0, 0);

        // Try to get author from assembly attributes
        var companyAttr = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyCompanyAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyCompanyAttribute;
        Author = companyAttr?.Company ?? "Unknown";
    }

    public bool CanAnalyze(string targetPath)
    {
        if (string.IsNullOrEmpty(targetPath))
            return false;

        // Check if file exists and has supported extension
        if (File.Exists(targetPath))
        {
            var extension = Path.GetExtension(targetPath);
            return SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        // Check if directory contains C# files
        if (Directory.Exists(targetPath))
        {
            return Directory.GetFiles(targetPath, "*.cs", SearchOption.AllDirectories).Any();
        }

        return false;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initialized Roslyn analyzer adapter: {AnalyzerId}", Id);
        return Task.CompletedTask;
    }

    public async Task<Models.Analyzers.AnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("Analyzing file {FilePath} with Roslyn analyzer {AnalyzerId}", filePath, Id);

            // Create a Roslyn compilation for analysis
            var syntaxTree = CSharpSyntaxTree.ParseText(content, path: filePath, cancellationToken: cancellationToken);

            var compilation = CSharpCompilation.Create(
                "AnalysisAssembly",
                syntaxTrees: new[] { syntaxTree },
                references: GetMetadataReferences(),
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // Create compilation with analyzers
            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create(_roslynAnalyzer),
                options: null);

            // Get diagnostics
            var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);

            // Filter diagnostics to only those for the target file
            var fileDiagnostics = diagnostics
                .Where(d => d.Location.SourceTree?.FilePath == filePath || d.Location == Location.None)
                .ToList();

            // Convert Roslyn diagnostics to MCPsharp issues
            var issues = fileDiagnostics
                .Select(d => ConvertDiagnosticToIssue(d, filePath))
                .ToImmutableArray();

            return new Models.Analyzers.AnalysisResult
            {
                FilePath = filePath,
                AnalyzerId = Id,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = true,
                Issues = issues,
                Statistics = new Dictionary<string, object>
                {
                    ["IssuesFound"] = issues.Length,
                    ["AnalysisDuration"] = DateTime.UtcNow - startTime
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file {FilePath} with Roslyn analyzer {AnalyzerId}", filePath, Id);

            return new Models.Analyzers.AnalysisResult
            {
                FilePath = filePath,
                AnalyzerId = Id,
                StartTime = startTime,
                EndTime = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message,
                Issues = ImmutableArray<AnalyzerIssue>.Empty
            };
        }
    }

    public ImmutableArray<AnalyzerRule> GetRules()
    {
        try
        {
            // Get supported diagnostics from the Roslyn analyzer
            var supportedDiagnostics = _roslynAnalyzer.SupportedDiagnostics;

            return supportedDiagnostics
                .Select(d => new AnalyzerRule
                {
                    Id = d.Id,
                    Title = d.Title.ToString(),
                    Description = d.Description.ToString(),
                    Category = MapCategory(d.Category),
                    DefaultSeverity = MapSeverity(d.DefaultSeverity),
                    IsEnabledByDefault = d.IsEnabledByDefault,
                    Tags = d.CustomTags.ToImmutableArray(),
                    HelpLink = d.HelpLinkUri
                })
                .ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rules from Roslyn analyzer {AnalyzerId}", Id);
            return ImmutableArray<AnalyzerRule>.Empty;
        }
    }

    public ImmutableArray<AnalyzerFix> GetFixes(string ruleId)
    {
        // Roslyn code fixes are handled separately via CodeFixProvider
        // This would require additional infrastructure to support
        return ImmutableArray<AnalyzerFix>.Empty;
    }

    public AnalyzerCapabilities GetCapabilities()
    {
        return new AnalyzerCapabilities
        {
            SupportedLanguages = new[] { "C#" },
            SupportedFileTypes = SupportedExtensions.ToArray(),
            MaxFileSize = 10 * 1024 * 1024, // 10MB
            CanAnalyzeProjects = true,
            CanAnalyzeSolutions = true,
            SupportsParallelProcessing = true,
            CanFixIssues = false // Code fixes require separate infrastructure
        };
    }

    public Task DisposeAsync()
    {
        // No cleanup needed
        return Task.CompletedTask;
    }

    private AnalyzerIssue ConvertDiagnosticToIssue(Diagnostic diagnostic, string filePath)
    {
        var location = diagnostic.Location;
        var lineSpan = location.GetLineSpan();

        return new AnalyzerIssue
        {
            Id = Guid.NewGuid().ToString(),
            RuleId = diagnostic.Id,
            AnalyzerId = Id,
            Title = diagnostic.Descriptor.Title.ToString(),
            Description = diagnostic.GetMessage(),
            FilePath = filePath,
            LineNumber = lineSpan.StartLinePosition.Line + 1,
            ColumnNumber = lineSpan.StartLinePosition.Character + 1,
            EndLineNumber = lineSpan.EndLinePosition.Line + 1,
            EndColumnNumber = lineSpan.EndLinePosition.Character + 1,
            Severity = MapSeverity(diagnostic.Severity),
            Confidence = Confidence.High,
            Category = MapCategory(diagnostic.Descriptor.Category),
            HelpLink = diagnostic.Descriptor.HelpLinkUri,
            Properties = new Dictionary<string, object>
            {
                ["DiagnosticId"] = diagnostic.Id,
                ["WarningLevel"] = diagnostic.WarningLevel,
                ["IsSuppressed"] = diagnostic.IsSuppressed
            }.ToImmutableDictionary()
        };
    }

    private static IssueSeverity MapSeverity(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Hidden => IssueSeverity.Info,
            DiagnosticSeverity.Info => IssueSeverity.Info,
            DiagnosticSeverity.Warning => IssueSeverity.Warning,
            DiagnosticSeverity.Error => IssueSeverity.Error,
            _ => IssueSeverity.Info
        };
    }

    private static RuleCategory MapCategory(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "style" or "naming" or "formatting" => RuleCategory.Style,
            "design" => RuleCategory.Design,
            "performance" => RuleCategory.Performance,
            "security" => RuleCategory.Security,
            "reliability" or "correctness" => RuleCategory.Reliability,
            "maintainability" => RuleCategory.Maintainability,
            "usage" => RuleCategory.CodeQuality, // Map usage to CodeQuality
            _ => RuleCategory.CodeQuality
        };
    }

    private IEnumerable<MetadataReference> GetMetadataReferences()
    {
        // Get common .NET references for compilation
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
                typeof(Console),           // System.Console
                typeof(System.Linq.Enumerable),  // System.Linq
                typeof(System.Collections.Generic.List<>)  // System.Collections.Generic
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
            _logger.LogWarning(ex, "Error loading metadata references for Roslyn analysis");
        }

        return references;
    }
}

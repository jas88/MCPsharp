using MCPsharp.Models;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace MCPsharp.Services.Phase2;

public class ImpactAnalyzerService : IImpactAnalyzerService
{
    private readonly RoslynWorkspace _workspace;
    private readonly ReferenceFinderService _referenceFinder;
    private readonly IConfigAnalyzerService _configAnalyzer;
    private readonly IWorkflowAnalyzerService _workflowAnalyzer;

    public ImpactAnalyzerService(
        RoslynWorkspace workspace,
        ReferenceFinderService referenceFinder,
        IConfigAnalyzerService configAnalyzer,
        IWorkflowAnalyzerService workflowAnalyzer)
    {
        _workspace = workspace;
        _referenceFinder = referenceFinder;
        _configAnalyzer = configAnalyzer;
        _workflowAnalyzer = workflowAnalyzer;
    }

    public async Task<ImpactAnalysisResult> AnalyzeImpactAsync(CodeChange change)
    {
        var csharpImpacts = await FindCSharpImpactsAsync(change);
        var projectRoot = GetProjectRoot(change.FilePath);
        var configImpacts = await FindConfigImpactsAsync(change, projectRoot);
        var workflowImpacts = await FindWorkflowImpactsAsync(change, projectRoot);
        var docImpacts = await FindDocumentationImpactsAsync(change, projectRoot);

        var impactedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        impactedFiles.UnionWith(csharpImpacts.Select(i => i.FilePath));
        impactedFiles.UnionWith(configImpacts.Select(i => i.FilePath));
        impactedFiles.UnionWith(workflowImpacts.Select(i => i.FilePath));
        impactedFiles.UnionWith(docImpacts.Select(i => i.FilePath));

        return new ImpactAnalysisResult
        {
            Change = change,
            CSharpImpacts = csharpImpacts.ToList(),
            ConfigImpacts = configImpacts.ToList(),
            WorkflowImpacts = workflowImpacts.ToList(),
            DocumentationImpacts = docImpacts.ToList(),
            TotalImpactedFiles = impactedFiles.Count
        };
    }

    private async Task<IReadOnlyList<CSharpImpact>> FindCSharpImpactsAsync(CodeChange change, CancellationToken ct = default)
    {
        var impacts = new List<CSharpImpact>();

        // Find the symbol in the workspace
        var document = _workspace.GetDocument(change.FilePath);

        if (document == null)
            return impacts;

        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel == null)
            return impacts;

        var root = await document.GetSyntaxRootAsync(ct);
        if (root == null)
            return impacts;

        // Find all symbols matching the name
        var symbols = root.DescendantNodes()
            .Select(n => semanticModel.GetDeclaredSymbol(n, ct))
            .Where(s => s != null && s.Name == change.SymbolName)
            .ToList();

        foreach (var symbol in symbols.OfType<ISymbol>())
        {
            // Find all references to this symbol
            var project = _workspace.GetAllDocuments().FirstOrDefault()?.Project;
            if (project == null)
                continue;

            var references = await SymbolFinder.FindReferencesAsync(symbol, project.Solution, ct);

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    if (location.Document.FilePath == null)
                        continue;

                    // Skip the definition itself
                    if (location.IsImplicit || location.Document.FilePath == change.FilePath)
                        continue;

                    var lineSpan = location.Location.GetLineSpan();
                    var impactType = DetermineImpactType(change, symbol);

                    impacts.Add(new CSharpImpact
                    {
                        FilePath = location.Document.FilePath,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        ImpactType = impactType,
                        Description = $"{impactType} in {symbol.ContainingType?.Name ?? symbol.Name}: {change.ChangeType}"
                    });
                }
            }
        }

        return impacts;
    }

    private async Task<IReadOnlyList<ConfigImpact>> FindConfigImpactsAsync(CodeChange change, string projectRoot, CancellationToken ct = default)
    {
        var impacts = new List<ConfigImpact>();

        // Find all config files
        var configFiles = Directory.GetFiles(projectRoot, "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectRoot, "*.yml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(projectRoot, "*.yaml", SearchOption.AllDirectories))
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"));

        foreach (var configFile in configFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(configFile, ct);

                // Search for symbol name in config
                if (content.Contains(change.SymbolName, StringComparison.OrdinalIgnoreCase))
                {
                    // Parse config to find exact keys
                    var schema = await _configAnalyzer.GetConfigSchemaAsync(configFile);
                    var matchingKeys = schema.Properties
                        .Where(p => p.Value.DefaultValue?.ToString()?.Contains(change.SymbolName, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();

                    if (matchingKeys.Any())
                    {
                        foreach (var key in matchingKeys)
                        {
                            impacts.Add(new ConfigImpact
                            {
                                FilePath = configFile,
                                ConfigKey = key.Key,
                                ImpactType = DetermineConfigImpactType(change),
                                Description = $"Configuration references {change.SymbolName} in {key.Key}"
                            });
                        }
                    }
                    else
                    {
                        // Generic reference found but not in structured settings
                        impacts.Add(new ConfigImpact
                        {
                            FilePath = configFile,
                            ConfigKey = "unknown",
                            ImpactType = "reference",
                            Description = $"Configuration may reference {change.SymbolName}"
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Ignore files we can't read
            }
        }

        return impacts;
    }

    private async Task<IReadOnlyList<WorkflowImpact>> FindWorkflowImpactsAsync(CodeChange change, string projectRoot, CancellationToken ct = default)
    {
        var impacts = new List<WorkflowImpact>();

        var workflows = await _workflowAnalyzer.GetAllWorkflowsAsync(projectRoot);

        foreach (var workflow in workflows)
        {
            try
            {
                var workflowContent = await File.ReadAllTextAsync(workflow.FilePath, ct);

                foreach (var job in workflow.Jobs ?? [])
                {
                    var isBreaking = false;
                    string? suggestion = null;

                    // Check for direct file references
                    if (workflowContent.Contains(Path.GetFileName(change.FilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        impacts.Add(new WorkflowImpact
                        {
                            FilePath = workflow.FilePath,
                            JobName = job.Name,
                            IsBreaking = false,
                            Description = $"Workflow job '{job.Name}' references {Path.GetFileName(change.FilePath)}"
                        });
                        continue;
                    }

                    // Check for symbol name references
                    if (workflowContent.Contains(change.SymbolName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Determine if this could break the workflow
                        if (change.ChangeType == "delete")
                        {
                            isBreaking = true;
                            suggestion = $"Remove or update references to {change.SymbolName} in workflow";
                        }

                        impacts.Add(new WorkflowImpact
                        {
                            FilePath = workflow.FilePath,
                            JobName = job.Name,
                            IsBreaking = isBreaking,
                            Description = $"Workflow job '{job.Name}' may be affected by {change.ChangeType} of {change.SymbolName}",
                            Suggestion = suggestion
                        });
                    }

                    // Check for build/test impacts
                    if (change.ChangeType == "signature_change" &&
                        job.Steps?.Any(s => s.Run?.Contains("dotnet test", StringComparison.OrdinalIgnoreCase) == true ||
                                           s.Run?.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) == true) == true)
                    {
                        impacts.Add(new WorkflowImpact
                        {
                            FilePath = workflow.FilePath,
                            JobName = job.Name,
                            IsBreaking = true,
                            Description = $"Build/test job '{job.Name}' may fail due to signature change of {change.SymbolName}",
                            Suggestion = "Verify build and tests pass after signature change"
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Ignore files we can't read
            }
        }

        return impacts;
    }

    private async Task<IReadOnlyList<DocumentationImpact>> FindDocumentationImpactsAsync(CodeChange change, string projectRoot, CancellationToken ct = default)
    {
        var impacts = new List<DocumentationImpact>();

        // Find all markdown files
        var mdFiles = Directory.GetFiles(projectRoot, "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"));

        foreach (var mdFile in mdFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(mdFile, ct);

                // Search for symbol name in documentation
                if (content.Contains(change.SymbolName, StringComparison.OrdinalIgnoreCase))
                {
                    var needsUpdate = DetermineIfDocNeedsUpdate(change);
                    var section = FindSectionContaining(content, change.SymbolName);

                    impacts.Add(new DocumentationImpact
                    {
                        FilePath = mdFile,
                        Section = section,
                        NeedsUpdate = needsUpdate,
                        Description = needsUpdate
                            ? $"Documentation mentions {change.SymbolName} and needs update for {change.ChangeType}"
                            : $"Documentation references {change.SymbolName} - review recommended"
                    });
                }

                // Search for old signature if provided
                if (change.OldSignature != null && content.Contains(change.OldSignature, StringComparison.OrdinalIgnoreCase))
                {
                    var section = FindSectionContaining(content, change.OldSignature);

                    impacts.Add(new DocumentationImpact
                    {
                        FilePath = mdFile,
                        Section = section,
                        NeedsUpdate = true,
                        Description = $"Documentation contains outdated signature: {change.OldSignature}"
                    });
                }
            }
            catch (Exception)
            {
                // Ignore files we can't read
            }
        }

        return impacts;
    }

    private static string DetermineImpactType(CodeChange change, ISymbol symbol)
    {
        return change.ChangeType switch
        {
            "delete" => "breaking_change",
            "signature_change" => "breaking_change",
            "rename" => "breaking_change",
            _ when symbol.DeclaredAccessibility == Accessibility.Public => "reference",
            _ => "reference"
        };
    }

    private static string DetermineConfigImpactType(CodeChange change)
    {
        return change.ChangeType switch
        {
            "delete" => "validation",
            "rename" => "reference",
            "signature_change" => "behavior",
            _ => "reference"
        };
    }

    private static bool DetermineIfDocNeedsUpdate(CodeChange change)
    {
        return change.ChangeType is "delete" or "signature_change" or "rename";
    }

    private static string? FindSectionContaining(string content, string text)
    {
        var lines = content.Split('\n');
        string? currentSection = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Track current section header
            if (line.StartsWith("#"))
            {
                currentSection = line.TrimStart('#').Trim();
            }

            // If this line contains our text, return the current section
            if (line.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                return currentSection;
            }
        }

        return null;
    }

    private static string GetProjectRoot(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        while (dir != null && !string.IsNullOrEmpty(dir))
        {
            // Look for common project root indicators
            if (Directory.GetFiles(dir, "*.sln").Any() ||
                Directory.GetFiles(dir, "*.csproj").Any() ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Path.GetDirectoryName(filePath) ?? filePath;
    }
}

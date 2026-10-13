using MCPsharp.Models;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase2;

public partial class ImpactAnalyzerService
{
    private static async Task<IReadOnlyList<ConfigImpact>> FindConfigImpactsAsync(CodeChange change, string projectRoot, CancellationToken ct = default)
    {
        var impacts = new List<ConfigImpact>();

        // If the change is in a config file, always include the file itself
        if (change.FilePath.EndsWith(".json", StringComparison.Ordinal) || change.FilePath.EndsWith(".yml", StringComparison.Ordinal) || change.FilePath.EndsWith(".yaml", StringComparison.Ordinal))
        {
            impacts.Add(new ConfigImpact
            {
                FilePath = change.FilePath,
                ConfigKey = change.SymbolName,
                ImpactType = DetermineConfigImpactType(change),
                Description = $"Configuration file being modified: {change.SymbolName}"
            });
        }

        // Find all config files
        var configFiles = Directory.GetFiles(projectRoot, "*.json", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectRoot, "*.yml", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(projectRoot, "*.yaml", SearchOption.AllDirectories))
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj") && f != change.FilePath);

        foreach (var configFile in configFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(configFile, ct);

                // Search for symbol name in config (case-insensitive, substring match)
                if (content.Contains(change.SymbolName, StringComparison.OrdinalIgnoreCase))
                {
                    impacts.Add(new ConfigImpact
                    {
                        FilePath = configFile,
                        ConfigKey = change.SymbolName,
                        ImpactType = DetermineConfigImpactType(change),
                        Description = $"Configuration references {change.SymbolName}"
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore files we can't read
            }
        }

        return impacts;
    }

    private static async Task<IReadOnlyList<DocumentationImpact>> FindDocumentationImpactsAsync(CodeChange change, string projectRoot, CancellationToken ct = default)
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore files we can't read
            }
        }

        return impacts;
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
            if (line.StartsWith('#'))
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
}

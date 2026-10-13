using MCPsharp.Models;
using MCPsharp.Services.Roslyn;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase2;

public partial class ImpactAnalyzerService : IImpactAnalyzerService
{
    private readonly RoslynWorkspace _workspace;
    private readonly IWorkflowAnalyzerService _workflowAnalyzer;

    public ImpactAnalyzerService(
        RoslynWorkspace workspace,
        ReferenceFinderService referenceFinder,
        IConfigAnalyzerService configAnalyzer,
        IWorkflowAnalyzerService workflowAnalyzer)
    {
        _workspace = workspace;
        _workflowAnalyzer = workflowAnalyzer;
    }

    public async Task<ImpactAnalysisResult> AnalyzeImpactAsync(CodeChange change)
    {
        var csharpImpacts = await FindCSharpImpactsAsync(change);
        var projectRoot = GetProjectRoot(change.FilePath);
        var configImpacts = await FindConfigImpactsAsync(change, projectRoot);
        var workflowImpacts = await FindWorkflowImpactsAsync(change, projectRoot);
        var docImpacts = await FindDocumentationImpactsAsync(change, projectRoot);

        // Deduplicate impacts by file path
        var uniqueCSharpImpacts = DeduplicateImpacts(csharpImpacts);
        var uniqueConfigImpacts = DeduplicateImpacts(configImpacts);
        var uniqueWorkflowImpacts = DeduplicateImpacts(workflowImpacts);
        var uniqueDocImpacts = DeduplicateImpacts(docImpacts);

        var impactedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        impactedFiles.UnionWith(uniqueCSharpImpacts.Select(i => i.FilePath));
        impactedFiles.UnionWith(uniqueConfigImpacts.Select(i => i.FilePath));
        impactedFiles.UnionWith(uniqueWorkflowImpacts.Select(i => i.FilePath));
        impactedFiles.UnionWith(uniqueDocImpacts.Select(i => i.FilePath));

        return new ImpactAnalysisResult
        {
            Change = change,
            CSharpImpacts = uniqueCSharpImpacts.ToList(),
            ConfigImpacts = uniqueConfigImpacts.ToList(),
            WorkflowImpacts = uniqueWorkflowImpacts.ToList(),
            DocumentationImpacts = uniqueDocImpacts.ToList(),
            TotalImpactedFiles = impactedFiles.Count
        };
    }

    private static IEnumerable<T> DeduplicateImpacts<T>(IEnumerable<T> impacts) where T : class
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var impact in impacts)
        {
            var filePath = GetFilePath(impact);
            if (filePath != null && seen.Add(filePath))
            {
                yield return impact;
            }
        }
    }

    private static string? GetFilePath<T>(T impact) where T : class
    {
        return impact switch
        {
            CSharpImpact csharp => csharp.FilePath,
            ConfigImpact config => config.FilePath,
            WorkflowImpact workflow => workflow.FilePath,
            DocumentationImpact doc => doc.FilePath,
            _ => null
        };
    }

    private static string GetProjectRoot(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        var originalDir = dir;

        while (dir != null && !string.IsNullOrEmpty(dir))
        {
            // Look for common project root indicators
            if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                Directory.GetFiles(dir, "*.csproj").Length > 0 ||
                Directory.Exists(Path.Combine(dir, ".git")))
            {
                return dir;
            }

            // Look for common project files/directories that indicate a project root
            if (Directory.GetFiles(dir, "appsettings*.json").Length > 0 ||
                Directory.GetFiles(dir, "*.json").Length > 0 ||
                Directory.Exists(Path.Combine(dir, ".github")) ||
                Directory.Exists(Path.Combine(dir, "src")) ||
                Directory.Exists(Path.Combine(dir, "docs")))
            {
                // If we found project indicators, but haven't found formal project files,
                // and we're in a subdirectory, go up one more level to find the true root
                var parentDir = Path.GetDirectoryName(dir);
                // Check if parent has better indicators
                if (parentDir != null && parentDir != dir &&
                    (Directory.GetFiles(parentDir, "*.sln").Length > 0 ||
                     Directory.GetFiles(parentDir, "*.csproj").Length > 0 ||
                     Directory.Exists(Path.Combine(parentDir, ".git"))))
                {
                    return parentDir;
                }
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        // If no project root indicators found, try to find common project structures
        // from the original directory
        // If we're in a src subdirectory, go up one level
        if (originalDir != null && Path.GetFileName(originalDir).Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            var parentDir = Path.GetDirectoryName(originalDir);
            if (parentDir != null)
            {
                return parentDir;
            }
        }

        // If no project root indicators found, use the immediate directory
        // This handles test scenarios where projects are created in temp directories
        var immediateDir = Path.GetDirectoryName(filePath);
        return immediateDir ?? filePath;
    }
}

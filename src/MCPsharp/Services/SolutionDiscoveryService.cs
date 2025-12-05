using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Discovers all solutions and projects in a workspace directory.
/// </summary>
public sealed class SolutionDiscoveryService
{
    private readonly ILogger<SolutionDiscoveryService>? _logger;

    public SolutionDiscoveryService(ILogger<SolutionDiscoveryService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Result of solution discovery.
    /// </summary>
    public sealed class DiscoveryResult
    {
        public required string RootPath { get; init; }
        public required IReadOnlyList<string> Solutions { get; init; }
        public required IReadOnlyList<string> StandaloneProjects { get; init; }
        public required IReadOnlyDictionary<string, IReadOnlyList<string>> ProjectsBySolution { get; init; }

        public int TotalSolutions => Solutions.Count;
        public int TotalProjects => StandaloneProjects.Count + ProjectsBySolution.Values.Sum(p => p.Count);
        public bool IsMonorepo => Solutions.Count > 1;
    }

    /// <summary>
    /// Discovers all solutions and projects in the given root directory.
    /// </summary>
    public async Task<DiscoveryResult> DiscoverAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        rootPath = Path.GetFullPath(rootPath);

        _logger?.LogInformation("Discovering solutions in: {Path}", rootPath);

        // Find all solution files
        var solutions = await Task.Run(() =>
            Directory.GetFiles(rootPath, "*.sln", SearchOption.AllDirectories)
                .OrderBy(s => s.Length) // Prefer shorter paths (closer to root)
                .ThenBy(s => s)
                .ToList(),
            cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Found {Count} solution files", solutions.Count);

        // Find all project files
        var allProjects = await Task.Run(() =>
            Directory.GetFiles(rootPath, "*.csproj", SearchOption.AllDirectories)
                .ToHashSet(StringComparer.OrdinalIgnoreCase),
            cancellationToken).ConfigureAwait(false);

        _logger?.LogDebug("Found {Count} project files", allProjects.Count);

        // Parse each solution to find its projects
        var projectsBySolution = new Dictionary<string, IReadOnlyList<string>>();
        var projectsInSolutions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var solutionPath in solutions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projects = await ParseSolutionProjectsAsync(solutionPath, cancellationToken)
                .ConfigureAwait(false);

            projectsBySolution[solutionPath] = projects;

            foreach (var project in projects)
            {
                projectsInSolutions.Add(project);
            }
        }

        // Find standalone projects (not in any solution)
        var standaloneProjects = allProjects
            .Where(p => !projectsInSolutions.Contains(p))
            .OrderBy(p => p)
            .ToList();

        _logger?.LogInformation(
            "Discovery complete: {Solutions} solutions, {InSolution} projects in solutions, {Standalone} standalone projects",
            solutions.Count,
            projectsInSolutions.Count,
            standaloneProjects.Count);

        return new DiscoveryResult
        {
            RootPath = rootPath,
            Solutions = solutions,
            StandaloneProjects = standaloneProjects,
            ProjectsBySolution = projectsBySolution
        };
    }

    /// <summary>
    /// Parses a solution file to extract project paths.
    /// </summary>
    private async Task<IReadOnlyList<string>> ParseSolutionProjectsAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        var projects = new List<string>();
        var solutionDir = Path.GetDirectoryName(solutionPath) ?? "";

        try
        {
            var lines = await File.ReadAllLinesAsync(solutionPath, cancellationToken)
                .ConfigureAwait(false);

            foreach (var line in lines)
            {
                // Solution file format: Project("{GUID}") = "Name", "Path", "{GUID}"
                if (!line.TrimStart().StartsWith("Project(", StringComparison.Ordinal))
                    continue;

                var parts = line.Split('"');
                if (parts.Length < 6) continue;

                var projectPath = parts[5]; // The path is in the 6th quoted section

                // Skip solution folders (they don't have file extensions)
                if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Resolve relative path
                var fullPath = Path.GetFullPath(Path.Combine(solutionDir, projectPath));

                if (File.Exists(fullPath))
                {
                    projects.Add(fullPath);
                }
                else
                {
                    _logger?.LogDebug("Project not found: {Path}", fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to parse solution: {Path}", solutionPath);
        }

        return projects;
    }

    /// <summary>
    /// Gets a unified list of all projects (from solutions + standalone), deduplicated.
    /// </summary>
    public IReadOnlyList<string> GetUnifiedProjectList(DiscoveryResult discovery)
    {
        var allProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projects in discovery.ProjectsBySolution.Values)
        {
            foreach (var project in projects)
            {
                allProjects.Add(project);
            }
        }

        foreach (var project in discovery.StandaloneProjects)
        {
            allProjects.Add(project);
        }

        return allProjects.OrderBy(p => p).ToList();
    }

    /// <summary>
    /// Suggests which solution(s) to load based on heuristics.
    /// </summary>
    public IReadOnlyList<string> SuggestSolutionsToLoad(DiscoveryResult discovery)
    {
        if (discovery.Solutions.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (discovery.Solutions.Count == 1)
        {
            return discovery.Solutions;
        }

        // For monorepos, prefer solutions that:
        // 1. Are in the root directory
        // 2. Match the directory name
        // 3. Have the most projects

        var rootDirName = Path.GetFileName(discovery.RootPath);
        var suggestions = new List<(string Path, int Score)>();

        foreach (var solution in discovery.Solutions)
        {
            var score = 0;
            var solutionDir = Path.GetDirectoryName(solution) ?? "";
            var solutionName = Path.GetFileNameWithoutExtension(solution);

            // Prefer root-level solutions
            if (solutionDir.Equals(discovery.RootPath, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            // Prefer solutions matching directory name
            if (solutionName.Equals(rootDirName, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            // Prefer solutions with more projects
            if (discovery.ProjectsBySolution.TryGetValue(solution, out var projects))
            {
                score += projects.Count;
            }

            suggestions.Add((solution, score));
        }

        // Return all solutions for unified loading (per requirements)
        // But sorted by score for display purposes
        return suggestions
            .OrderByDescending(s => s.Score)
            .Select(s => s.Path)
            .ToList();
    }

    /// <summary>
    /// Builds a dependency graph showing which projects reference each other.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> BuildDependencyGraphAsync(
        IEnumerable<string> projectPaths,
        CancellationToken cancellationToken = default)
    {
        var graph = new Dictionary<string, IReadOnlyList<string>>();

        foreach (var projectPath in projectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var references = await ParseProjectReferencesAsync(projectPath, cancellationToken)
                .ConfigureAwait(false);

            graph[projectPath] = references;
        }

        return graph;
    }

    private async Task<IReadOnlyList<string>> ParseProjectReferencesAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var references = new List<string>();
        var projectDir = Path.GetDirectoryName(projectPath) ?? "";

        try
        {
            var content = await File.ReadAllTextAsync(projectPath, cancellationToken)
                .ConfigureAwait(false);

            // Simple regex-free parsing for ProjectReference elements
            var startTag = "<ProjectReference Include=\"";
            var index = 0;

            while ((index = content.IndexOf(startTag, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                index += startTag.Length;
                var endIndex = content.IndexOf('"', index);
                if (endIndex < 0) break;

                var relativePath = content[index..endIndex];
                var fullPath = Path.GetFullPath(Path.Combine(projectDir, relativePath));

                if (File.Exists(fullPath))
                {
                    references.Add(fullPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to parse project references: {Path}", projectPath);
        }

        return references;
    }
}

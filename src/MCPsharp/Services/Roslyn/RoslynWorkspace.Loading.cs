using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Loading a solution, project or directory into the workspace.
/// </summary>
public partial class RoslynWorkspace
{
    private async Task InitializeCoreAsync(string projectPath)
    {
        _health = new WorkspaceHealth { IsInitialized = false };

        try
        {
            var (slnPath, csprojPath, workspaceRoot) = ResolveProjectPath(projectPath);

            // If we couldn't find a project or solution, fall back to adhoc workspace
            if (slnPath == null && csprojPath == null)
            {
                await InitializeAdhocWorkspaceAsync(workspaceRoot);
                return;
            }

            // Use MSBuildWorkspace to load the actual project/solution with all references.
            // The new workspace is disposed here unless ownership passes to _workspace.
            MSBuildWorkspace? pendingWorkspace = null;
            MSBuildWorkspace msbuildWorkspace;
            try
            {
                pendingWorkspace = MSBuildWorkspace.Create();

                _workspaceFailedRegistration?.Dispose();
                _workspaceFailedRegistration = pendingWorkspace.RegisterWorkspaceFailedHandler(e =>
                {
                    Console.Error.WriteLine($"MSBuildWorkspace warning: {e.Diagnostic.Kind} - {e.Diagnostic.Message}");
                });

                if (slnPath != null)
                {
                    var solution = await pendingWorkspace.OpenSolutionAsync(slnPath);
                    if (!solution.ProjectIds.Any())
                    {
                        throw new InvalidOperationException("Solution contains no projects");
                    }
                }
                else
                {
                    await pendingWorkspace.OpenProjectAsync(csprojPath!);
                }

                _workspace?.Dispose();
                _workspace = pendingWorkspace;
                msbuildWorkspace = pendingWorkspace;
                pendingWorkspace = null;
            }
            finally
            {
                pendingWorkspace?.Dispose();
            }
            _adhocProjectId = null;
            _state = CompilationState.Empty;
            _semanticModelCache.InvalidateAll();

            await TrackWorkspaceFileHealthAsync(workspaceRoot);
            // Must run before the first compilation: load failures are only raised on first load
            _health.AnalyzerLoadFailures = CollectAnalyzerLoadFailures(msbuildWorkspace.CurrentSolution);
            await RefreshAllCompilationsAndHealthAsync();
            _health.IsInitialized = true;
        }
        catch (Exception ex)
        {
            // MSBuildWorkspace failed, fallback to ad-hoc workspace
            await Console.Error.WriteLineAsync($"MSBuildWorkspace initialization failed: {ex}");

            string adhocRoot = projectPath;
            if (File.Exists(projectPath))
            {
                adhocRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
            }

            await InitializeAdhocWorkspaceAsync(adhocRoot);
        }
    }

    private static (string? SlnPath, string? CsprojPath, string WorkspaceRoot) ResolveProjectPath(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            var root = Path.GetDirectoryName(projectPath) ?? projectPath;
            var extension = Path.GetExtension(projectPath).ToLowerInvariant();
            return extension switch
            {
                ".sln" or ".slnx" => (projectPath, null, root),
                ".csproj" => (null, projectPath, root),
                _ => throw new ArgumentException($"Unsupported file type: {extension}. Expected .sln, .slnx or .csproj")
            };
        }

        if (!Directory.Exists(projectPath))
        {
            throw new ArgumentException($"Path does not exist: {projectPath}");
        }

        // Prefer a solution if available, then a project file
        var slnFile = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(projectPath, "*.slnx", SearchOption.TopDirectoryOnly))
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
        if (slnFile != null)
        {
            return (slnFile, null, projectPath);
        }

        var csprojFile = Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
        return (null, csprojFile, projectPath);
    }

    /// <summary>
    /// Initialize an ad-hoc workspace (fallback when MSBuildWorkspace fails or no .csproj found)
    /// </summary>
    private async Task InitializeAdhocWorkspaceAsync(string projectPath)
    {
        CreateAdhocWorkspace("Project", new[]
        {
            typeof(object).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(Task).Assembly.Location,
            typeof(System.Net.Http.HttpClient).Assembly.Location,
            typeof(System.Collections.Generic.List<string>).Assembly.Location
        });

        _health = new WorkspaceHealth { IsInitialized = false };

        var allFiles = EnumerateSourceFiles(projectPath);
        _health.TotalFiles = allFiles.Count;

        // Add all .cs files and track health
        int parseableCount = 0;
        foreach (var file in allFiles)
        {
            var fileHealth = await AddDocumentAndTrackHealthAsync(file);
            _health.FileStatus[file] = fileHealth;
            if (fileHealth.IsParseable)
            {
                parseableCount++;
            }
        }

        _health.ParseableFiles = parseableCount;

        await RefreshAllCompilationsAndHealthAsync();
        _health.IsInitialized = true;
    }

    private void CreateAdhocWorkspace(string projectName, IEnumerable<string> referencePaths)
    {
        LoadedProjectPath = null;
        var adhocWorkspace = new AdhocWorkspace();
        _workspace?.Dispose();
        _workspace = adhocWorkspace;
        _state = CompilationState.Empty;
        _semanticModelCache.InvalidateAll();

        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            projectName,
            projectName,
            LanguageNames.CSharp);

        var project = adhocWorkspace.AddProject(projectInfo);
        _adhocProjectId = project.Id;

        foreach (var reference in referencePaths.Where(File.Exists).Distinct(StringComparer.Ordinal))
        {
            project = project.AddMetadataReference(MetadataReference.CreateFromFile(reference));
        }

        adhocWorkspace.TryApplyChanges(project.Solution);
    }

    private static List<string> EnumerateSourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains("/obj/") && !file.Contains("/bin/") &&
                           !file.Contains("\\obj\\") && !file.Contains("\\bin\\"))
            .ToList();

    private async Task TrackWorkspaceFileHealthAsync(string workspaceRoot)
    {
        var allFiles = EnumerateSourceFiles(workspaceRoot);
        _health.TotalFiles = allFiles.Count;

        int parseableCount = 0;
        foreach (var file in allFiles)
        {
            var fileHealth = await TrackFileHealthAsync(file);
            _health.FileStatus[file] = fileHealth;
            if (fileHealth.IsParseable)
            {
                parseableCount++;
            }
        }

        _health.ParseableFiles = parseableCount;
    }
}

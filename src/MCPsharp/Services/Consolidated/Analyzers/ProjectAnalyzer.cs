using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using System.Text.RegularExpressions;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes project-level code elements including structure, dependencies, build configuration, and metrics.
/// </summary>
public class ProjectAnalyzer
{
    private readonly FileAnalyzer _fileAnalyzer;
    private readonly ILogger<ProjectAnalyzer> _logger;

    public ProjectAnalyzer(FileAnalyzer? fileAnalyzer = null, ILogger<ProjectAnalyzer>? logger = null)
    {
        _fileAnalyzer = fileAnalyzer ?? new FileAnalyzer();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectAnalyzer>.Instance;
    }

    public async Task<ProjectStructure> GetProjectStructureAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var structure = new ProjectStructure
            {
                ProjectPath = projectPath,
                ProjectType = "Unknown",
                Configurations = new List<string>(),
                TargetFrameworks = new List<string>(),
                References = new List<ProjectReference>()
            };

            if (Directory.Exists(Path.GetDirectoryName(projectPath)))
            {
                structure.Configurations.Add("Debug");
                structure.Configurations.Add("Release");
                structure.TargetFrameworks.Add("Unknown");
            }

            return structure;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project structure for {Project}", projectPath);
        }

        return new ProjectStructure { ProjectPath = projectPath, ProjectType = "Unknown" };
    }

    public async Task<List<ProjectFile>> GetProjectFileInventoryAsync(string projectPath, ToolOptions? options, CancellationToken ct)
    {
        try
        {
            var files = new List<ProjectFile>();

            var projectDir = Path.GetDirectoryName(projectPath);
            var filePaths = projectDir != null ? Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories) : Array.Empty<string>();

            foreach (var filePath in filePaths)
            {
                var fileInfo = new FileInfo(filePath);
                var basicInfo = await _fileAnalyzer.GetFileBasicInfoAsync(filePath, ct);

                if (basicInfo != null)
                {
                    files.Add(new ProjectFile
                    {
                        Path = filePath,
                        Type = Path.GetExtension(filePath).TrimStart('.'),
                        Language = basicInfo.Language,
                        Size = fileInfo.Length,
                        LineCount = basicInfo.LineCount,
                        LastModified = fileInfo.LastWriteTimeUtc
                    });
                }
            }

            return files.OrderBy(f => Path.GetFileName(f.Path)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project file inventory for {Project}", projectPath);
        }

        return new List<ProjectFile>();
    }

    public async Task<ProjectDependencies?> AnalyzeProjectDependenciesAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var dependencies = new ProjectDependencies
            {
                PackageReferences = new List<PackageDependency>(),
                ProjectReferences = new List<ProjectReference>(),
                AssemblyReferences = new List<AssemblyReference>()
            };

            if (File.Exists(projectPath) && Path.GetExtension(projectPath).ToLowerInvariant() == ".csproj")
            {
                var content = await File.ReadAllTextAsync(projectPath, ct);

                var packagePattern = @"<PackageReference\s+Include=""([^""]+)""";
                var packageMatches = Regex.Matches(content, packagePattern);
                dependencies.PackageReferences.AddRange(packageMatches.Cast<Match>()
                    .Select(m => new PackageDependency { Name = m.Groups[1].Value, Version = "Unknown", IsDevelopmentDependency = false, UsedBy = new List<string>() }));

                var projectRefPattern = @"<ProjectReference\s+Include=""([^""]+)""";
                var projectRefMatches = Regex.Matches(content, projectRefPattern);
                dependencies.ProjectReferences.AddRange(projectRefMatches.Cast<Match>()
                    .Select(m => new ProjectReference { Name = Path.GetFileNameWithoutExtension(m.Groups[1].Value), ReferenceType = "Project", Path = m.Groups[1].Value }));
            }

            return dependencies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project dependencies for {Project}", projectPath);
        }

        return null;
    }

    public async Task<ProjectBuildInfo?> AnalyzeProjectBuildAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var buildInfo = new ProjectBuildInfo
            {
                BuildSystem = "MSBuild",
                BuildConfigurations = new List<string>(),
                BuildSteps = new List<BuildStep>(),
                BuildArtifacts = new List<string>()
            };

            if (File.Exists(projectPath) && Path.GetExtension(projectPath).ToLowerInvariant() == ".csproj")
            {
                var content = await File.ReadAllTextAsync(projectPath, ct);

                var tfPattern = @"<TargetFramework>([^<]+)</TargetFramework>";
                var tfMatch = Regex.Match(content, tfPattern);
                if (tfMatch.Success)
                    buildInfo.BuildConfigurations.Add($"TargetFramework: {tfMatch.Groups[1].Value}");

                var outputPathPattern = @"<OutputPath>([^<]+)</OutputPath>";
                var outputPathMatch = Regex.Match(content, outputPathPattern);
                if (outputPathMatch.Success)
                    buildInfo.BuildArtifacts.Add(outputPathMatch.Groups[1].Value);

                buildInfo.BuildConfigurations.Add("Debug");
                buildInfo.BuildConfigurations.Add("Release");

                buildInfo.BuildSteps.Add(new BuildStep
                {
                    Name = "Build",
                    Command = "dotnet build",
                    Inputs = new List<string> { projectPath },
                    Outputs = new List<string>()
                });
            }

            return buildInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project build info for {Project}", projectPath);
        }

        return null;
    }

    public async Task<ProjectMetrics?> CalculateProjectMetricsAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var files = await GetProjectFileInventoryAsync(projectPath, null, ct);

            var csharpFiles = files.Where(f => f.Language == "C#").ToList();
            var totalLines = csharpFiles.Sum(f => f.LineCount);

            return new ProjectMetrics
            {
                TotalFiles = files.Count,
                LinesOfCode = totalLines,
                TypesCount = 0,
                MethodsCount = 0,
                AverageComplexity = 0,
                FileMetrics = csharpFiles.Select(f => new FileMetric
                {
                    FilePath = f.Path,
                    LinesOfCode = f.LineCount,
                    Complexity = 1.0,
                    QualityScore = 100.0
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating project metrics for {Project}", projectPath);
        }

        return null;
    }
}

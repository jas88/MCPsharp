using MCPsharp.Models.Consolidated;
using Microsoft.CodeAnalysis;
using ConsolidatedProjectReference = MCPsharp.Models.Consolidated.ProjectReference;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    private ProjectStructure GetProjectStructure(string projectPath)
    {
        try
        {
            var structure = new ProjectStructure
            {
                ProjectPath = projectPath,
                ProjectType = "Unknown",
                Configurations = new List<string>(),
                TargetFrameworks = new List<string>(),
                References = new List<ConsolidatedProjectReference>()
            };

            var projectDirectory = ResolveProjectDirectory(projectPath);
            if (projectDirectory != null && Directory.Exists(projectDirectory))
            {

                // Add basic configurations
                structure.Configurations.Add("Debug");
                structure.Configurations.Add("Release");

                // Could parse project file for target frameworks, but simplified for now
                structure.TargetFrameworks.Add("Unknown");
            }

            return structure;
        }
        catch (Exception ex)
        {
            LogErrorGettingProjectStructureFor(_logger, ex, projectPath);
        }

        return new ProjectStructure { ProjectPath = projectPath, ProjectType = "Unknown" };
    }

    private async Task<List<ProjectFile>> GetProjectFileInventoryAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var files = new List<ProjectFile>();

            // Get all C# files in the project directory (the path itself when it is a directory,
            // otherwise the directory containing the project file)
            var projectDir = ResolveProjectDirectory(projectPath);
            var filePaths = projectDir != null && Directory.Exists(projectDir)
                ? Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();

            foreach (var filePath in filePaths)
            {
                var fileInfo = new FileInfo(filePath);
                var basicInfo = await GetFileBasicInfoAsync(filePath, ct);

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
            LogErrorGettingProjectFileInventory(_logger, ex, projectPath);
        }

        return new List<ProjectFile>();
    }

    private async Task<ProjectDependencies?> AnalyzeProjectDependenciesAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var dependencies = new ProjectDependencies
            {
                PackageReferences = new List<PackageDependency>(),
                ProjectReferences = new List<ConsolidatedProjectReference>(),
                AssemblyReferences = new List<AssemblyReference>()
            };

            var projectFile = ResolveProjectFile(projectPath);
            if (projectFile != null)
            {
                var content = await File.ReadAllTextAsync(projectFile, ct);

                // Extract NuGet package references
                var packagePattern = @"<PackageReference\s+Include=""([^""]+)""";
                var packageMatches = System.Text.RegularExpressions.Regex.Matches(content, packagePattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                dependencies.PackageReferences.AddRange(packageMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => new PackageDependency { Name = m.Groups[1].Value, Version = "Unknown", IsDevelopmentDependency = false, UsedBy = new List<string>() }));

                // Extract project references
                var projectRefPattern = @"<ProjectReference\s+Include=""([^""]+)""";
                var projectRefMatches = System.Text.RegularExpressions.Regex.Matches(content, projectRefPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                dependencies.ProjectReferences.AddRange(projectRefMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => new ConsolidatedProjectReference { Name = Path.GetFileNameWithoutExtension(m.Groups[1].Value), ReferenceType = "Project", Path = m.Groups[1].Value }));
            }

            return dependencies;
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingProjectDependenciesFor(_logger, ex, projectPath);
        }

        return null;
    }

    private async Task<ProjectBuildInfo?> AnalyzeProjectBuildAsync(string projectPath, CancellationToken ct)
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

            var projectFile = ResolveProjectFile(projectPath);
            if (projectFile != null)
            {
                var content = await File.ReadAllTextAsync(projectFile, ct);

                // Extract target framework
                var tfPattern = @"<TargetFramework>([^<]+)</TargetFramework>";
                var tfMatch = System.Text.RegularExpressions.Regex.Match(content, tfPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                if (tfMatch.Success)
                    buildInfo.BuildConfigurations.Add($"TargetFramework: {tfMatch.Groups[1].Value}");

                // Extract output path as build artifact
                var outputPathPattern = @"<OutputPath>([^<]+)</OutputPath>";
                var outputPathMatch = System.Text.RegularExpressions.Regex.Match(content, outputPathPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                if (outputPathMatch.Success)
                    buildInfo.BuildArtifacts.Add(outputPathMatch.Groups[1].Value);

                // Add basic build configurations
                buildInfo.BuildConfigurations.Add("Debug");
                buildInfo.BuildConfigurations.Add("Release");

                // Add a basic build step
                buildInfo.BuildSteps.Add(new BuildStep
                {
                    Name = "Build",
                    Command = "dotnet build",
                    Inputs = new List<string> { projectFile },
                    Outputs = new List<string>()
                });
            }

            return buildInfo;
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingProjectBuildInfo(_logger, ex, projectPath);
        }

        return null;
    }

    private async Task<ProjectMetrics?> CalculateProjectMetricsAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var files = await GetProjectFileInventoryAsync(projectPath, ct);

            var csharpFiles = files.Where(f => f.Language == "C#").ToList();
            var totalLines = csharpFiles.Sum(f => f.LineCount);

            return new ProjectMetrics
            {
                TotalFiles = files.Count,
                LinesOfCode = totalLines,
                TypesCount = 0, // Would need more detailed analysis
                MethodsCount = 0, // Would need more detailed analysis
                AverageComplexity = 0, // Would need complexity analysis
                FileMetrics = csharpFiles.Select(f => new FileMetric
                {
                    FilePath = f.Path,
                    LinesOfCode = f.LineCount,
                    Complexity = 1.0, // Simplified
                    QualityScore = 100.0 // Simplified
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            LogErrorCalculatingProjectMetricsFor(_logger, ex, projectPath);
        }

        return null;
    }
}

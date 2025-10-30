using System.Text.RegularExpressions;
using System.Xml.Linq;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for parsing .NET project and solution files
/// </summary>
public partial class ProjectParserService
{
    private static readonly string[] SupportedTargetFrameworks =
    [
        "net6.0", "net7.0", "net8.0", "net9.0",
        "netstandard2.0", "netstandard2.1"
    ];

    /// <summary>
    /// Parse a .csproj file and extract project information
    /// </summary>
    /// <param name="csprojPath">Path to the .csproj file</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Project information</returns>
    /// <exception cref="FileNotFoundException">If the .csproj file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">If the .csproj file is invalid</exception>
    public async Task<CsprojInfo> ParseProjectAsync(string csprojPath, CancellationToken ct = default)
    {
        var absolutePath = Path.GetFullPath(csprojPath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Project file not found: {absolutePath}");
        }

        try
        {
            var content = await File.ReadAllTextAsync(absolutePath, ct);
            var doc = XDocument.Parse(content);

            var projectName = Path.GetFileNameWithoutExtension(absolutePath);

            // Extract PropertyGroup elements
            var propertyGroups = doc.Descendants("PropertyGroup").ToList();

            // Get output type (default to Library if not specified)
            var outputType = GetPropertyValue(propertyGroups, "OutputType") ?? "Library";

            // Get target frameworks
            var targetFrameworks = GetTargetFrameworks(propertyGroups);

            // Get other properties
            var assemblyName = GetPropertyValue(propertyGroups, "AssemblyName");
            var rootNamespace = GetPropertyValue(propertyGroups, "RootNamespace");
            var nullable = GetPropertyValue(propertyGroups, "Nullable")?.Equals("enable", StringComparison.OrdinalIgnoreCase) ?? false;
            var langVersion = GetPropertyValue(propertyGroups, "LangVersion");

            // Parse package references
            var packageReferences = doc.Descendants("PackageReference")
                .Select(pr => new PackageReference
                {
                    Id = pr.Attribute("Include")?.Value ?? throw new InvalidOperationException("PackageReference missing Include attribute"),
                    Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value ?? throw new InvalidOperationException("PackageReference missing Version")
                })
                .ToList();

            // Parse project references
            var projectDir = Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException("Could not determine project directory");
            var projectReferences = doc.Descendants("ProjectReference")
                .Select(pr =>
                {
                    var includePath = pr.Attribute("Include")?.Value ?? throw new InvalidOperationException("ProjectReference missing Include attribute");
                    var referencedPath = Path.GetFullPath(Path.Combine(projectDir, includePath));
                    return new ProjectReference
                    {
                        ProjectPath = referencedPath,
                        ProjectName = Path.GetFileNameWithoutExtension(referencedPath)
                    };
                })
                .ToList();

            return new CsprojInfo
            {
                Name = projectName,
                FilePath = absolutePath,
                OutputType = outputType,
                TargetFrameworks = targetFrameworks,
                AssemblyName = assemblyName,
                RootNamespace = rootNamespace,
                Nullable = nullable,
                LangVersion = langVersion,
                PackageReferences = packageReferences.Count > 0 ? packageReferences : null,
                ProjectReferences = projectReferences.Count > 0 ? projectReferences : null
            };
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException($"Failed to parse project file: {absolutePath}", ex);
        }
    }

    /// <summary>
    /// Parse a .sln file and extract solution information
    /// </summary>
    /// <param name="slnPath">Path to the .sln file</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Solution information</returns>
    /// <exception cref="FileNotFoundException">If the .sln file doesn't exist</exception>
    /// <exception cref="InvalidOperationException">If the .sln file is invalid</exception>
    public async Task<SolutionInfo> ParseSolutionAsync(string slnPath, CancellationToken ct = default)
    {
        var absolutePath = Path.GetFullPath(slnPath);

        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException($"Solution file not found: {absolutePath}");
        }

        try
        {
            var lines = await File.ReadAllLinesAsync(absolutePath, ct);
            var projects = new List<SolutionProject>();
            var configurations = new List<string>();

            var solutionDir = Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException("Could not determine solution directory");

            foreach (var line in lines)
            {
                // Parse project lines
                // Format: Project("{TYPE-GUID}") = "ProjectName", "RelativePath", "{PROJECT-GUID}"
                var projectMatch = ProjectLineRegex().Match(line);
                if (projectMatch.Success)
                {
                    var typeGuid = projectMatch.Groups[1].Value;
                    var name = projectMatch.Groups[2].Value;
                    var path = projectMatch.Groups[3].Value;
                    var projectGuid = projectMatch.Groups[4].Value;

                    // Convert relative path to absolute
                    var absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDir, path));

                    projects.Add(new SolutionProject
                    {
                        Name = name,
                        Path = absoluteProjectPath,
                        ProjectGuid = projectGuid,
                        TypeGuid = typeGuid
                    });
                }

                // Parse configuration lines
                // Format: Debug|Any CPU = Debug|Any CPU
                var configMatch = ConfigLineRegex().Match(line);
                if (configMatch.Success && !configurations.Contains(configMatch.Groups[1].Value))
                {
                    configurations.Add(configMatch.Groups[1].Value);
                }
            }

            return new SolutionInfo
            {
                FilePath = absolutePath,
                Projects = projects,
                Configurations = configurations.Count > 0 ? configurations : null
            };
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException($"Failed to parse solution file: {absolutePath}", ex);
        }
    }

    /// <summary>
    /// Find all .csproj files in a directory (recursively)
    /// </summary>
    /// <param name="directory">Directory to search</param>
    /// <returns>List of absolute paths to .csproj files</returns>
    public Task<IReadOnlyList<string>> FindProjectsAsync(string directory)
    {
        var absoluteDir = Path.GetFullPath(directory);

        if (!Directory.Exists(absoluteDir))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var projects = Directory.GetFiles(absoluteDir, "*.csproj", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(projects);
    }

    /// <summary>
    /// Find a .sln file in a directory (non-recursive, prefers directory name match)
    /// </summary>
    /// <param name="directory">Directory to search</param>
    /// <returns>Absolute path to .sln file, or null if not found</returns>
    public Task<string?> FindSolutionAsync(string directory)
    {
        var absoluteDir = Path.GetFullPath(directory);

        if (!Directory.Exists(absoluteDir))
        {
            return Task.FromResult<string?>(null);
        }

        var solutionFiles = Directory.GetFiles(absoluteDir, "*.sln", SearchOption.TopDirectoryOnly);

        if (solutionFiles.Length == 0)
        {
            return Task.FromResult<string?>(null);
        }

        // Prefer solution file that matches directory name
        var dirName = Path.GetFileName(absoluteDir);
        var matchingName = solutionFiles.FirstOrDefault(s =>
            Path.GetFileNameWithoutExtension(s).Equals(dirName, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<string?>(matchingName ?? solutionFiles[0]);
    }

    /// <summary>
    /// Check if a target framework is compatible with MCPsharp
    /// </summary>
    /// <param name="targetFramework">Target framework moniker (e.g., "net9.0")</param>
    /// <returns>True if compatible</returns>
    public bool IsCompatibleTargetFramework(string targetFramework)
    {
        return SupportedTargetFrameworks.Contains(targetFramework, StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetPropertyValue(List<XElement> propertyGroups, string propertyName)
    {
        return propertyGroups
            .SelectMany(pg => pg.Elements(propertyName))
            .FirstOrDefault()?.Value;
    }

    private static IReadOnlyList<string> GetTargetFrameworks(List<XElement> propertyGroups)
    {
        // Try multi-targeting first (TargetFrameworks)
        var targetFrameworks = GetPropertyValue(propertyGroups, "TargetFrameworks");
        if (!string.IsNullOrWhiteSpace(targetFrameworks))
        {
            return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        // Fall back to single framework (TargetFramework)
        var targetFramework = GetPropertyValue(propertyGroups, "TargetFramework");
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            return [targetFramework];
        }

        // Default to empty list
        return Array.Empty<string>();
    }

    [GeneratedRegex(@"Project\(""\{([^}]+)\}""\)\s*=\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*""\{([^}]+)\}""")]
    private static partial Regex ProjectLineRegex();

    [GeneratedRegex(@"^\s*([^=]+)\s*=\s*[^=]+$")]
    private static partial Regex ConfigLineRegex();
}

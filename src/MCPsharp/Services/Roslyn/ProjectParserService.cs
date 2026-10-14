using System.Xml.Linq;
using MCPsharp.Models.Roslyn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for parsing .csproj files and project information
/// </summary>
public partial class ProjectParserService
{
    private readonly ILogger<ProjectParserService> _logger;

    public ProjectParserService()
        : this(NullLogger<ProjectParserService>.Instance)
    {
    }

    public ProjectParserService(ILogger<ProjectParserService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parse a .csproj file and return project information
    /// </summary>
    public async Task<ProjectInfo?> ParseProjectAsync(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return null;
        }

        try
        {
            var xml = await File.ReadAllTextAsync(projectPath);
            var doc = XDocument.Parse(xml);
            var root = doc.Root;

            if (root == null)
            {
                return null;
            }

            // Get target framework
            var targetFramework = root.Descendants("TargetFramework").FirstOrDefault()?.Value
                               ?? root.Descendants("TargetFrameworks").FirstOrDefault()?.Value
                               ?? "unknown";

            // Get project directory
            var projectDir = Path.GetDirectoryName(projectPath) ?? "";

            // Find all .cs files
            var sourceFiles = Directory.Exists(projectDir)
                ? Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("/obj/", StringComparison.Ordinal) && !f.Contains("/bin/", StringComparison.Ordinal) &&
                               !f.Contains("\\obj\\", StringComparison.Ordinal) && !f.Contains("\\bin\\", StringComparison.Ordinal))
                    .Select(f => Path.GetRelativePath(projectDir, f))
                    .ToList()
                : new List<string>();

            // Get package references
            var references = root.Descendants("PackageReference")
                .Select(pr => $"{pr.Attribute("Include")?.Value} {pr.Attribute("Version")?.Value}")
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .ToList();

            return new ProjectInfo
            {
                Name = Path.GetFileNameWithoutExtension(projectPath),
                Path = projectPath,
                TargetFramework = targetFramework,
                SourceFiles = sourceFiles,
                References = references,
                FileCount = sourceFiles.Count
            };
        }
        catch (Exception ex)
        {
            LogParseFailed(_logger, ex, projectPath);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to parse project file {ProjectPath}")]
    private static partial void LogParseFailed(ILogger logger, Exception exception, string projectPath);
}

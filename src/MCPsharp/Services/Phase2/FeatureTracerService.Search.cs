using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase2;

public partial class FeatureTracerService
{
    /// <summary>
    /// Search for files related to a feature using multiple heuristics
    /// </summary>
    private async Task SearchForFeatureFilesAsync(string normalizedFeature, MutableFeatureComponents components, List<string> dataFlow)
    {
        var searchPatterns = GetFeatureSearchPatterns(normalizedFeature);
        var foundFiles = new HashSet<string>();

        foreach (var pattern in searchPatterns)
        {
            try
            {
                var files = _fileOps.ListFiles(pattern);
                foreach (var file in files.Files.Where(f => !foundFiles.Contains(f.Path)))
                {
                    foundFiles.Add(file.Path);
                    await CategorizeFeatureFileAsync(file, components, normalizedFeature, dataFlow);
                }
            }
            catch (Exception ex)
            {
                LogErrorSearchingPattern(_logger, ex, pattern);
            }
        }

        // Additional symbol-based search
        await SearchForFeatureSymbolsAsync(normalizedFeature, components, dataFlow);
    }

    /// <summary>
    /// Get search patterns for feature-related files
    /// </summary>
    private static string[] GetFeatureSearchPatterns(string normalizedFeature)
    {
        return new[]
        {
            $"**/*{normalizedFeature}*.cs",
            $"**/Controllers/*{normalizedFeature}*.cs",
            $"**/Services/*{normalizedFeature}*.cs",
            $"**/Repositories/*{normalizedFeature}*.cs",
            $"**/Models/*{normalizedFeature}*.cs",
            $"**/Data/*{normalizedFeature}*.cs",
            $"**/Features/{normalizedFeature}/**/*",
            $"**/{normalizedFeature}/**/*",
            $"**/*{normalizedFeature}*.yml",
            $"**/*{normalizedFeature}*.yaml",
            $"**/*{normalizedFeature}*.json",
            $"**/*{normalizedFeature}*.md",
            $"**/Migrations/*{normalizedFeature}*.cs",
            $"**/Tests/**/*{normalizedFeature}*.cs",
            $"**/*Test*{normalizedFeature}*.cs"
        };
    }

    /// <summary>
    /// Categorize a file based on its path and content
    /// </summary>
    private async Task CategorizeFeatureFileAsync(Models.FileInfo file, MutableFeatureComponents components, string normalizedFeature, List<string> dataFlow)
    {
        var relativePath = file.RelativePath;
        var extension = Path.GetExtension(relativePath).ToLowerInvariant();

        try
        {
            if (extension == ".cs")
            {
                await CategorizeCSharpFileAsync(file, components, normalizedFeature, dataFlow);
            }
            else if (extension is ".yml" or ".yaml")
            {
                components.Workflows.Add(relativePath);
            }
            else if (extension == ".json")
            {
                components.Config.Add(relativePath);
            }
            else if (extension == ".md")
            {
                components.Documentation = relativePath;
            }
        }
        catch (Exception ex)
        {
            LogErrorCategorizingFile(_logger, ex, relativePath);
        }
    }

    /// <summary>
    /// Categorize C# files based on their content and structure
    /// </summary>
    private async Task CategorizeCSharpFileAsync(Models.FileInfo file, MutableFeatureComponents components, string normalizedFeature, List<string> dataFlow)
    {
        var relativePath = file.RelativePath;
        var fileName = Path.GetFileNameWithoutExtension(relativePath);

        // Read file content for analysis
        var fileContent = await _fileOps.ReadFileAsync(relativePath);
        if (!fileContent.Success || fileContent.Content == null)
        {
            return;
        }

        var content = fileContent.Content;

        // Check file path patterns first
        if (relativePath.Contains("/Controllers/", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        {
            components.Controller = $"{relativePath}::FindEntryPoint";
            if (!dataFlow.Contains("Controller")) dataFlow.Add("Controller");
        }
        else if (relativePath.Contains("/Services/", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
        {
            components.Service = $"{relativePath}::FindEntryPoint";
            if (!dataFlow.Contains("Service")) dataFlow.Add("Service");
        }
        else if (relativePath.Contains("/Repositories/", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.Contains("/Data/", StringComparison.OrdinalIgnoreCase) ||
                 fileName.EndsWith("Repository", StringComparison.OrdinalIgnoreCase))
        {
            components.Repository = $"{relativePath}::FindEntryPoint";
            if (!dataFlow.Contains("Repository")) dataFlow.Add("Repository");
        }
        else if (relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.Contains("/Entities/", StringComparison.OrdinalIgnoreCase))
        {
            components.Models.Add(relativePath);
            if (!dataFlow.Contains("Models")) dataFlow.Add("Models");
        }
        else if (relativePath.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
        {
            components.Migrations.Add(relativePath);
        }
        else if (relativePath.Contains("/Tests/", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Contains("Test", StringComparison.OrdinalIgnoreCase))
        {
            components.Tests.Add(relativePath);
        }

        // Additional content-based analysis
        AnalyzeCSharpContentForFeature(content, relativePath, components, normalizedFeature);
    }

    /// <summary>
    /// Analyze C# content for feature-related patterns
    /// </summary>
    private static void AnalyzeCSharpContentForFeature(string content, string filePath, MutableFeatureComponents components, string normalizedFeature)
    {
        var lines = content.Split('\n');
        var className = ExtractClassName(content);

        // Look for feature attributes
        if (content.Contains($"[Feature(\"{normalizedFeature}\")]") ||
            content.Contains($"[Feature(\"{normalizedFeature.Replace("-", "")}\")]"))
        {
            // This is explicitly marked as part of the feature
            CategorizeByClassName(className, filePath, components);
        }

        // Look for feature comments
        if (lines.Any(line =>
                line.Contains($"// Feature: {normalizedFeature}") ||
                line.Contains($"// Feature: {normalizedFeature.Replace("-", "")}") ||
                line.Contains($"/* Feature: {normalizedFeature}") ||
                line.Contains($"/* Feature: {normalizedFeature.Replace("-", "")}")))
        {
            CategorizeByClassName(className, filePath, components);
        }
    }

    /// <summary>
    /// Search for symbols related to the feature
    /// </summary>
    private async Task SearchForFeatureSymbolsAsync(string normalizedFeature, MutableFeatureComponents components, List<string> dataFlow)
    {
        try
        {
            // Search for symbols with feature name variations
            var searchTerms = GetFeatureSearchTerms(normalizedFeature);

            foreach (var term in searchTerms)
            {
                var symbols = await _symbolQuery.FindSymbolsAsync(term);

                foreach (var location in symbols.Select(symbol => symbol.File))
                {
                    if (string.IsNullOrEmpty(location)) continue;

                    var fileInfo = new Models.FileInfo
                    {
                        Path = location,
                        RelativePath = Path.GetRelativePath(_rootPath, location)
                    };

                    await CategorizeCSharpFileAsync(fileInfo, components, normalizedFeature, dataFlow);
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorSearchingFeatureSymbols(_logger, ex);
        }
    }

    /// <summary>
    /// Get search terms for feature name variations
    /// </summary>
    private static string[] GetFeatureSearchTerms(string normalizedFeature)
    {
        var baseName = normalizedFeature.Replace("-", "").Replace("_", "");
        var pascalCase = ToPascalCase(normalizedFeature);

        return new[]
        {
            baseName,
            pascalCase,
            pascalCase + "Controller",
            pascalCase + "Service",
            pascalCase + "Repository",
            pascalCase + "Model",
            pascalCase + "ViewModel",
            pascalCase + "Dto"
        };
    }
}

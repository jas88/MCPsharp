using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using MCPsharp.Models;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Phase2;

/// <summary>
/// Mutable version of FeatureComponents for building up feature maps
/// </summary>
public class MutableFeatureComponents
{
    public string? Controller { get; set; }
    public string? Service { get; set; }
    public string? Repository { get; set; }
    public List<string> Models { get; set; } = new();
    public List<string> Migrations { get; set; } = new();
    public List<string> Config { get; set; } = new();
    public List<string> Tests { get; set; } = new();
    public string? Documentation { get; set; }
    public List<string> Workflows { get; set; } = new();

    /// <summary>
    /// Convert to immutable FeatureComponents
    /// </summary>
    public FeatureComponents ToImmutable()
    {
        return new FeatureComponents
        {
            Controller = Controller,
            Service = Service,
            Repository = Repository,
            Models = Models.Count > 0 ? Models : null,
            Migrations = Migrations.Count > 0 ? Migrations : null,
            Config = Config.Count > 0 ? Config : null,
            Tests = Tests.Count > 0 ? Tests : null,
            Documentation = Documentation,
            Workflows = Workflows.Count > 0 ? Workflows : null
        };
    }
}

/// <summary>
/// Implementation of FeatureTracerService for Phase 2
/// Traces features across multiple files and builds dependency graphs
/// </summary>
public partial class FeatureTracerService : IFeatureTracerService
{
    private readonly ILogger<FeatureTracerService> _logger;
    private readonly FileOperationsService _fileOps;
    private readonly SymbolQueryService _symbolQuery;
    private readonly AdvancedReferenceFinderService _referenceFinder;
    private readonly string _rootPath;

    private static readonly char[] WordSeparators = ['-', '_', ' '];

    public FeatureTracerService(
        ILogger<FeatureTracerService> logger,
        FileOperationsService fileOps,
        SymbolQueryService symbolQuery,
        AdvancedReferenceFinderService referenceFinder,
        RoslynWorkspace workspace)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileOps = fileOps ?? throw new ArgumentNullException(nameof(fileOps));
        _symbolQuery = symbolQuery ?? throw new ArgumentNullException(nameof(symbolQuery));
        _referenceFinder = referenceFinder ?? throw new ArgumentNullException(nameof(referenceFinder));

        // Extract root path from file operations service
        // FileOperationsService constructor stores the absolute path
        var projectPathField = typeof(FileOperationsService).GetField("_rootPath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        _rootPath = projectPathField?.GetValue(_fileOps) as string ?? "";
    }

    /// <summary>
    /// Search for all files related to a feature name using multiple heuristics
    /// </summary>
    public async Task<FeatureMap> TraceFeatureAsync(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName))
        {
            throw new ArgumentException("Feature name cannot be null or empty", nameof(featureName));
        }

        LogTracingFeature(_logger, featureName);

        var components = new MutableFeatureComponents();
        var dataFlow = new List<string>();

        // Normalize feature name for searching
        var normalizedFeature = NormalizeFeatureName(featureName);

        // Search for related files using various heuristics
        await SearchForFeatureFilesAsync(normalizedFeature, components, dataFlow);

        var componentCount = GetComponentCount(components);
        LogFeatureTraceCompleted(_logger, featureName, componentCount);

        return new FeatureMap
        {
            FeatureName = featureName,
            Components = components.ToImmutable(),
            DataFlow = dataFlow.Count > 0 ? dataFlow : null
        };
    }

    /// <summary>
    /// Given an entry point, trace all dependencies and find related components
    /// </summary>
    public async Task<FeatureMap> DiscoverFeatureComponentsAsync(string entryPoint)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            throw new ArgumentException("Entry point cannot be null or empty", nameof(entryPoint));
        }

        LogDiscoveringFeatureComponents(_logger, entryPoint);

        var components = new MutableFeatureComponents();
        var dataFlow = new List<string>();

        // Parse entry point (format: "FilePath::SymbolName" or just "SymbolName")
        var (filePath, symbolName) = ParseEntryPoint(entryPoint);

        if (string.IsNullOrEmpty(symbolName))
        {
            throw new ArgumentException($"Invalid entry point format: {entryPoint}", nameof(entryPoint));
        }

        // Find the symbol in the workspace
        var symbols = await _symbolQuery.FindSymbolsAsync(symbolName);
        if (symbols.Count == 0)
        {
            throw new ArgumentException($"Symbol '{symbolName}' not found in workspace", nameof(entryPoint));
        }

        var primarySymbol = symbols[0];

        // Determine feature name from symbol or file path
        var featureName = ExtractFeatureNameFromSymbol(primarySymbol, filePath);

        // Trace dependencies from the entry point
        await TraceDependenciesFromEntryPointAsync(primarySymbol, components, dataFlow);

        // Search for related test files
        FindRelatedTestFiles(components, featureName);

        // Search for related configuration files
        await FindRelatedConfigFilesAsync(components, featureName);

        LogFeatureDiscoveryCompleted(_logger, featureName, entryPoint);

        return new FeatureMap
        {
            FeatureName = featureName,
            Components = components.ToImmutable(),
            DataFlow = dataFlow.Count > 0 ? dataFlow : null
        };
    }

    #region Helper Methods

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

    /// <summary>
    /// Trace dependencies from an entry point symbol
    /// </summary>
    private async Task TraceDependenciesFromEntryPointAsync(SymbolResult entrySymbol, MutableFeatureComponents components, List<string> dataFlow)
    {
        var filePath = entrySymbol.File;
        var symbolName = entrySymbol.Name;

        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        var relativePath = Path.GetRelativePath(_rootPath, filePath);

        // Categorize the entry point
        CategorizeBySymbolName(symbolName, relativePath, components, dataFlow);

        // Find dependencies using call chain analysis
        try
        {
            var callChain = await _referenceFinder.FindCallChainsAsync(symbolName, null, CallDirection.Backward);
            if (callChain != null)
            {
                ProcessCallChain(callChain, components, dataFlow);
            }
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingCallChain(_logger, ex, symbolName);
        }

        // Find type usages
        try
        {
            var typeUsages = await _referenceFinder.FindTypeUsagesAsync(symbolName);
            if (typeUsages != null)
            {
                ProcessTypeUsages(typeUsages, components);
            }
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingTypeUsages(_logger, ex, symbolName);
        }
    }

    /// <summary>
    /// Process call chain results to categorize components
    /// </summary>
    private void ProcessCallChain(CallChainResult callChain, MutableFeatureComponents components, List<string> dataFlow)
    {
        try
        {
            foreach (var path in callChain.Paths)
            {
                foreach (var step in path.Steps)
                {
                    var filePath = step.File;
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var relativePath = Path.GetRelativePath(_rootPath, filePath);
                        CategorizeBySymbolName(step.ToMethod.Name, relativePath, components, dataFlow);
                    }
                }
            }

            LogProcessedCallChains(_logger, callChain.Paths.Count);
        }
        catch (Exception ex)
        {
            LogErrorProcessingCallChain(_logger, ex);
        }
    }

    /// <summary>
    /// Process type usage results to categorize components
    /// </summary>
    private void ProcessTypeUsages(TypeUsageResult typeUsages, MutableFeatureComponents components)
    {
        try
        {
            foreach (var usage in typeUsages.Usages)
            {
                var filePath = usage.File;
                if (!string.IsNullOrEmpty(filePath))
                {
                    var relativePath = Path.GetRelativePath(_rootPath, filePath);
                    var fileName = Path.GetFileNameWithoutExtension(relativePath);

                    // Categorize based on file path and type usage pattern
                    if (relativePath.Contains("/Models/", StringComparison.OrdinalIgnoreCase) ||
                        relativePath.Contains("/Entities/", StringComparison.OrdinalIgnoreCase))
                    {
                        components.Models.Add(relativePath);
                    }
                    else if (relativePath.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase))
                    {
                        components.Migrations.Add(relativePath);
                    }
                    else if (usage.UsageKind == TypeUsageKind.BaseClass || usage.UsageKind == TypeUsageKind.InterfaceImplementation)
                    {
                        if (fileName.EndsWith("Controller", StringComparison.Ordinal))
                        {
                            components.Controller = $"{relativePath}::{fileName}";
                        }
                    }
                    else if (usage.UsageKind == TypeUsageKind.Parameter)
                    {
                        // This is likely a service or repository dependency
                        if (fileName.EndsWith("Service", StringComparison.Ordinal))
                        {
                            components.Service = $"{relativePath}::{fileName}";
                        }
                        else if (fileName.EndsWith("Repository", StringComparison.Ordinal))
                        {
                            components.Repository = $"{relativePath}::{fileName}";
                        }
                    }
                }
            }

            LogProcessedTypeUsages(_logger, typeUsages.Usages.Count);
        }
        catch (Exception ex)
        {
            LogErrorProcessingTypeUsages(_logger, ex);
        }
    }

    /// <summary>
    /// Find related test files for components
    /// </summary>
    private void FindRelatedTestFiles(MutableFeatureComponents components, string featureName)
    {
        var testPatterns = new[]
        {
            $"**/Tests/**/*{featureName}*.cs",
            $"**/*Test*{featureName}*.cs",
            $"**/*{featureName}*Test*.cs"
        };

        var foundTests = new List<string>();

        foreach (var pattern in testPatterns)
        {
            try
            {
                var files = _fileOps.ListFiles(pattern);
                foundTests.AddRange(files.Files.Select(f => f.RelativePath));
            }
            catch (Exception ex)
            {
                LogErrorSearchingTestFiles(_logger, ex, pattern);
            }
        }

        if (foundTests.Count > 0)
        {
            components.Tests = foundTests.Distinct().ToList();
        }
    }

    /// <summary>
    /// Find related configuration files for the feature
    /// </summary>
    private async Task FindRelatedConfigFilesAsync(MutableFeatureComponents components, string featureName)
    {
        var configPatterns = new[]
        {
            $"**/*{featureName}*.json",
            $"**/*{featureName}*.yml",
            $"**/*{featureName}*.yaml",
            $"**/config/*{featureName}*.json",
            $"**/appsettings.*.json"
        };

        var foundConfigs = new List<string>();

        foreach (var pattern in configPatterns)
        {
            try
            {
                var files = _fileOps.ListFiles(pattern);
                foundConfigs.AddRange(files.Files.Select(f => f.RelativePath));
            }
            catch (Exception ex)
            {
                LogErrorSearchingConfigFiles(_logger, ex, pattern);
            }
        }

        // Check appsettings files for feature-specific configuration
        await CheckAppSettingsForFeatureAsync(components, featureName);

        if (foundConfigs.Count > 0)
        {
            components.Config = foundConfigs.Distinct().ToList();
        }
    }

    /// <summary>
    /// Check appsettings files for feature-specific configuration
    /// </summary>
    private async Task CheckAppSettingsForFeatureAsync(MutableFeatureComponents components, string featureName)
    {
        try
        {
            var appSettingsFiles = _fileOps.ListFiles("**/appsettings*.json");

            foreach (var relativePath in appSettingsFiles.Files.Select(file => file.RelativePath))
            {
                var content = await _fileOps.ReadFileAsync(relativePath);

                // Simple check for feature name in configuration
                if (content.Success && content.Content != null &&
                    content.Content.Contains(featureName, StringComparison.OrdinalIgnoreCase))
                {
                    components.Config.Add(relativePath);
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorCheckingAppSettings(_logger, ex);
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Normalize feature name for searching
    /// </summary>
    private static string NormalizeFeatureName(string featureName)
    {
        return featureName.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
    }

    /// <summary>
    /// Convert string to PascalCase
    /// </summary>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var words = input.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Select(word =>
            char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant()));
    }

    /// <summary>
    /// Parse entry point string into file path and symbol name
    /// </summary>
    private static (string? filePath, string? symbolName) ParseEntryPoint(string entryPoint)
    {
        if (entryPoint.Contains("::"))
        {
            var parts = entryPoint.Split("::", StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 2 ? (parts[0], parts[1]) : (null, entryPoint);
        }

        return (null, entryPoint);
    }

    /// <summary>
    /// Extract feature name from symbol and file path
    /// </summary>
    private static string ExtractFeatureNameFromSymbol(SymbolResult symbol, string? filePath)
    {
        // Try to extract from symbol name first
        var symbolName = symbol.Name;
        if (symbolName.EndsWith("Controller", StringComparison.Ordinal))
        {
            return symbolName.Replace("Controller", "").ToLowerInvariant();
        }
        if (symbolName.EndsWith("Service", StringComparison.Ordinal))
        {
            return symbolName.Replace("Service", "").ToLowerInvariant();
        }

        // Fall back to file path analysis
        if (!string.IsNullOrEmpty(filePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            return fileName.ToLowerInvariant();
        }

        return symbolName.ToLowerInvariant();
    }

    /// <summary>
    /// Extract class name from C# content
    /// </summary>
    private static string ExtractClassName(string content)
    {
        var match = ClassNameRegex().Match(content);
        return match.Success ? match.Groups[1].Value : "";
    }

    /// <summary>
    /// Categorize component by class name
    /// </summary>
    private static void CategorizeByClassName(string className, string filePath, MutableFeatureComponents components)
    {
        if (className.EndsWith("Controller", StringComparison.Ordinal))
        {
            components.Controller = $"{filePath}::{className}";
        }
        else if (className.EndsWith("Service", StringComparison.Ordinal))
        {
            components.Service = $"{filePath}::{className}";
        }
        else if (className.EndsWith("Repository", StringComparison.Ordinal))
        {
            components.Repository = $"{filePath}::{className}";
        }
        else
        {
            components.Models.Add(filePath);
        }
    }

    /// <summary>
    /// Categorize component by symbol name
    /// </summary>
    private static void CategorizeBySymbolName(string symbolName, string filePath, MutableFeatureComponents components, List<string> dataFlow)
    {
        if (symbolName.EndsWith("Controller", StringComparison.Ordinal))
        {
            components.Controller = $"{filePath}::{symbolName}";
            if (!dataFlow.Contains("Controller")) dataFlow.Add("Controller");
        }
        else if (symbolName.EndsWith("Service", StringComparison.Ordinal))
        {
            components.Service = $"{filePath}::{symbolName}";
            if (!dataFlow.Contains("Service")) dataFlow.Add("Service");
        }
        else if (symbolName.EndsWith("Repository", StringComparison.Ordinal))
        {
            components.Repository = $"{filePath}::{symbolName}";
            if (!dataFlow.Contains("Repository")) dataFlow.Add("Repository");
        }
    }

    /// <summary>
    /// Count total components in feature map
    /// </summary>
    private static int GetComponentCount(MutableFeatureComponents components)
    {
        var count = 0;
        if (components.Controller != null) count++;
        if (components.Service != null) count++;
        if (components.Repository != null) count++;
        if (components.Models != null) count += components.Models.Count;
        if (components.Migrations != null) count += components.Migrations.Count;
        if (components.Config != null) count += components.Config.Count;
        if (components.Tests != null) count += components.Tests.Count;
        if (components.Documentation != null) count++;
        if (components.Workflows != null) count += components.Workflows.Count;

        return count;
    }

    #endregion

    [GeneratedRegex(@"class\s+(\w+)", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ClassNameRegex();

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracing feature: {FeatureName}")]
    private static partial void LogTracingFeature(ILogger logger, string featureName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feature trace completed for {FeatureName}: found {ComponentCount} components")]
    private static partial void LogFeatureTraceCompleted(ILogger logger, string featureName, int componentCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovering feature components from entry point: {EntryPoint}")]
    private static partial void LogDiscoveringFeatureComponents(ILogger logger, string entryPoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "Feature discovery completed for {FeatureName} from entry point {EntryPoint}")]
    private static partial void LogFeatureDiscoveryCompleted(ILogger logger, string featureName, string entryPoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching pattern {Pattern}")]
    private static partial void LogErrorSearchingPattern(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error categorizing file {FilePath}")]
    private static partial void LogErrorCategorizingFile(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching for feature symbols")]
    private static partial void LogErrorSearchingFeatureSymbols(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error analyzing call chain for {SymbolName}")]
    private static partial void LogErrorAnalyzingCallChain(ILogger logger, Exception exception, string symbolName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error analyzing type usages for {SymbolName}")]
    private static partial void LogErrorAnalyzingTypeUsages(ILogger logger, Exception exception, string symbolName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {PathCount} call chains for feature discovery")]
    private static partial void LogProcessedCallChains(ILogger logger, int pathCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error processing call chain results")]
    private static partial void LogErrorProcessingCallChain(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {UsageCount} type usages for feature discovery")]
    private static partial void LogProcessedTypeUsages(ILogger logger, int usageCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error processing type usage results")]
    private static partial void LogErrorProcessingTypeUsages(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching for test files with pattern {Pattern}")]
    private static partial void LogErrorSearchingTestFiles(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error searching for config files with pattern {Pattern}")]
    private static partial void LogErrorSearchingConfigFiles(ILogger logger, Exception exception, string pattern);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error checking appsettings files for feature configuration")]
    private static partial void LogErrorCheckingAppSettings(ILogger logger, Exception exception);

    #endregion
}

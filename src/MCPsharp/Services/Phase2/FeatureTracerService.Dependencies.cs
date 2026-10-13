using Microsoft.CodeAnalysis;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Phase2;

public partial class FeatureTracerService
{
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
}

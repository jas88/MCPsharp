using MCPsharp.Models;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Phase2;

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

        _rootPath = _fileOps.RootPath;
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
}

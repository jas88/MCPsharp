using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for discovering and loading Roslyn CodeFixProviders from assemblies
/// </summary>
public partial class RoslynCodeFixLoader
{
    private readonly ILogger<RoslynCodeFixLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public RoslynCodeFixLoader(
        ILogger<RoslynCodeFixLoader> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Load all Roslyn code fix providers from a given assembly path
    /// </summary>
    public Task<ImmutableArray<RoslynCodeFixAdapter>> LoadCodeFixProvidersFromAssemblyAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            LogLoadingFromAssembly(_logger, assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                LogAssemblyNotFound(_logger, assemblyPath);
                return Task.FromResult(ImmutableArray<RoslynCodeFixAdapter>.Empty);
            }

            var codeFixProviders = new List<RoslynCodeFixAdapter>();

            // Load assembly
            var assembly = AnalyzerAssemblyLoadContext.LoadAnalyzerAssembly(assemblyPath);

            // Find all types that inherit from CodeFixProvider
            var codeFixTypes = assembly.GetTypes()
                .Where(t => t.IsClass
                    && !t.IsAbstract
                    && typeof(CodeFixProvider).IsAssignableFrom(t))
                .ToList();

            LogFoundProviderTypes(_logger, codeFixTypes.Count, assemblyPath);

            foreach (var codeFixType in codeFixTypes)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Create instance of code fix provider
                    var constructor = codeFixType.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        LogNoParameterlessConstructor(_logger, codeFixType.Name);
                        continue;
                    }

                    var codeFixProvider = (CodeFixProvider)constructor.Invoke(Array.Empty<object>());

                    // Create adapter logger
                    var adapterLogger = _loggerFactory.CreateLogger<RoslynCodeFixAdapter>();

                    // Wrap in adapter
                    var adapter = new RoslynCodeFixAdapter(codeFixProvider, adapterLogger);

                    codeFixProviders.Add(adapter);

                    LogLoadedProvider(_logger, adapter.Id, codeFixType.Name);
                }
                catch (Exception ex)
                {
                    LogErrorLoadingProviderType(_logger, ex, codeFixType.Name);
                }
            }

            LogLoadedProviders(_logger, codeFixProviders.Count, assemblyPath);

            return Task.FromResult(codeFixProviders.ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorLoadingFromAssembly(_logger, ex, assemblyPath);
            return Task.FromResult(ImmutableArray<RoslynCodeFixAdapter>.Empty);
        }
    }

    /// <summary>
    /// Load all Roslyn code fix providers from multiple assembly paths
    /// </summary>
    public async Task<ImmutableArray<RoslynCodeFixAdapter>> LoadCodeFixProvidersFromAssembliesAsync(
        IEnumerable<string> assemblyPaths,
        CancellationToken cancellationToken = default)
    {
        var allCodeFixProviders = new List<RoslynCodeFixAdapter>();

        foreach (var assemblyPath in assemblyPaths)
        {
            var codeFixProviders = await LoadCodeFixProvidersFromAssemblyAsync(assemblyPath, cancellationToken);
            allCodeFixProviders.AddRange(codeFixProviders);
        }

        return allCodeFixProviders.ToImmutableArray();
    }

    /// <summary>
    /// Discover Roslyn code fix provider assemblies in a directory
    /// </summary>
    public ImmutableArray<string> DiscoverCodeFixProviderAssemblies(string directory, bool recursive = true)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                LogDirectoryNotFound(_logger, directory);
                return ImmutableArray<string>.Empty;
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Look for DLL files
            var dllFiles = Directory.GetFiles(directory, "*.dll", searchOption);

            var codeFixProviderAssemblies = new List<string>();

            foreach (var dllFile in dllFiles)
            {
                try
                {
                    // Quick check: load assembly and see if it contains CodeFixProviders
                    var assembly = AnalyzerAssemblyLoadContext.LoadAnalyzerAssembly(dllFile);

                    var hasCodeFixProviders = assembly.GetTypes()
                        .Any(t => t.IsClass
                            && !t.IsAbstract
                            && typeof(CodeFixProvider).IsAssignableFrom(t));

                    if (hasCodeFixProviders)
                    {
                        codeFixProviderAssemblies.Add(dllFile);
                        LogDiscoveredAssembly(_logger, dllFile);
                    }
                }
                catch (Exception ex)
                {
                    // Skip assemblies that can't be loaded or inspected
                    LogCouldNotInspectAssembly(_logger, ex, dllFile);
                }
            }

            LogDiscoveredAssemblies(_logger, codeFixProviderAssemblies.Count, directory);

            return codeFixProviderAssemblies.ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogErrorDiscoveringAssemblies(_logger, ex, directory);
            return ImmutableArray<string>.Empty;
        }
    }

    /// <summary>
    /// Load all Roslyn code fix providers from NuGet packages cache
    /// </summary>
    public async Task<ImmutableArray<RoslynCodeFixAdapter>> LoadCodeFixProvidersFromNuGetCacheAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Common NuGet cache locations
            var nugetCachePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".nuget", "packages"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NuGet", "Cache")
            };

            var allCodeFixProviders = new List<RoslynCodeFixAdapter>();

            foreach (var cachePath in nugetCachePaths)
            {
                if (!Directory.Exists(cachePath))
                    continue;

                LogScanningNuGetCache(_logger, cachePath);

                var assemblyPaths = DiscoverCodeFixProviderAssemblies(cachePath, recursive: true);
                var codeFixProviders = await LoadCodeFixProvidersFromAssembliesAsync(assemblyPaths, cancellationToken);

                allCodeFixProviders.AddRange(codeFixProviders);
            }

            LogLoadedFromNuGetCache(_logger, allCodeFixProviders.Count);

            return allCodeFixProviders.ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogErrorLoadingFromNuGetCache(_logger, ex);
            return ImmutableArray<RoslynCodeFixAdapter>.Empty;
        }
    }

    /// <summary>
    /// Get metadata about a code fix provider assembly without loading it
    /// </summary>
    public CodeFixProviderAssemblyInfo? GetCodeFixProviderAssemblyInfo(string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return null;

            var assembly = AnalyzerAssemblyLoadContext.LoadAnalyzerAssembly(assemblyPath);
            var codeFixTypes = assembly.GetTypes()
                .Where(t => t.IsClass
                    && !t.IsAbstract
                    && typeof(CodeFixProvider).IsAssignableFrom(t))
                .ToList();

            if (codeFixTypes.Count == 0)
                return null;

            // Get fixable diagnostic IDs from each provider
            var allFixableDiagnosticIds = new HashSet<string>();
            foreach (var codeFixType in codeFixTypes)
            {
                try
                {
                    var constructor = codeFixType.GetConstructor(Type.EmptyTypes);
                    if (constructor != null)
                    {
                        var instance = (CodeFixProvider)constructor.Invoke(Array.Empty<object>());
                        foreach (var diagnosticId in instance.FixableDiagnosticIds)
                        {
                            allFixableDiagnosticIds.Add(diagnosticId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Skip if we can't instantiate
                    LogCouldNotInstantiateProvider(_logger, ex, codeFixType.Name);
                }
            }

            return new CodeFixProviderAssemblyInfo
            {
                AssemblyPath = assemblyPath,
                AssemblyName = assembly.GetName().Name ?? "Unknown",
                Version = assembly.GetName().Version ?? new Version(1, 0, 0),
                CodeFixProviderCount = codeFixTypes.Count,
                CodeFixProviderTypeNames = codeFixTypes.Select(t => t.FullName ?? t.Name).ToImmutableArray(),
                FixableDiagnosticIds = allFixableDiagnosticIds.ToImmutableArray()
            };
        }
        catch (Exception ex)
        {
            LogErrorGettingAssemblyInfo(_logger, ex, assemblyPath);
            return null;
        }
    }

    /// <summary>
    /// Match code fix providers to analyzers based on diagnostic IDs
    /// </summary>
    public static ImmutableDictionary<string, ImmutableArray<RoslynCodeFixAdapter>> MatchCodeFixProvidersToAnalyzers(
        ImmutableArray<RoslynCodeFixAdapter> codeFixProviders,
        ImmutableArray<Models.Analyzers.IAnalyzer> analyzers)
    {
        var matches = new Dictionary<string, List<RoslynCodeFixAdapter>>();

        foreach (var analyzer in analyzers)
        {
            var matchingProviders = new List<RoslynCodeFixAdapter>();

            var analyzerRules = analyzer.GetRules();
            foreach (var rule in analyzerRules)
            {
                matchingProviders.AddRange(codeFixProviders.Where(codeFixProvider => codeFixProvider.CanFix(rule.Id)));
            }

            if (matchingProviders.Count > 0)
            {
                matches[analyzer.Id] = matchingProviders.Distinct().ToList();
            }
        }

        return matches.ToImmutableDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToImmutableArray());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading Roslyn code fix providers from assembly: {AssemblyPath}")]
    private static partial void LogLoadingFromAssembly(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Assembly not found: {AssemblyPath}")]
    private static partial void LogAssemblyNotFound(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} code fix provider types in assembly {AssemblyPath}")]
    private static partial void LogFoundProviderTypes(ILogger logger, int count, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Code fix provider {CodeFixType} does not have parameterless constructor")]
    private static partial void LogNoParameterlessConstructor(ILogger logger, string codeFixType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded Roslyn code fix provider: {ProviderId} ({ProviderType})")]
    private static partial void LogLoadedProvider(ILogger logger, string providerId, string providerType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading code fix provider type {CodeFixType}")]
    private static partial void LogErrorLoadingProviderType(ILogger logger, Exception exception, string codeFixType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully loaded {Count} Roslyn code fix providers from {AssemblyPath}")]
    private static partial void LogLoadedProviders(ILogger logger, int count, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading code fix providers from assembly {AssemblyPath}")]
    private static partial void LogErrorLoadingFromAssembly(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Directory not found: {Directory}")]
    private static partial void LogDirectoryNotFound(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discovered code fix provider assembly: {AssemblyPath}")]
    private static partial void LogDiscoveredAssembly(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Could not inspect assembly {AssemblyPath}")]
    private static partial void LogCouldNotInspectAssembly(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered {Count} code fix provider assemblies in {Directory}")]
    private static partial void LogDiscoveredAssemblies(ILogger logger, int count, string directory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error discovering code fix provider assemblies in {Directory}")]
    private static partial void LogErrorDiscoveringAssemblies(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanning NuGet cache for code fix providers: {CachePath}")]
    private static partial void LogScanningNuGetCache(ILogger logger, string cachePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} code fix providers from NuGet cache")]
    private static partial void LogLoadedFromNuGetCache(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading code fix providers from NuGet cache")]
    private static partial void LogErrorLoadingFromNuGetCache(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Could not instantiate code fix provider {CodeFixType}")]
    private static partial void LogCouldNotInstantiateProvider(ILogger logger, Exception exception, string codeFixType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting code fix provider assembly info for {AssemblyPath}")]
    private static partial void LogErrorGettingAssemblyInfo(ILogger logger, Exception exception, string assemblyPath);
}

/// <summary>
/// Information about a code fix provider assembly
/// </summary>
public class CodeFixProviderAssemblyInfo
{
    public required string AssemblyPath { get; init; }
    public required string AssemblyName { get; init; }
    public required Version Version { get; init; }
    public required int CodeFixProviderCount { get; init; }
    public required ImmutableArray<string> CodeFixProviderTypeNames { get; init; }
    public required ImmutableArray<string> FixableDiagnosticIds { get; init; }
}

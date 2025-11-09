using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for discovering and loading Roslyn CodeFixProviders from assemblies
/// </summary>
public class RoslynCodeFixLoader
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
    public async Task<ImmutableArray<RoslynCodeFixAdapter>> LoadCodeFixProvidersFromAssemblyAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading Roslyn code fix providers from assembly: {AssemblyPath}", assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                _logger.LogError("Assembly not found: {AssemblyPath}", assemblyPath);
                return ImmutableArray<RoslynCodeFixAdapter>.Empty;
            }

            var codeFixProviders = new List<RoslynCodeFixAdapter>();

            // Load assembly
            var assembly = Assembly.LoadFrom(assemblyPath);

            // Find all types that inherit from CodeFixProvider
            var codeFixTypes = assembly.GetTypes()
                .Where(t => t.IsClass
                    && !t.IsAbstract
                    && typeof(CodeFixProvider).IsAssignableFrom(t))
                .ToList();

            _logger.LogInformation("Found {Count} code fix provider types in assembly {AssemblyPath}",
                codeFixTypes.Count, assemblyPath);

            foreach (var codeFixType in codeFixTypes)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Create instance of code fix provider
                    var constructor = codeFixType.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        _logger.LogWarning("Code fix provider {CodeFixType} does not have parameterless constructor",
                            codeFixType.Name);
                        continue;
                    }

                    var codeFixProvider = (CodeFixProvider)constructor.Invoke(Array.Empty<object>());

                    // Create adapter logger
                    var adapterLogger = _loggerFactory.CreateLogger<RoslynCodeFixAdapter>();

                    // Wrap in adapter
                    var adapter = new RoslynCodeFixAdapter(codeFixProvider, adapterLogger);

                    codeFixProviders.Add(adapter);

                    _logger.LogDebug("Loaded Roslyn code fix provider: {ProviderId} ({ProviderType})",
                        adapter.Id, codeFixType.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading code fix provider type {CodeFixType}", codeFixType.Name);
                }
            }

            _logger.LogInformation("Successfully loaded {Count} Roslyn code fix providers from {AssemblyPath}",
                codeFixProviders.Count, assemblyPath);

            return codeFixProviders.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading code fix providers from assembly {AssemblyPath}", assemblyPath);
            return ImmutableArray<RoslynCodeFixAdapter>.Empty;
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
                _logger.LogError("Directory not found: {Directory}", directory);
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
                    var assembly = Assembly.LoadFrom(dllFile);

                    var hasCodeFixProviders = assembly.GetTypes()
                        .Any(t => t.IsClass
                            && !t.IsAbstract
                            && typeof(CodeFixProvider).IsAssignableFrom(t));

                    if (hasCodeFixProviders)
                    {
                        codeFixProviderAssemblies.Add(dllFile);
                        _logger.LogDebug("Discovered code fix provider assembly: {AssemblyPath}", dllFile);
                    }
                }
                catch (Exception ex)
                {
                    // Skip assemblies that can't be loaded or inspected
                    _logger.LogTrace(ex, "Could not inspect assembly {AssemblyPath}", dllFile);
                }
            }

            _logger.LogInformation("Discovered {Count} code fix provider assemblies in {Directory}",
                codeFixProviderAssemblies.Count, directory);

            return codeFixProviderAssemblies.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering code fix provider assemblies in {Directory}", directory);
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

                _logger.LogInformation("Scanning NuGet cache for code fix providers: {CachePath}", cachePath);

                var assemblyPaths = DiscoverCodeFixProviderAssemblies(cachePath, recursive: true);
                var codeFixProviders = await LoadCodeFixProvidersFromAssembliesAsync(assemblyPaths, cancellationToken);

                allCodeFixProviders.AddRange(codeFixProviders);
            }

            _logger.LogInformation("Loaded {Count} code fix providers from NuGet cache", allCodeFixProviders.Count);

            return allCodeFixProviders.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading code fix providers from NuGet cache");
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

            var assembly = Assembly.LoadFrom(assemblyPath);
            var codeFixTypes = assembly.GetTypes()
                .Where(t => t.IsClass
                    && !t.IsAbstract
                    && typeof(CodeFixProvider).IsAssignableFrom(t))
                .ToList();

            if (!codeFixTypes.Any())
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
                catch
                {
                    // Skip if we can't instantiate
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
            _logger.LogError(ex, "Error getting code fix provider assembly info for {AssemblyPath}", assemblyPath);
            return null;
        }
    }

    /// <summary>
    /// Match code fix providers to analyzers based on diagnostic IDs
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<RoslynCodeFixAdapter>> MatchCodeFixProvidersToAnalyzers(
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
                foreach (var codeFixProvider in codeFixProviders)
                {
                    if (codeFixProvider.CanFix(rule.Id))
                    {
                        matchingProviders.Add(codeFixProvider);
                    }
                }
            }

            if (matchingProviders.Any())
            {
                matches[analyzer.Id] = matchingProviders.Distinct().ToList();
            }
        }

        return matches.ToImmutableDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToImmutableArray());
    }
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

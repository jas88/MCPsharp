using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for discovering and loading Roslyn DiagnosticAnalyzers from assemblies
/// </summary>
public class RoslynAnalyzerLoader
{
    private readonly ILogger<RoslynAnalyzerLoader> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public RoslynAnalyzerLoader(
        ILogger<RoslynAnalyzerLoader> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <summary>
    /// Load all Roslyn analyzers from a given assembly path
    /// </summary>
    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromAssemblyAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading Roslyn analyzers from assembly: {AssemblyPath}", assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                _logger.LogError("Assembly not found: {AssemblyPath}", assemblyPath);
                return ImmutableArray<IAnalyzer>.Empty;
            }

            var analyzers = new List<IAnalyzer>();

            // Load assembly
            var assembly = Assembly.LoadFrom(assemblyPath);

            // Find all types that inherit from DiagnosticAnalyzer
            var analyzerTypes = assembly.GetTypes()
                .Where(t => t.IsClass
                    && !t.IsAbstract
                    && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .ToList();

            _logger.LogInformation("Found {Count} analyzer types in assembly {AssemblyPath}",
                analyzerTypes.Count, assemblyPath);

            foreach (var analyzerType in analyzerTypes)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Create instance of analyzer
                    var constructor = analyzerType.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        _logger.LogWarning("Analyzer {AnalyzerType} does not have parameterless constructor",
                            analyzerType.Name);
                        continue;
                    }

                    var roslynAnalyzer = (DiagnosticAnalyzer)constructor.Invoke(Array.Empty<object>());

                    // Create adapter logger
                    var adapterLogger = _loggerFactory.CreateLogger<RoslynAnalyzerAdapter>();

                    // Wrap in adapter
                    var adapter = new RoslynAnalyzerAdapter(roslynAnalyzer, adapterLogger);

                    analyzers.Add(adapter);

                    _logger.LogDebug("Loaded Roslyn analyzer: {AnalyzerId} ({AnalyzerType})",
                        adapter.Id, analyzerType.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading analyzer type {AnalyzerType}", analyzerType.Name);
                }
            }

            _logger.LogInformation("Successfully loaded {Count} Roslyn analyzers from {AssemblyPath}",
                analyzers.Count, assemblyPath);

            return analyzers.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzers from assembly {AssemblyPath}", assemblyPath);
            return ImmutableArray<IAnalyzer>.Empty;
        }
    }

    /// <summary>
    /// Load all Roslyn analyzers from multiple assembly paths
    /// </summary>
    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromAssembliesAsync(
        IEnumerable<string> assemblyPaths,
        CancellationToken cancellationToken = default)
    {
        var allAnalyzers = new List<IAnalyzer>();

        foreach (var assemblyPath in assemblyPaths)
        {
            var analyzers = await LoadAnalyzersFromAssemblyAsync(assemblyPath, cancellationToken);
            allAnalyzers.AddRange(analyzers);
        }

        return allAnalyzers.ToImmutableArray();
    }

    /// <summary>
    /// Discover Roslyn analyzer assemblies in a directory
    /// </summary>
    public ImmutableArray<string> DiscoverAnalyzerAssemblies(string directory, bool recursive = true)
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

            var analyzerAssemblies = new List<string>();

            foreach (var dllFile in dllFiles)
            {
                try
                {
                    // Quick check: load assembly and see if it contains DiagnosticAnalyzers
                    var assembly = Assembly.LoadFrom(dllFile);

                    var hasAnalyzers = assembly.GetTypes()
                        .Any(t => t.IsClass
                            && !t.IsAbstract
                            && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));

                    if (hasAnalyzers)
                    {
                        analyzerAssemblies.Add(dllFile);
                        _logger.LogDebug("Discovered analyzer assembly: {AssemblyPath}", dllFile);
                    }
                }
                catch (Exception ex)
                {
                    // Skip assemblies that can't be loaded or inspected
                    _logger.LogTrace(ex, "Could not inspect assembly {AssemblyPath}", dllFile);
                }
            }

            _logger.LogInformation("Discovered {Count} analyzer assemblies in {Directory}",
                analyzerAssemblies.Count, directory);

            return analyzerAssemblies.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering analyzer assemblies in {Directory}", directory);
            return ImmutableArray<string>.Empty;
        }
    }

    /// <summary>
    /// Load all Roslyn analyzers from NuGet packages cache
    /// </summary>
    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromNuGetCacheAsync(
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

            var allAnalyzers = new List<IAnalyzer>();

            foreach (var cachePath in nugetCachePaths)
            {
                if (!Directory.Exists(cachePath))
                    continue;

                _logger.LogInformation("Scanning NuGet cache for analyzers: {CachePath}", cachePath);

                // Look for analyzer DLLs in the analyzers subdirectory
                var analyzerPattern = Path.Combine(cachePath, "**", "analyzers", "**", "*.dll");

                var assemblyPaths = DiscoverAnalyzerAssemblies(cachePath, recursive: true);
                var analyzers = await LoadAnalyzersFromAssembliesAsync(assemblyPaths, cancellationToken);

                allAnalyzers.AddRange(analyzers);
            }

            _logger.LogInformation("Loaded {Count} analyzers from NuGet cache", allAnalyzers.Count);

            return allAnalyzers.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading analyzers from NuGet cache");
            return ImmutableArray<IAnalyzer>.Empty;
        }
    }

    /// <summary>
    /// Get metadata about an analyzer assembly without loading it
    /// </summary>
    public AnalyzerAssemblyInfo? GetAnalyzerAssemblyInfo(string assemblyPath)
    {
        try
        {
            if (!File.Exists(assemblyPath))
                return null;

            var assembly = Assembly.LoadFrom(assemblyPath);
            var analyzerTypes = assembly.GetTypes()
                .Where(t => t.IsClass
                    && !t.IsAbstract
                    && typeof(DiagnosticAnalyzer).IsAssignableFrom(t))
                .ToList();

            if (!analyzerTypes.Any())
                return null;

            return new AnalyzerAssemblyInfo
            {
                AssemblyPath = assemblyPath,
                AssemblyName = assembly.GetName().Name ?? "Unknown",
                Version = assembly.GetName().Version ?? new Version(1, 0, 0),
                AnalyzerCount = analyzerTypes.Count,
                AnalyzerTypeNames = analyzerTypes.Select(t => t.FullName ?? t.Name).ToImmutableArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting analyzer assembly info for {AssemblyPath}", assemblyPath);
            return null;
        }
    }
}

/// <summary>
/// Information about an analyzer assembly
/// </summary>
public class AnalyzerAssemblyInfo
{
    public required string AssemblyPath { get; init; }
    public required string AssemblyName { get; init; }
    public required Version Version { get; init; }
    public required int AnalyzerCount { get; init; }
    public required ImmutableArray<string> AnalyzerTypeNames { get; init; }
}

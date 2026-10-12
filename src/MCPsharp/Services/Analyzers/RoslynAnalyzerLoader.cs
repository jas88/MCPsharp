using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for discovering and loading Roslyn DiagnosticAnalyzers from assemblies
/// </summary>
public partial class RoslynAnalyzerLoader
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
    public virtual Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromAssemblyAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            LogLoadingFromAssembly(_logger, assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                LogAssemblyNotFound(_logger, assemblyPath);
                return Task.FromResult(ImmutableArray<IAnalyzer>.Empty);
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

            LogFoundAnalyzerTypes(_logger, analyzerTypes.Count, assemblyPath);

            foreach (var analyzerType in analyzerTypes)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Create instance of analyzer
                    var constructor = analyzerType.GetConstructor(Type.EmptyTypes);
                    if (constructor == null)
                    {
                        LogNoParameterlessConstructor(_logger, analyzerType.Name);
                        continue;
                    }

                    var roslynAnalyzer = (DiagnosticAnalyzer)constructor.Invoke(Array.Empty<object>());

                    // Create adapter logger
                    var adapterLogger = _loggerFactory.CreateLogger<RoslynAnalyzerAdapter>();

                    // Wrap in adapter
                    var adapter = new RoslynAnalyzerAdapter(roslynAnalyzer, adapterLogger);

                    analyzers.Add(adapter);

                    LogLoadedAnalyzer(_logger, adapter.Id, analyzerType.Name);
                }
                catch (Exception ex)
                {
                    LogErrorLoadingAnalyzerType(_logger, ex, analyzerType.Name);
                }
            }

            LogLoadedAnalyzers(_logger, analyzers.Count, assemblyPath);

            return Task.FromResult(analyzers.ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorLoadingFromAssembly(_logger, ex, assemblyPath);
            return Task.FromResult(ImmutableArray<IAnalyzer>.Empty);
        }
    }

    /// <summary>
    /// Load all Roslyn analyzers from multiple assembly paths
    /// </summary>
    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromAssembliesAsync(
        IEnumerable<string> assemblyPaths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
    public virtual ImmutableArray<string> DiscoverAnalyzerAssemblies(string directory, bool recursive = true)
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
                        LogDiscoveredAssembly(_logger, dllFile);
                    }
                }
                catch (Exception ex)
                {
                    // Skip assemblies that can't be loaded or inspected
                    LogCouldNotInspectAssembly(_logger, ex, dllFile);
                }
            }

            LogDiscoveredAssemblies(_logger, analyzerAssemblies.Count, directory);

            return analyzerAssemblies.ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogErrorDiscoveringAssemblies(_logger, ex, directory);
            return ImmutableArray<string>.Empty;
        }
    }

    /// <summary>
    /// Load all Roslyn analyzers from NuGet packages cache
    /// </summary>
    public async Task<ImmutableArray<IAnalyzer>> LoadAnalyzersFromNuGetCacheAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

                LogScanningNuGetCache(_logger, cachePath);

                // Look for analyzer DLLs in the analyzers subdirectory

                var assemblyPaths = DiscoverAnalyzerAssemblies(cachePath, recursive: true);
                var analyzers = await LoadAnalyzersFromAssembliesAsync(assemblyPaths, cancellationToken);

                allAnalyzers.AddRange(analyzers);
            }

            LogLoadedFromNuGetCache(_logger, allAnalyzers.Count);

            return allAnalyzers.ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogErrorLoadingFromNuGetCache(_logger, ex);
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

            if (analyzerTypes.Count == 0)
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
            LogErrorGettingAssemblyInfo(_logger, ex, assemblyPath);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading Roslyn analyzers from assembly: {AssemblyPath}")]
    private static partial void LogLoadingFromAssembly(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Assembly not found: {AssemblyPath}")]
    private static partial void LogAssemblyNotFound(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Found {Count} analyzer types in assembly {AssemblyPath}")]
    private static partial void LogFoundAnalyzerTypes(ILogger logger, int count, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerType} does not have parameterless constructor")]
    private static partial void LogNoParameterlessConstructor(ILogger logger, string analyzerType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded Roslyn analyzer: {AnalyzerId} ({AnalyzerType})")]
    private static partial void LogLoadedAnalyzer(ILogger logger, string analyzerId, string analyzerType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzer type {AnalyzerType}")]
    private static partial void LogErrorLoadingAnalyzerType(ILogger logger, Exception exception, string analyzerType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully loaded {Count} Roslyn analyzers from {AssemblyPath}")]
    private static partial void LogLoadedAnalyzers(ILogger logger, int count, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from assembly {AssemblyPath}")]
    private static partial void LogErrorLoadingFromAssembly(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Directory not found: {Directory}")]
    private static partial void LogDirectoryNotFound(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Discovered analyzer assembly: {AssemblyPath}")]
    private static partial void LogDiscoveredAssembly(ILogger logger, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Could not inspect assembly {AssemblyPath}")]
    private static partial void LogCouldNotInspectAssembly(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered {Count} analyzer assemblies in {Directory}")]
    private static partial void LogDiscoveredAssemblies(ILogger logger, int count, string directory);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error discovering analyzer assemblies in {Directory}")]
    private static partial void LogErrorDiscoveringAssemblies(ILogger logger, Exception exception, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanning NuGet cache for analyzers: {CachePath}")]
    private static partial void LogScanningNuGetCache(ILogger logger, string cachePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Count} analyzers from NuGet cache")]
    private static partial void LogLoadedFromNuGetCache(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from NuGet cache")]
    private static partial void LogErrorLoadingFromNuGetCache(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting analyzer assembly info for {AssemblyPath}")]
    private static partial void LogErrorGettingAssemblyInfo(ILogger logger, Exception exception, string assemblyPath);
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

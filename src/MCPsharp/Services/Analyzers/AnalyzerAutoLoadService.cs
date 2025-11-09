using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;
using System.Text.Json;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Service for automatically discovering and loading Roslyn analyzers from standard locations
/// </summary>
public class AnalyzerAutoLoadService
{
    private readonly ILogger<AnalyzerAutoLoadService> _logger;
    private readonly RoslynAnalyzerLoader _loader;
    private readonly IAnalyzerRegistry _registry;
    private readonly AnalyzerAutoLoadConfiguration _configuration;
    private bool _hasAutoLoaded;
    private readonly object _lock = new();
    private ImmutableArray<IAnalyzer> _autoLoadedAnalyzers = ImmutableArray<IAnalyzer>.Empty;

    public AnalyzerAutoLoadService(
        ILogger<AnalyzerAutoLoadService> logger,
        RoslynAnalyzerLoader loader,
        IAnalyzerRegistry registry,
        AnalyzerAutoLoadConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _configuration = configuration ?? AnalyzerAutoLoadConfiguration.Default;
    }

    /// <summary>
    /// Auto-load analyzers if not already loaded
    /// </summary>
    public async Task<AnalyzerAutoLoadResult> EnsureAnalyzersLoadedAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_hasAutoLoaded)
            {
                return new AnalyzerAutoLoadResult
                {
                    Success = true,
                    AlreadyLoaded = true,
                    LoadedAnalyzers = _autoLoadedAnalyzers,
                    Message = "Analyzers already auto-loaded"
                };
            }
        }

        if (!_configuration.AutoLoadEnabled)
        {
            _logger.LogInformation("Auto-loading is disabled in configuration");
            return new AnalyzerAutoLoadResult
            {
                Success = true,
                AlreadyLoaded = true,
                LoadedAnalyzers = ImmutableArray<IAnalyzer>.Empty,
                Message = "Auto-loading is disabled"
            };
        }

        return await LoadAnalyzersAsync(cancellationToken);
    }

    /// <summary>
    /// Load analyzers from configured sources
    /// </summary>
    private async Task<AnalyzerAutoLoadResult> LoadAnalyzersAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var loadedAnalyzers = new List<IAnalyzer>();
        var errors = new List<string>();

        try
        {
            _logger.LogInformation("Starting auto-load of Roslyn analyzers...");

            // Load from NuGet cache
            if (_configuration.AnalyzerSources.Contains("nuget_cache"))
            {
                try
                {
                    var nugetAnalyzers = await LoadFromNuGetCacheAsync(cancellationToken);
                    loadedAnalyzers.AddRange(nugetAnalyzers);
                    _logger.LogInformation("Loaded {Count} analyzers from NuGet cache", nugetAnalyzers.Length);
                }
                catch (Exception ex)
                {
                    var error = $"Error loading from NuGet cache: {ex.Message}";
                    _logger.LogWarning(ex, error);
                    errors.Add(error);
                }
            }

            // Load built-in providers (already registered in BuiltInCodeFixRegistry)
            if (_configuration.AnalyzerSources.Contains("builtin_providers"))
            {
                _logger.LogInformation("Built-in code fix providers are automatically registered via BuiltInCodeFixRegistry");
            }

            // Load from custom paths
            foreach (var customPath in _configuration.CustomAnalyzerPaths)
            {
                try
                {
                    if (Directory.Exists(customPath))
                    {
                        var assemblyPaths = _loader.DiscoverAnalyzerAssemblies(customPath, recursive: true);
                        foreach (var assemblyPath in assemblyPaths)
                        {
                            var customAnalyzers = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath, cancellationToken);
                            loadedAnalyzers.AddRange(customAnalyzers);
                        }
                        _logger.LogInformation("Loaded {Count} analyzers from custom path: {Path}",
                            loadedAnalyzers.Count, customPath);
                    }
                    else if (File.Exists(customPath))
                    {
                        var customAnalyzers = await _loader.LoadAnalyzersFromAssemblyAsync(customPath, cancellationToken);
                        loadedAnalyzers.AddRange(customAnalyzers);
                        _logger.LogInformation("Loaded {Count} analyzers from custom assembly: {Path}",
                            customAnalyzers.Length, customPath);
                    }
                    else
                    {
                        var warning = $"Custom analyzer path not found: {customPath}";
                        _logger.LogWarning(warning);
                        errors.Add(warning);
                    }
                }
                catch (Exception ex)
                {
                    var error = $"Error loading from custom path '{customPath}': {ex.Message}";
                    _logger.LogWarning(ex, error);
                    errors.Add(error);
                }
            }

            // Filter excluded analyzers
            if (_configuration.ExcludeAnalyzers.Any())
            {
                var beforeCount = loadedAnalyzers.Count;
                loadedAnalyzers = loadedAnalyzers
                    .Where(a => !_configuration.ExcludeAnalyzers.Contains(a.Id))
                    .ToList();
                _logger.LogInformation("Filtered out {Count} excluded analyzers", beforeCount - loadedAnalyzers.Count);
            }

            // Register all loaded analyzers
            foreach (var analyzer in loadedAnalyzers)
            {
                try
                {
                    await _registry.RegisterAnalyzerAsync(analyzer, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error registering analyzer: {AnalyzerId}", analyzer.Id);
                    errors.Add($"Failed to register analyzer '{analyzer.Id}': {ex.Message}");
                }
            }

            lock (_lock)
            {
                _hasAutoLoaded = true;
                _autoLoadedAnalyzers = loadedAnalyzers.ToImmutableArray();
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Auto-load completed: {Count} analyzers loaded in {Duration}ms",
                loadedAnalyzers.Count, duration.TotalMilliseconds);

            return new AnalyzerAutoLoadResult
            {
                Success = true,
                LoadedAnalyzers = loadedAnalyzers.ToImmutableArray(),
                LoadDuration = duration,
                Errors = errors.ToImmutableArray(),
                Message = $"Successfully auto-loaded {loadedAnalyzers.Count} analyzers"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during analyzer auto-load");
            return new AnalyzerAutoLoadResult
            {
                Success = false,
                LoadedAnalyzers = loadedAnalyzers.ToImmutableArray(),
                LoadDuration = DateTime.UtcNow - startTime,
                Errors = errors.Concat(new[] { $"Fatal error: {ex.Message}" }).ToImmutableArray(),
                Message = $"Auto-load failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Load analyzers from NuGet package cache
    /// </summary>
    private async Task<ImmutableArray<IAnalyzer>> LoadFromNuGetCacheAsync(CancellationToken cancellationToken)
    {
        var nugetCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget", "packages");

        if (!Directory.Exists(nugetCachePath))
        {
            _logger.LogWarning("NuGet cache directory not found: {Path}", nugetCachePath);
            return ImmutableArray<IAnalyzer>.Empty;
        }

        var analyzers = new List<IAnalyzer>();

        // Known analyzer packages with their standard paths
        var knownPackages = new Dictionary<string, string[]>
        {
            ["microsoft.codeanalysis.netanalyzers"] = new[] { "analyzers/dotnet/cs" },
            ["roslynator.analyzers"] = new[] { "analyzers/dotnet/cs" },
            ["sonaranalyzer.csharp"] = new[] { "analyzers" },
            ["errorprone.net.codefixes"] = new[] { "analyzers/dotnet/cs" },
            ["errorprone.net.structs"] = new[] { "analyzers/dotnet/cs" },
            ["asyncfixer"] = new[] { "analyzers/dotnet/cs" },
            ["meziantou.analyzer"] = new[] { "analyzers/dotnet/cs" }
        };

        foreach (var (packageName, relativePaths) in knownPackages)
        {
            try
            {
                var packagePath = Path.Combine(nugetCachePath, packageName);
                if (!Directory.Exists(packagePath))
                    continue;

                // Find latest version directory
                var versions = Directory.GetDirectories(packagePath)
                    .Select(d => new DirectoryInfo(d).Name)
                    .OrderByDescending(v => v)
                    .ToList();

                if (!versions.Any())
                    continue;

                var latestVersion = versions.First();
                var versionPath = Path.Combine(packagePath, latestVersion);

                // Try each relative path
                foreach (var relativePath in relativePaths)
                {
                    var analyzerPath = Path.Combine(versionPath, relativePath);
                    if (!Directory.Exists(analyzerPath))
                        continue;

                    var assemblyPaths = _loader.DiscoverAnalyzerAssemblies(analyzerPath, recursive: true);
                    foreach (var assemblyPath in assemblyPaths)
                    {
                        var packageAnalyzers = await _loader.LoadAnalyzersFromAssemblyAsync(assemblyPath, cancellationToken);
                        analyzers.AddRange(packageAnalyzers);
                    }
                    _logger.LogDebug("Loaded {Count} analyzers from {Package} v{Version}",
                        analyzers.Count, packageName, latestVersion);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading analyzers from package: {Package}", packageName);
            }
        }

        return analyzers.ToImmutableArray();
    }

    /// <summary>
    /// Reset auto-load state (for testing)
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _hasAutoLoaded = false;
            _autoLoadedAnalyzers = ImmutableArray<IAnalyzer>.Empty;
        }
    }

    /// <summary>
    /// Get auto-load status
    /// </summary>
    public AnalyzerAutoLoadStatus GetStatus()
    {
        lock (_lock)
        {
            return new AnalyzerAutoLoadStatus
            {
                HasAutoLoaded = _hasAutoLoaded,
                AutoLoadedCount = _autoLoadedAnalyzers.Length,
                Configuration = _configuration
            };
        }
    }

    /// <summary>
    /// Load configuration from file
    /// </summary>
    public static AnalyzerAutoLoadConfiguration LoadConfiguration(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return AnalyzerAutoLoadConfiguration.Default;

            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<AnalyzerAutoLoadConfiguration>(json);
            return config ?? AnalyzerAutoLoadConfiguration.Default;
        }
        catch (Exception)
        {
            return AnalyzerAutoLoadConfiguration.Default;
        }
    }
}

/// <summary>
/// Configuration for analyzer auto-loading
/// </summary>
public record AnalyzerAutoLoadConfiguration
{
    public bool AutoLoadEnabled { get; init; } = true;
    public bool LoadOnStartup { get; init; } = false; // false = lazy load on first use
    public ImmutableArray<string> AnalyzerSources { get; init; } = ImmutableArray.Create("nuget_cache", "builtin_providers");
    public ImmutableArray<string> ExcludeAnalyzers { get; init; } = ImmutableArray<string>.Empty;
    public ImmutableArray<string> CustomAnalyzerPaths { get; init; } = ImmutableArray<string>.Empty;

    public static readonly AnalyzerAutoLoadConfiguration Default = new()
    {
        AutoLoadEnabled = true,
        LoadOnStartup = false,
        AnalyzerSources = ImmutableArray.Create("nuget_cache", "builtin_providers"),
        ExcludeAnalyzers = ImmutableArray<string>.Empty,
        CustomAnalyzerPaths = ImmutableArray<string>.Empty
    };
}

/// <summary>
/// Result of auto-loading analyzers
/// </summary>
public record AnalyzerAutoLoadResult
{
    public required bool Success { get; init; }
    public bool AlreadyLoaded { get; init; }
    public ImmutableArray<IAnalyzer> LoadedAnalyzers { get; init; } = ImmutableArray<IAnalyzer>.Empty;
    public TimeSpan LoadDuration { get; init; }
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;
    public string? Message { get; init; }
}

/// <summary>
/// Current auto-load status
/// </summary>
public record AnalyzerAutoLoadStatus
{
    public bool HasAutoLoaded { get; init; }
    public int AutoLoadedCount { get; init; }
    public AnalyzerAutoLoadConfiguration Configuration { get; init; } = AnalyzerAutoLoadConfiguration.Default;
}

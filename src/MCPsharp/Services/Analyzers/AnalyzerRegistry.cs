using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using MCPsharp.Models.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of analyzer registry for discovery and management
/// </summary>
public partial class AnalyzerRegistry : IAnalyzerRegistry
{
    private readonly ILogger<AnalyzerRegistry> _logger;
    private readonly Dictionary<string, IAnalyzer> _analyzers;
    private readonly Dictionary<string, AnalyzerInfo> _analyzerInfos;
    private readonly object _lock = new();

    public AnalyzerRegistry(ILogger<AnalyzerRegistry> logger)
    {
        _logger = logger;
        _analyzers = new Dictionary<string, IAnalyzer>();
        _analyzerInfos = new Dictionary<string, AnalyzerInfo>();
    }

    public event EventHandler<AnalyzerRegisteredEventArgs>? AnalyzerRegistered;
    public event EventHandler<AnalyzerUnregisteredEventArgs>? AnalyzerUnregistered;

    public async Task<bool> RegisterAnalyzerAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(analyzer.Id))
            {
                LogEmptyAnalyzerId(_logger);
                return false;
            }

            lock (_lock)
            {
                if (_analyzers.ContainsKey(analyzer.Id))
                {
                    LogAlreadyRegistered(_logger, analyzer.Id);
                    return false;
                }

                _analyzers[analyzer.Id] = analyzer;

                var info = new AnalyzerInfo
                {
                    Id = analyzer.Id,
                    Name = analyzer.Name,
                    Description = analyzer.Description,
                    Version = analyzer.Version,
                    Author = analyzer.Author,
                    SupportedExtensions = analyzer.SupportedExtensions,
                    IsBuiltIn = analyzer.GetType().Assembly.GetName().Name?.Equals("MCPsharp", StringComparison.OrdinalIgnoreCase) ?? false,
                    IsEnabled = analyzer.IsEnabled,
                    Rules = analyzer.GetRules()
                };

                _analyzerInfos[analyzer.Id] = info;
            }

            await analyzer.InitializeAsync(cancellationToken);

            LogRegistered(_logger, analyzer.Id, analyzer.Version);
            AnalyzerRegistered?.Invoke(this, new AnalyzerRegisteredEventArgs(analyzer.Id, _analyzerInfos[analyzer.Id]));

            return true;
        }
        catch (Exception ex)
        {
            LogErrorRegistering(_logger, ex, analyzer.Id);
            return false;
        }
    }

    public Task<bool> UnregisterAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!_analyzers.Remove(analyzerId, out var analyzer))
                {
                    LogNotRegistered(_logger, analyzerId);
                    return Task.FromResult(false);
                }

                _analyzerInfos.Remove(analyzerId);

                // Dispose analyzer asynchronously
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await analyzer.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        LogErrorDisposing(_logger, ex, analyzerId);
                    }
                }, cancellationToken);
            }

            LogUnregistered(_logger, analyzerId);
            AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(analyzerId));

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogErrorUnregistering(_logger, ex, analyzerId);
            return Task.FromResult(false);
        }
    }

    public ImmutableArray<AnalyzerInfo> GetRegisteredAnalyzers()
    {
        lock (_lock)
        {
            return _analyzerInfos.Values.ToImmutableArray();
        }
    }

    public List<IAnalyzer> GetLoadedAnalyzers()
    {
        lock (_lock)
        {
            return _analyzers.Values.ToList();
        }
    }

    public IAnalyzer? GetAnalyzer(string analyzerId)
    {
        lock (_lock)
        {
            return _analyzers.TryGetValue(analyzerId, out var analyzer) ? analyzer : null;
        }
    }

    public AnalyzerInfo? GetAnalyzerInfo(string analyzerId)
    {
        lock (_lock)
        {
            return _analyzerInfos.TryGetValue(analyzerId, out var info) ? info : null;
        }
    }

    public Task<ImmutableArray<AnalyzerInfo>> DiscoverAnalyzersAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            LogDiscovering(_logger, directoryPath);

            if (!Directory.Exists(directoryPath))
            {
                LogDirectoryDoesNotExist(_logger, directoryPath);
                return Task.FromResult(ImmutableArray<AnalyzerInfo>.Empty);
            }

            var discoveredAnalyzers = new List<AnalyzerInfo>();

            foreach (var dllFile in Directory.GetFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var analyzerInfos = LoadAnalyzersFromAssembly(dllFile);
                    discoveredAnalyzers.AddRange(analyzerInfos);
                }
                catch (Exception ex)
                {
                    LogErrorLoadingAssembly(_logger, ex, dllFile);
                }
            }

            LogDiscovered(_logger, discoveredAnalyzers.Count, directoryPath);
            return Task.FromResult(discoveredAnalyzers.ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorDiscovering(_logger, ex, directoryPath);
            return Task.FromResult(ImmutableArray<AnalyzerInfo>.Empty);
        }
    }

    public ImmutableArray<AnalyzerInfo> GetAnalyzersForExtension(string extension)
    {
        lock (_lock)
        {
            return _analyzerInfos.Values
                .Where(info => info.SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                .ToImmutableArray();
        }
    }

    public ImmutableArray<AnalyzerInfo> GetAnalyzersByCategory(RuleCategory category)
    {
        lock (_lock)
        {
            return _analyzerInfos.Values
                .Where(info => info.Rules.Any(rule => rule.Category == category))
                .ToImmutableArray();
        }
    }

    public ImmutableArray<AnalyzerInfo> GetBuiltInAnalyzers()
    {
        lock (_lock)
        {
            return _analyzerInfos.Values
                .Where(info => info.IsBuiltIn)
                .ToImmutableArray();
        }
    }

    public ImmutableArray<AnalyzerInfo> GetExternalAnalyzers()
    {
        lock (_lock)
        {
            return _analyzerInfos.Values
                .Where(info => !info.IsBuiltIn)
                .ToImmutableArray();
        }
    }

    public Task<CompatibilityResult> ValidateCompatibilityAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return Task.FromResult(new CompatibilityResult
                {
                    IsCompatible = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}"
                });
            }

            var assemblyContext = new AssemblyLoadContext(assemblyPath, isCollectible: true);
            var assembly = assemblyContext.LoadFromAssemblyPath(assemblyPath);

            var assemblyVersion = assembly.GetName().Version ?? new Version();
            var requiredVersion = new Version(1, 0, 0, 0); // Minimum required version

            var missingDependencies = new List<string>();
            var conflicts = new List<string>();
            var warnings = new List<string>();

            // Check for required types
            var analyzerTypes = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IAnalyzer).IsAssignableFrom(t))
                .ToList();

            if (analyzerTypes.Count == 0)
            {
                return Task.FromResult(new CompatibilityResult
                {
                    IsCompatible = false,
                    ErrorMessage = "No analyzer types found in assembly"
                });
            }

            // Check dependencies
            foreach (var referencedAssembly in assembly.GetReferencedAssemblies())
            {
                try
                {
                    Assembly.Load(referencedAssembly);
                }
                catch (Exception ex)
                {
                    LogReferencedAssemblyLoadFailed(_logger, ex, referencedAssembly.Name);
                    missingDependencies.Add($"{referencedAssembly.Name}: {ex.Message}");
                }
            }

            var isCompatible = assemblyVersion >= requiredVersion && missingDependencies.Count == 0;

            var result = new CompatibilityResult
            {
                IsCompatible = isCompatible,
                RequiredVersion = requiredVersion,
                ActualVersion = assemblyVersion,
                MissingDependencies = missingDependencies.ToImmutableArray(),
                Conflicts = conflicts.ToImmutableArray(),
                Warnings = warnings.ToImmutableArray(),
                ErrorMessage = !isCompatible ? "Assembly is not compatible" : null
            };

            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            LogErrorValidatingCompatibility(_logger, ex, assemblyPath);
            return Task.FromResult(new CompatibilityResult
            {
                IsCompatible = false,
                ErrorMessage = ex.Message
            });
        }
    }

    public Task<ImmutableArray<AnalyzerDependency>> GetDependenciesAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!_analyzerInfos.TryGetValue(analyzerId, out _))
                {
                    return Task.FromResult(ImmutableArray<AnalyzerDependency>.Empty);
                }
            }

            // This would typically read dependency information from analyzer metadata
            // For now, return empty dependencies
            return Task.FromResult(ImmutableArray<AnalyzerDependency>.Empty);
        }
        catch (Exception ex)
        {
            LogErrorGettingDependencies(_logger, ex, analyzerId);
            return Task.FromResult(ImmutableArray<AnalyzerDependency>.Empty);
        }
    }

    public async Task<bool> AreDependenciesSatisfiedAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var dependencies = await GetDependenciesAsync(analyzerId, cancellationToken);

            // Check that every required dependency is available
            return dependencies.Where(d => !d.IsOptional).All(IsDependencyAvailable);
        }
        catch (Exception ex)
        {
            LogErrorCheckingDependencies(_logger, ex, analyzerId);
            return false;
        }
    }

    private List<AnalyzerInfo> LoadAnalyzersFromAssembly(string assemblyPath)
    {
        var analyzerInfos = new List<AnalyzerInfo>();

        var assemblyContext = new AssemblyLoadContext(assemblyPath, isCollectible: true);
        var assembly = assemblyContext.LoadFromAssemblyPath(assemblyPath);

        var analyzerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IAnalyzer).IsAssignableFrom(t))
            .ToList();

        foreach (var analyzerType in analyzerTypes)
        {
            try
            {
                // Create instance without executing code (just for metadata)
                var constructor = analyzerType.GetConstructor(Type.EmptyTypes);
                if (constructor != null)
                {
                    var analyzer = (IAnalyzer)constructor.Invoke(Array.Empty<object>());

                    var info = new AnalyzerInfo
                    {
                        Id = analyzer.Id,
                        Name = analyzer.Name,
                        Description = analyzer.Description,
                        Version = analyzer.Version,
                        Author = analyzer.Author,
                        AssemblyPath = assemblyPath,
                        SupportedExtensions = analyzer.SupportedExtensions,
                        IsBuiltIn = false,
                        Rules = analyzer.GetRules()
                    };

                    analyzerInfos.Add(info);
                }
            }
            catch (Exception ex)
            {
                LogErrorInstantiating(_logger, ex, analyzerType.Name);
            }
        }

        return analyzerInfos;
    }

    private bool IsDependencyAvailable(AnalyzerDependency dependency)
    {
        try
        {
            // Simple check - try to load the assembly
            Assembly.Load(dependency.Name);
            return true;
        }
        catch (Exception ex)
        {
            LogDependencyUnavailable(_logger, ex, dependency.Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Attempted to register analyzer with empty ID")]
    private static partial void LogEmptyAnalyzerId(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} is already registered")]
    private static partial void LogAlreadyRegistered(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered analyzer: {AnalyzerId} v{Version}")]
    private static partial void LogRegistered(ILogger logger, string analyzerId, Version version);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error registering analyzer: {AnalyzerId}")]
    private static partial void LogErrorRegistering(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} is not registered")]
    private static partial void LogNotRegistered(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error disposing analyzer: {AnalyzerId}")]
    private static partial void LogErrorDisposing(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Unregistered analyzer: {AnalyzerId}")]
    private static partial void LogUnregistered(ILogger logger, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error unregistering analyzer: {AnalyzerId}")]
    private static partial void LogErrorUnregistering(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovering analyzers in directory: {DirectoryPath}")]
    private static partial void LogDiscovering(ILogger logger, string directoryPath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Directory does not exist: {DirectoryPath}")]
    private static partial void LogDirectoryDoesNotExist(ILogger logger, string directoryPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error loading analyzers from assembly: {AssemblyPath}")]
    private static partial void LogErrorLoadingAssembly(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Discovered {Count} analyzers in {DirectoryPath}")]
    private static partial void LogDiscovered(ILogger logger, int count, string directoryPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error discovering analyzers in directory: {DirectoryPath}")]
    private static partial void LogErrorDiscovering(ILogger logger, Exception exception, string directoryPath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Referenced assembly {AssemblyName} could not be loaded")]
    private static partial void LogReferencedAssemblyLoadFailed(ILogger logger, Exception exception, string? assemblyName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error validating assembly compatibility: {AssemblyPath}")]
    private static partial void LogErrorValidatingCompatibility(ILogger logger, Exception exception, string assemblyPath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting dependencies for analyzer: {AnalyzerId}")]
    private static partial void LogErrorGettingDependencies(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error checking dependencies for analyzer: {AnalyzerId}")]
    private static partial void LogErrorCheckingDependencies(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error instantiating analyzer type: {AnalyzerType}")]
    private static partial void LogErrorInstantiating(ILogger logger, Exception exception, string analyzerType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dependency {DependencyName} is not available")]
    private static partial void LogDependencyUnavailable(ILogger logger, Exception exception, string dependencyName);
}

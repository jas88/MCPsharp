using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of analyzer registry for discovery and management
/// </summary>
public class AnalyzerRegistry : IAnalyzerRegistry
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
                _logger.LogWarning("Attempted to register analyzer with empty ID");
                return false;
            }

            lock (_lock)
            {
                if (_analyzers.ContainsKey(analyzer.Id))
                {
                    _logger.LogWarning("Analyzer {AnalyzerId} is already registered", analyzer.Id);
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

            _logger.LogInformation("Registered analyzer: {AnalyzerId} v{Version}", analyzer.Id, analyzer.Version);
            AnalyzerRegistered?.Invoke(this, new AnalyzerRegisteredEventArgs(analyzer.Id, _analyzerInfos[analyzer.Id]));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering analyzer: {AnalyzerId}", analyzer.Id);
            return false;
        }
    }

    public async Task<bool> UnregisterAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!_analyzers.ContainsKey(analyzerId))
                {
                    _logger.LogWarning("Analyzer {AnalyzerId} is not registered", analyzerId);
                    return false;
                }

                var analyzer = _analyzers[analyzerId];
                _analyzers.Remove(analyzerId);
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
                        _logger.LogError(ex, "Error disposing analyzer: {AnalyzerId}", analyzerId);
                    }
                }, cancellationToken);
            }

            _logger.LogInformation("Unregistered analyzer: {AnalyzerId}", analyzerId);
            AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(analyzerId));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unregistering analyzer: {AnalyzerId}", analyzerId);
            return false;
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

    public async Task<ImmutableArray<AnalyzerInfo>> DiscoverAnalyzersAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Discovering analyzers in directory: {DirectoryPath}", directoryPath);

            if (!Directory.Exists(directoryPath))
            {
                _logger.LogWarning("Directory does not exist: {DirectoryPath}", directoryPath);
                return ImmutableArray<AnalyzerInfo>.Empty;
            }

            var discoveredAnalyzers = new List<AnalyzerInfo>();

            foreach (var dllFile in Directory.GetFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var analyzerInfos = await LoadAnalyzersFromAssemblyAsync(dllFile, cancellationToken);
                    discoveredAnalyzers.AddRange(analyzerInfos);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading analyzers from assembly: {AssemblyPath}", dllFile);
                }
            }

            _logger.LogInformation("Discovered {Count} analyzers in {DirectoryPath}", discoveredAnalyzers.Count, directoryPath);
            return discoveredAnalyzers.ToImmutableArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering analyzers in directory: {DirectoryPath}", directoryPath);
            return ImmutableArray<AnalyzerInfo>.Empty;
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

    public async Task<CompatibilityResult> ValidateCompatibilityAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(assemblyPath))
            {
                return new CompatibilityResult
                {
                    IsCompatible = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}"
                };
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

            if (!analyzerTypes.Any())
            {
                return new CompatibilityResult
                {
                    IsCompatible = false,
                    ErrorMessage = "No analyzer types found in assembly"
                };
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
                    missingDependencies.Add($"{referencedAssembly.Name}: {ex.Message}");
                }
            }

            var isCompatible = assemblyVersion >= requiredVersion && !missingDependencies.Any();

            return new CompatibilityResult
            {
                IsCompatible = isCompatible,
                RequiredVersion = requiredVersion,
                ActualVersion = assemblyVersion,
                MissingDependencies = missingDependencies.ToImmutableArray(),
                Conflicts = conflicts.ToImmutableArray(),
                Warnings = warnings.ToImmutableArray(),
                ErrorMessage = !isCompatible ? "Assembly is not compatible" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating assembly compatibility: {AssemblyPath}", assemblyPath);
            return new CompatibilityResult
            {
                IsCompatible = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ImmutableArray<AnalyzerDependency>> GetDependenciesAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            lock (_lock)
            {
                if (!_analyzerInfos.TryGetValue(analyzerId, out var info))
                {
                    return ImmutableArray<AnalyzerDependency>.Empty;
                }
            }

            // This would typically read dependency information from analyzer metadata
            // For now, return empty dependencies
            return ImmutableArray<AnalyzerDependency>.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dependencies for analyzer: {AnalyzerId}", analyzerId);
            return ImmutableArray<AnalyzerDependency>.Empty;
        }
    }

    public async Task<bool> AreDependenciesSatisfiedAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var dependencies = await GetDependenciesAsync(analyzerId, cancellationToken);

            foreach (var dependency in dependencies.Where(d => !d.IsOptional))
            {
                // Check if dependency is available
                if (!await IsDependencyAvailableAsync(dependency))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking dependencies for analyzer: {AnalyzerId}", analyzerId);
            return false;
        }
    }

    private async Task<List<AnalyzerInfo>> LoadAnalyzersFromAssemblyAsync(string assemblyPath, CancellationToken cancellationToken)
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
                _logger.LogError(ex, "Error instantiating analyzer type: {AnalyzerType}", analyzerType.Name);
            }
        }

        return analyzerInfos;
    }

    private async Task<bool> IsDependencyAvailableAsync(AnalyzerDependency dependency)
    {
        try
        {
            // Simple check - try to load the assembly
            Assembly.Load(dependency.Name);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
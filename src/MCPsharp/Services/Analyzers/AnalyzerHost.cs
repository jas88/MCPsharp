using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Implementation of analyzer host for loading and managing analyzers
/// </summary>
public partial class AnalyzerHost : IAnalyzerHost
{
    private readonly ILogger<AnalyzerHost> _logger;
    private readonly IAnalyzerRegistry _registry;
    private readonly ISecurityManager _securityManager;
    private readonly IFixEngine _fixEngine;
    private readonly IAnalyzerSandboxFactory _sandboxFactory;
    private readonly Dictionary<string, IAnalyzerSandbox> _sandboxes;
    private readonly object _lock = new();

    public event EventHandler<AnalyzerLoadedEventArgs>? AnalyzerLoaded;
    public event EventHandler<AnalyzerUnloadedEventArgs>? AnalyzerUnloaded;
    public event EventHandler<AnalyzerUnregisteredEventArgs>? AnalyzerUnregistered;
    public event EventHandler<AnalysisCompletedEventArgs>? AnalysisCompleted;

    public AnalyzerHost(
        ILogger<AnalyzerHost> logger,
        ILoggerFactory loggerFactory,
        IAnalyzerRegistry registry,
        ISecurityManager securityManager,
        IFixEngine fixEngine,
        IAnalyzerSandboxFactory sandboxFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _securityManager = securityManager ?? throw new ArgumentNullException(nameof(securityManager));
        _fixEngine = fixEngine ?? throw new ArgumentNullException(nameof(fixEngine));
        _sandboxFactory = sandboxFactory ?? throw new ArgumentNullException(nameof(sandboxFactory));
        _sandboxes = new Dictionary<string, IAnalyzerSandbox>();

        // Subscribe to registry events
        _registry.AnalyzerRegistered += (sender, args) => AnalyzerLoaded?.Invoke(this, new AnalyzerLoadedEventArgs(args.AnalyzerId, args.AnalyzerInfo));
        _registry.AnalyzerUnregistered += (sender, args) => AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(args.AnalyzerId));
    }

    public async Task<AnalyzerLoadResult> LoadAnalyzerAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            LogLoadingAnalyzer(_logger, assemblyPath);

            if (string.IsNullOrEmpty(assemblyPath))
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "Assembly path cannot be null or empty"
                };
            }

            if (!File.Exists(assemblyPath))
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Assembly not found: {assemblyPath}"
                };
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Security validation
            var securityValidation = await _securityManager.ValidateAssemblyAsync(assemblyPath, cancellationToken);
            if (!securityValidation.IsValid)
            {
                await _securityManager.LogSecurityEventAsync(new SecurityEvent
                {
                    AnalyzerId = Path.GetFileNameWithoutExtension(assemblyPath),
                    EventType = SecurityEventType.AssemblyValidation,
                    Operation = "LoadAnalyzer",
                    TargetPath = assemblyPath,
                    Success = false,
                    Details = $"Security validation failed: {securityValidation.ErrorMessage}"
                });

                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Security validation failed: {securityValidation.ErrorMessage}",
                    SecurityValidation = securityValidation
                };
            }

            // Compatibility check
            var compatibilityResult = await _registry.ValidateCompatibilityAsync(assemblyPath, cancellationToken);
            if (!compatibilityResult.IsCompatible)
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = $"Compatibility check failed: {compatibilityResult.ErrorMessage}",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            // Create sandbox for loading
            var loadSandbox = _sandboxFactory.CreateSandbox();

            // Load analyzer using sandbox
            var loadedAnalyzer = await loadSandbox.LoadAnalyzerAsync(assemblyPath, cancellationToken);
            if (loadedAnalyzer == null)
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "No valid analyzers found in assembly",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            // Register analyzer
            var registered = await _registry.RegisterAnalyzerAsync(loadedAnalyzer, cancellationToken);
            if (!registered)
            {
                return new AnalyzerLoadResult
                {
                    Success = false,
                    ErrorMessage = "Failed to register analyzer",
                    SecurityValidation = securityValidation,
                    Warnings = compatibilityResult.Warnings
                };
            }

            // Store sandbox for the analyzer
            lock (_lock)
            {
                _sandboxes[loadedAnalyzer.Id] = loadSandbox;
            }

            // Set default permissions
            await _securityManager.SetPermissionsAsync(loadedAnalyzer.Id, new AnalyzerPermissions
            {
                CanReadFiles = true,
                CanWriteFiles = false,
                CanExecuteCommands = false,
                CanAccessNetwork = false,
                AllowedPaths = ImmutableArray.Create(Directory.GetCurrentDirectory())
            });

            return new AnalyzerLoadResult
            {
                Success = true,
                AnalyzerId = loadedAnalyzer.Id,
                Analyzer = loadedAnalyzer,
                SecurityValidation = securityValidation,
                Warnings = compatibilityResult.Warnings.Concat(securityValidation.Warnings).ToImmutableArray()
            };
        }
        catch (Exception ex)
        {
            LogErrorLoadingAnalyzer(_logger, ex, assemblyPath);
            return new AnalyzerLoadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> UnloadAnalyzerAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(analyzerId))
        {
            return false;
        }

        try
        {
            LogUnloadingAnalyzer(_logger, analyzerId);

            // Check if analyzer exists
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return false;
            }

            // Remove sandbox
            IAnalyzerSandbox? sandbox = null;
            lock (_lock)
            {
                if (_sandboxes.TryGetValue(analyzerId, out sandbox))
                {
                    _sandboxes.Remove(analyzerId);
                }
            }

            sandbox?.Dispose();

            // Unregister from registry
            var unregistered = await _registry.UnregisterAnalyzerAsync(analyzerId, cancellationToken);

            if (unregistered)
            {
                await _securityManager.LogSecurityEventAsync(new SecurityEvent
                {
                    AnalyzerId = analyzerId,
                    EventType = SecurityEventType.AssemblyValidation,
                    Operation = "UnloadAnalyzer",
                    Success = true,
                    Details = $"Analyzer {analyzerId} unloaded successfully"
                });

                AnalyzerUnloaded?.Invoke(this, new AnalyzerUnloadedEventArgs(analyzerId));
            }

            return unregistered;
        }
        catch (Exception ex)
        {
            LogErrorUnloadingAnalyzer(_logger, ex, analyzerId);
            return false;
        }
    }

    public async Task<AnalyzerUnloadResult> UnloadAnalyzerAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        try
        {
            LogUnloadingAnalyzer(_logger, analyzer.Id);

            // Remove sandbox
            IAnalyzerSandbox? sandbox = null;
            lock (_lock)
            {
                if (_sandboxes.TryGetValue(analyzer.Id, out sandbox))
                {
                    _sandboxes.Remove(analyzer.Id);
                }
            }

            sandbox?.Dispose();

            // Unregister from registry
            var unregistered = await _registry.UnregisterAnalyzerAsync(analyzer.Id, cancellationToken);

            if (unregistered)
            {
                await _securityManager.LogSecurityEventAsync(new SecurityEvent
                {
                    AnalyzerId = analyzer.Id,
                    EventType = SecurityEventType.AssemblyValidation,
                    Operation = "UnloadAnalyzer",
                    Success = true,
                    Details = $"Analyzer {analyzer.Id} unloaded successfully"
                });

                AnalyzerUnloaded?.Invoke(this, new AnalyzerUnloadedEventArgs(analyzer.Id));

                return new AnalyzerUnloadResult
                {
                    Success = true,
                    AnalyzerId = analyzer.Id
                };
            }
            else
            {
                return new AnalyzerUnloadResult
                {
                    Success = false,
                    AnalyzerId = analyzer.Id,
                    ErrorMessage = "Failed to unregister analyzer"
                };
            }
        }
        catch (Exception ex)
        {
            LogErrorUnloadingAnalyzer(_logger, ex, analyzer.Id);
            return new AnalyzerUnloadResult
            {
                Success = false,
                AnalyzerId = analyzer.Id,
                ErrorMessage = ex.Message
            };
        }
    }

    public ImmutableArray<AnalyzerInfo> GetLoadedAnalyzers()
    {
        return _registry.GetRegisteredAnalyzers();
    }

    public IAnalyzer? GetAnalyzer(string analyzerId)
    {
        return _registry.GetAnalyzer(analyzerId);
    }

    public AnalyzerInfo? GetAnalyzerInfo(string analyzerId)
    {
        return _registry.GetAnalyzerInfo(analyzerId);
    }

    public async Task<bool> SetAnalyzerEnabledAsync(string analyzerId, bool enabled, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return false;
            }

            analyzer.IsEnabled = enabled;

            await _securityManager.LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = analyzerId,
                EventType = SecurityEventType.PermissionCheck,
                Operation = "SetEnabled",
                Success = true,
                Details = $"Analyzer {analyzerId} {(enabled ? "enabled" : "disabled")}"
            });

            return true;
        }
        catch (Exception ex)
        {
            LogErrorSettingEnabledState(_logger, ex, analyzerId);
            return false;
        }
    }

    public async Task<bool> ConfigureAnalyzerAsync(string analyzerId, AnalyzerConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return false;
            }

            analyzer.Configuration = configuration;

            await _securityManager.LogSecurityEventAsync(new SecurityEvent
            {
                AnalyzerId = analyzerId,
                EventType = SecurityEventType.PermissionCheck,
                Operation = "Configure",
                Success = true,
                Details = $"Analyzer {analyzerId} configuration updated"
            });

            return true;
        }
        catch (Exception ex)
        {
            LogErrorConfiguringAnalyzer(_logger, ex, analyzerId);
            return false;
        }
    }
}

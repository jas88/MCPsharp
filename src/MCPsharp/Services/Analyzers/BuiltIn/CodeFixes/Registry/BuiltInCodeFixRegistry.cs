using System.Collections.Immutable;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;

/// <summary>
/// Central registry for built-in automated code fix providers
/// Discovers and manages all built-in fix providers
/// </summary>
public partial class BuiltInCodeFixRegistry
{
    private readonly ILogger<BuiltInCodeFixRegistry> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, IAutomatedCodeFixProvider> _providers = new();
    private readonly object _lock = new();

    public BuiltInCodeFixRegistry(
        ILogger<BuiltInCodeFixRegistry> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        RegisterBuiltInProviders();
    }

    /// <summary>
    /// Register all built-in fix providers
    /// </summary>
    private void RegisterBuiltInProviders()
    {
        try
        {
            // Register AsyncAwaitPattern provider
            RegisterProvider(new AsyncAwaitPatternProvider(
                _loggerFactory.CreateLogger<AsyncAwaitPatternProvider>()));

            // Register ExceptionLogging provider
            RegisterProvider(new ExceptionLoggingProvider(
                _loggerFactory.CreateLogger<ExceptionLoggingProvider>()));

            // Register UnusedCode provider
            RegisterProvider(new UnusedCodeProvider(
                _loggerFactory.CreateLogger<UnusedCodeProvider>()));

            // Register StaticMethod provider
            RegisterProvider(new StaticMethodProvider(
                _loggerFactory.CreateLogger<StaticMethodProvider>()));

            LogProvidersRegistered(_logger, _providers.Count);
        }
        catch (Exception ex)
        {
            LogRegistrationError(_logger, ex);
        }
    }

    /// <summary>
    /// Register a code fix provider
    /// </summary>
    public void RegisterProvider(IAutomatedCodeFixProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_lock)
        {
            if (!_providers.TryAdd(provider.Id, provider))
            {
                LogProviderAlreadyRegistered(_logger, provider.Id);
                return;
            }

            LogProviderRegistered(_logger, provider.Id, provider.Name, provider.Profile);
        }
    }

    /// <summary>
    /// Get a provider by ID
    /// </summary>
    public IAutomatedCodeFixProvider? GetProvider(string id)
    {
        lock (_lock)
        {
            _providers.TryGetValue(id, out var provider);
            return provider;
        }
    }

    /// <summary>
    /// Get all providers for a specific profile
    /// </summary>
    public ImmutableArray<IAutomatedCodeFixProvider> GetProvidersByProfile(FixProfile profile)
    {
        lock (_lock)
        {
            return _providers.Values
                .Where(p => p.Profile == profile || profile == FixProfile.Aggressive)
                .ToImmutableArray();
        }
    }

    /// <summary>
    /// Get all providers
    /// </summary>
    public ImmutableArray<IAutomatedCodeFixProvider> GetAllProviders()
    {
        lock (_lock)
        {
            return _providers.Values.ToImmutableArray();
        }
    }

    /// <summary>
    /// Get all analyzers from all providers
    /// </summary>
    public ImmutableArray<DiagnosticAnalyzer> GetAllAnalyzers()
    {
        lock (_lock)
        {
            return _providers.Values
                .Select(p => p.GetAnalyzer())
                .ToImmutableArray();
        }
    }

    /// <summary>
    /// Get all code fix providers from all providers
    /// </summary>
    public ImmutableArray<CodeFixProvider> GetAllCodeFixProviders()
    {
        lock (_lock)
        {
            return _providers.Values
                .Select(p => p.GetCodeFixProvider())
                .ToImmutableArray();
        }
    }

    /// <summary>
    /// Get providers that can fix a specific diagnostic ID
    /// </summary>
    public ImmutableArray<IAutomatedCodeFixProvider> GetProvidersForDiagnostic(string diagnosticId)
    {
        lock (_lock)
        {
            return _providers.Values
                .Where(p => p.FixableDiagnosticIds.Contains(diagnosticId))
                .ToImmutableArray();
        }
    }

    /// <summary>
    /// Get fully automated providers (safe for automatic application)
    /// </summary>
    public ImmutableArray<IAutomatedCodeFixProvider> GetFullyAutomatedProviders()
    {
        lock (_lock)
        {
            return _providers.Values
                .Where(p => p.IsFullyAutomated)
                .ToImmutableArray();
        }
    }

    /// <summary>
    /// Get statistics about registered providers
    /// </summary>
    public RegistryStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new RegistryStatistics
            {
                TotalProviders = _providers.Count,
                ProvidersByProfile = _providers.Values
                    .GroupBy(p => p.Profile)
                    .ToDictionary(g => g.Key, g => g.Count()),
                FullyAutomatedCount = _providers.Values.Count(p => p.IsFullyAutomated),
                TotalFixableDiagnostics = _providers.Values
                    .SelectMany(p => p.FixableDiagnosticIds)
                    .Distinct()
                    .Count()
            };
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered {Count} built-in code fix providers")]
    private static partial void LogProvidersRegistered(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error registering built-in code fix providers")]
    private static partial void LogRegistrationError(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provider {ProviderId} is already registered, skipping")]
    private static partial void LogProviderAlreadyRegistered(ILogger logger, string providerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered code fix provider: {Id} - {Name} (Profile: {Profile})")]
    private static partial void LogProviderRegistered(ILogger logger, string id, string name, FixProfile profile);
}

/// <summary>
/// Statistics about the registry
/// </summary>
public record RegistryStatistics
{
    public int TotalProviders { get; init; }
    public Dictionary<FixProfile, int> ProvidersByProfile { get; init; } = new();
    public int FullyAutomatedCount { get; init; }
    public int TotalFixableDiagnostics { get; init; }
}

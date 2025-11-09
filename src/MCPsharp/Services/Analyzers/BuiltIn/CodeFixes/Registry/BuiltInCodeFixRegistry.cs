using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;

/// <summary>
/// Central registry for built-in automated code fix providers
/// Discovers and manages all built-in fix providers
/// </summary>
public class BuiltInCodeFixRegistry
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

            _logger.LogInformation("Registered {Count} built-in code fix providers", _providers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering built-in code fix providers");
        }
    }

    /// <summary>
    /// Register a code fix provider
    /// </summary>
    public void RegisterProvider(IAutomatedCodeFixProvider provider)
    {
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        lock (_lock)
        {
            if (_providers.ContainsKey(provider.Id))
            {
                _logger.LogWarning("Provider {ProviderId} is already registered, skipping", provider.Id);
                return;
            }

            _providers[provider.Id] = provider;
            _logger.LogInformation("Registered code fix provider: {Id} - {Name} (Profile: {Profile})",
                provider.Id, provider.Name, provider.Profile);
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

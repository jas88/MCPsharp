using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

public partial class AnalyzerHost
{
    public Task<ImmutableArray<AnalyzerHealthStatus>> GetHealthStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzers = _registry.GetRegisteredAnalyzers();
            var healthStatuses = new List<AnalyzerHealthStatus>();

            foreach (var analyzerInfo in analyzers)
            {
                var analyzer = _registry.GetAnalyzer(analyzerInfo.Id);
                var isHealthy = analyzer != null && analyzer.IsEnabled;

                IAnalyzerSandbox? sandbox = null;
                lock (_lock)
                {
                    _sandboxes.TryGetValue(analyzerInfo.Id, out sandbox);
                }

                var sandboxHealthy = sandbox?.IsHealthy() ?? true;

                healthStatuses.Add(new AnalyzerHealthStatus
                {
                    AnalyzerId = analyzerInfo.Id,
                    IsHealthy = isHealthy && sandboxHealthy,
                    IsLoaded = analyzer != null,
                    IsEnabled = analyzer?.IsEnabled ?? false,
                    LastActivity = analyzerInfo.LoadedAt,
                    Uptime = DateTime.UtcNow - analyzerInfo.LoadedAt,
                    AnalysesRun = 0, // Would need to track this
                    ErrorsEncountered = 0, // Would need to track this
                    Metrics = new Dictionary<string, object>
                    {
                        ["SandboxHealthy"] = sandboxHealthy,
                        ["Version"] = analyzerInfo.Version.ToString(),
                        ["Author"] = analyzerInfo.Author
                    }.ToImmutableDictionary()
                });
            }

            return Task.FromResult(healthStatuses.ToImmutableArray());
        }
        catch (Exception ex)
        {
            LogErrorGettingHealthStatus(_logger, ex);
            return Task.FromResult(ImmutableArray<AnalyzerHealthStatus>.Empty);
        }
    }

    public async Task<List<IAnalyzer>> GetLoadedAnalyzersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.FromResult(_registry.GetLoadedAnalyzers());
        }
        catch (Exception ex)
        {
            LogErrorGettingLoadedAnalyzers(_logger, ex);
            return new List<IAnalyzer>();
        }
    }

    public Task<AnalyzerCapabilities?> GetAnalyzerCapabilitiesAsync(string analyzerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var analyzer = _registry.GetAnalyzer(analyzerId);
            if (analyzer == null)
            {
                return Task.FromResult<AnalyzerCapabilities?>(null);
            }

            return Task.FromResult<AnalyzerCapabilities?>(analyzer.GetCapabilities());
        }
        catch (Exception ex)
        {
            LogErrorGettingCapabilities(_logger, ex, analyzerId);
            return Task.FromResult<AnalyzerCapabilities?>(null);
        }
    }
}

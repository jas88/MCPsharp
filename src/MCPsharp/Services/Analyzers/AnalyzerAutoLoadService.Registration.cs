using MCPsharp.Models.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Registering a load's analyzers in the analyzer registry, and unregistering those of the previous load
/// </summary>
public sealed partial class AnalyzerAutoLoadService
{
    /// <summary>
    /// Register the analyzers, returning those the registry accepted: only those can run. One the registry rejects
    /// because another analyzer holds its ID (e.g. one loaded with load_roslyn_analyzers), and one that fails to
    /// initialize, are reported in <paramref name="errors"/>; a cancellation of the load propagates.
    /// </summary>
    private async Task<List<IAnalyzer>> RegisterAnalyzersAsync(List<IAnalyzer> analyzers, List<string> errors, CancellationToken cancellationToken)
    {
        var registered = new List<IAnalyzer>();
        foreach (var analyzer in analyzers)
        {
            try
            {
                if (await _registry.RegisterAnalyzerAsync(analyzer, cancellationToken))
                {
                    _registeredAnalyzers.Add(analyzer);
                    registered.Add(analyzer);
                }
                else
                {
                    LogAnalyzerIdTaken(_logger, analyzer.Id);
                    errors.Add($"Did not load analyzer '{analyzer.Id}': an analyzer with its ID is already registered");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // An analyzer's own cancellation, while the load goes on, is a failure to initialize like any other
                LogAnalyzerFailedToInitialize(_logger, ex, analyzer.Id);
                errors.Add($"Failed to initialize analyzer '{analyzer.Id}': {ex.Message}");
            }
        }

        return registered;
    }

    /// <summary>
    /// Unregister the analyzers this service registered, and forget the previous load
    /// </summary>
    private async Task UnregisterAutoLoadedAnalyzersAsync(CancellationToken cancellationToken)
    {
        Publish(null, [], [], loaded: false);

        var unregistered = 0;
        foreach (var analyzer in _registeredAnalyzers)
        {
            // Leave the ID alone if something else has registered its own analyzer under it since
            if (await _registry.UnregisterAnalyzerInstanceAsync(analyzer, cancellationToken))
            {
                unregistered++;
            }
        }

        _registeredAnalyzers.Clear();
        if (unregistered > 0)
        {
            LogUnregisteredPreviousAnalyzers(_logger, unregistered);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Unregistered {Count} analyzers of the previous auto-load")]
    private static partial void LogUnregisteredPreviousAnalyzers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Analyzer {AnalyzerId} failed to initialize, so it was not loaded")]
    private static partial void LogAnalyzerFailedToInitialize(ILogger logger, Exception exception, string analyzerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} was not loaded: an analyzer with its ID is already registered")]
    private static partial void LogAnalyzerIdTaken(ILogger logger, string analyzerId);
}

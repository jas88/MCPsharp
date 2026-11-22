using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Registering a load's analyzers in the analyzer registry, publishing as loaded those the registry holds, and
/// unregistering those of the previous load
/// </summary>
public sealed partial class AnalyzerAutoLoadService
{
    /// <summary>
    /// Register the analyzers in the registry, one after another. An analyzer that the registry accepts is recorded
    /// as registered by this service, which unregisters it when the load is cancelled or the next load begins;
    /// whether it is published as loaded is decided when the load ends (see <see cref="PublishRegisteredAnalyzers"/>).
    /// The registry rejects an analyzer whose ID another analyzer holds (e.g. one loaded with load_roslyn_analyzers):
    /// that analyzer is reported in <paramref name="errors"/>, and the load goes on. An analyzer that fails to
    /// initialize, or that cancels its own initialization while the load is not cancelled, is not registered: it too
    /// is reported in <paramref name="errors"/>, and the load goes on. A cancellation of the load propagates to the
    /// caller.
    /// </summary>
    private async Task RegisterAnalyzersAsync(List<IAnalyzer> analyzers, List<string> errors, CancellationToken cancellationToken)
    {
        foreach (var analyzer in analyzers)
        {
            try
            {
                if (await _registry.RegisterAnalyzerAsync(analyzer, cancellationToken))
                {
                    // Not yet one of the load's analyzers: the registry has told its subscribers of it, and one of
                    // them, or another caller, may already have unregistered it. That is checked when the load is
                    // published.
                    _registeredAnalyzers.Add(analyzer);
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
    }

    /// <summary>
    /// Record the outcome of a load, publishing as its analyzers those this service registered that the registry
    /// holds, and return them. The registry checks which it holds and the service records them in one step, during
    /// which nothing is registered or unregistered, so an analyzer that the registry stopped holding after the
    /// service registered it is not published.
    /// </summary>
    private ImmutableArray<IAnalyzer> PublishRegisteredAnalyzers(
        AnalyzerAutoLoadConfiguration configuration,
        IReadOnlyList<AnalyzerReference> references,
        bool loaded) =>
        _registry.PublishRegisteredAnalyzers(
            _registeredAnalyzers,
            registered => Publish(configuration, references, registered, loaded));

    /// <summary>
    /// Report in <paramref name="errors"/> each analyzer this service registered that is not among those
    /// <paramref name="published"/>: the registry stopped holding it before the load was published, because it was
    /// unregistered (e.g. unloaded) or replaced by another analyzer loaded under its ID. It is not registered again:
    /// like an analyzer unloaded after the load, it stays out until the next load.
    /// </summary>
    private void ReportAnalyzersNoLongerRegistered(ImmutableArray<IAnalyzer> published, List<string> errors)
    {
        var noLongerRegistered = _registeredAnalyzers.Except<IAnalyzer>(published, ReferenceEqualityComparer.Instance);
        foreach (var analyzerId in noLongerRegistered.Select(analyzer => analyzer.Id))
        {
            LogAnalyzerNoLongerRegistered(_logger, analyzerId);
            errors.Add($"Did not load analyzer '{analyzerId}': it was unregistered or replaced before the load finished");
        }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Analyzer {AnalyzerId} was not loaded: it was unregistered or replaced before the load finished")]
    private static partial void LogAnalyzerNoLongerRegistered(ILogger logger, string analyzerId);
}

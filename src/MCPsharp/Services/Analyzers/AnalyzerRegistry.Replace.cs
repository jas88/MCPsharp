using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Replacing an analyzer, and unregistering a particular one, each in one step, so that two callers registering
/// analyzers under one ID (an explicit load and the auto-load) can't leave it with an analyzer neither expects. An
/// analyzer is registered only once it has initialized, so nothing can get and run it before.
/// </summary>
public partial class AnalyzerRegistry
{
    /// <inheritdoc />
    public async Task<bool> ReplaceAnalyzerAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(analyzer.Id))
        {
            LogEmptyAnalyzerId(_logger);
            return false;
        }

        if (ReferenceEquals(GetAnalyzer(analyzer.Id), analyzer))
        {
            return true;
        }

        // Until the replacement has initialized, the analyzer registered under the ID stays, and runs; and it stays
        // if the replacement's initialization fails or is cancelled, which propagates
        var info = await InitializeForRegistrationAsync(analyzer, cancellationToken);

        // Whichever analyzer holds the ID now is replaced: while the replacement initialized, the one it was to
        // replace may have been unregistered, or another registered
        IAnalyzer? previous;
        lock (_lock)
        {
            _analyzers.TryGetValue(analyzer.Id, out previous);
            _analyzers[analyzer.Id] = analyzer;
            _analyzerInfos[analyzer.Id] = info;
        }

        if (previous != null && !ReferenceEquals(previous, analyzer))
        {
            DisposeInBackground(previous);
        }

        LogRegistered(_logger, analyzer.Id, analyzer.Version);
        AnalyzerRegistered?.Invoke(this, new AnalyzerRegisteredEventArgs(analyzer.Id, info));
        return true;
    }

    /// <inheritdoc />
    public Task<bool> UnregisterAnalyzerInstanceAsync(IAnalyzer analyzer, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_analyzers.TryGetValue(analyzer.Id, out var registered) || !ReferenceEquals(registered, analyzer))
            {
                return Task.FromResult(false);
            }

            _analyzers.Remove(analyzer.Id);
            _analyzerInfos.Remove(analyzer.Id);
            DisposeInBackground(analyzer);
        }

        LogUnregistered(_logger, analyzer.Id);
        AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(analyzer.Id));
        return Task.FromResult(true);
    }

    /// <summary>
    /// Initialize an analyzer that is to be registered, and describe it. One that fails to initialize, or whose
    /// initialization is cancelled, is disposed before the exception propagates, since it won't be registered and
    /// its caller handed it over to be.
    /// </summary>
    private async Task<AnalyzerInfo> InitializeForRegistrationAsync(IAnalyzer analyzer, CancellationToken cancellationToken)
    {
        // An analyzer's initialization may ignore the token, and a caller that has cancelled registers nothing more
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await analyzer.InitializeAsync(cancellationToken);
            return CreateAnalyzerInfo(analyzer);
        }
        catch
        {
            DisposeInBackground(analyzer);
            throw;
        }
    }

    private static AnalyzerInfo CreateAnalyzerInfo(IAnalyzer analyzer) => new()
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

    /// <summary>
    /// Dispose an analyzer that is no longer registered, without making the caller wait. The disposal doesn't take
    /// the caller's cancellation token: a task whose token is cancelled before it starts never runs, which would
    /// leave the analyzer undisposed.
    /// </summary>
    private void DisposeInBackground(IAnalyzer analyzer) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await analyzer.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogErrorDisposing(_logger, ex, analyzer.Id);
            }
        }, CancellationToken.None);
}

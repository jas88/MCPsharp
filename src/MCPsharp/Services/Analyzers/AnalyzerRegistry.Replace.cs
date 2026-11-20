using MCPsharp.Models.Analyzers;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Replacing an analyzer, and unregistering a particular one, each in one step, so that two callers registering
/// analyzers under one ID (an explicit load and the auto-load) can't leave it with an analyzer neither expects
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

        IAnalyzer? previous;
        AnalyzerInfo? previousInfo;
        AnalyzerInfo info;
        lock (_lock)
        {
            if (_analyzers.TryGetValue(analyzer.Id, out previous) && ReferenceEquals(previous, analyzer))
            {
                return true;
            }

            _analyzerInfos.TryGetValue(analyzer.Id, out previousInfo);
            info = CreateAnalyzerInfo(analyzer);
            _analyzers[analyzer.Id] = analyzer;
            _analyzerInfos[analyzer.Id] = info;
        }

        try
        {
            await analyzer.InitializeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            RestoreAfterFailedReplace(analyzer, previous, previousInfo);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            LogErrorRegistering(_logger, ex, analyzer.Id);
            return false;
        }

        if (previous != null)
        {
            DisposeInBackground(previous, cancellationToken);
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
            DisposeInBackground(analyzer, cancellationToken);
        }

        LogUnregistered(_logger, analyzer.Id);
        AnalyzerUnregistered?.Invoke(this, new AnalyzerUnregisteredEventArgs(analyzer.Id));
        return Task.FromResult(true);
    }

    /// <summary>
    /// Put back the analyzer that a replacement whose initialization failed displaced, unless the ID has been
    /// replaced again since
    /// </summary>
    private void RestoreAfterFailedReplace(IAnalyzer failed, IAnalyzer? previous, AnalyzerInfo? previousInfo)
    {
        lock (_lock)
        {
            if (!_analyzers.TryGetValue(failed.Id, out var current) || !ReferenceEquals(current, failed))
            {
                return;
            }

            if (previous != null && previousInfo != null)
            {
                _analyzers[failed.Id] = previous;
                _analyzerInfos[failed.Id] = previousInfo;
            }
            else
            {
                _analyzers.Remove(failed.Id);
                _analyzerInfos.Remove(failed.Id);
            }
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
    /// Dispose an analyzer that is no longer registered, without making the caller wait
    /// </summary>
    private void DisposeInBackground(IAnalyzer analyzer, CancellationToken cancellationToken) =>
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
        }, cancellationToken);
}

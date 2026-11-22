using MCPsharp.Models.Analyzers;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers;

/// <summary>
/// Raising the registry's events. Every subscriber is told, each on its own, and one that throws is logged: its
/// exception changes neither what the registry did nor what it reports, so an exception from registering an analyzer
/// always means that the analyzer was not registered, and the other subscribers (which may keep their own record of
/// the registered analyzers) are still told.
/// </summary>
public partial class AnalyzerRegistry
{
    private void RaiseAnalyzerRegistered(string analyzerId, AnalyzerInfo analyzerInfo) =>
        Raise(AnalyzerRegistered, new AnalyzerRegisteredEventArgs(analyzerId, analyzerInfo), analyzerId);

    private void RaiseAnalyzerUnregistered(string analyzerId) =>
        Raise(AnalyzerUnregistered, new AnalyzerUnregisteredEventArgs(analyzerId), analyzerId);

    private void Raise<TEventArgs>(EventHandler<TEventArgs>? subscribers, TEventArgs args, string analyzerId)
    {
        if (subscribers == null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList().Cast<EventHandler<TEventArgs>>())
        {
            try
            {
                subscriber(this, args);
            }
            catch (Exception ex)
            {
                LogErrorInEventSubscriber(_logger, ex, analyzerId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A subscriber to the analyzer registry's events failed for analyzer {AnalyzerId}")]
    private static partial void LogErrorInEventSubscriber(ILogger logger, Exception exception, string analyzerId);
}

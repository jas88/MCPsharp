using System.Collections.Immutable;
using MCPsharp.Models.Analyzers;

namespace MCPsharp.Tests.Services.Analyzers;

/// <summary>
/// An analyzer for registration tests that records when it is initialized and disposed. Its initialization can be
/// held until the test releases it, so a test can act while it is in progress and the interleaving is deterministic.
/// </summary>
internal sealed class TestAnalyzer : IAnalyzer
{
    // How long a test waits for something it expects to happen before failing
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    // How long a test waits for a disposal that should not happen before taking it that it won't: far longer than
    // the registry's background disposals take to run once it has started them, which is under a millisecond
    private static readonly TimeSpan UnwantedDisposalWait = TimeSpan.FromMilliseconds(250);

    private readonly TaskCompletionSource _initializationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource? _initializationRelease;
    private readonly bool _initializationObservesCancellation;
    private readonly Exception? _initializationError;

    /// <param name="id">The analyzer's ID</param>
    /// <param name="holdInitialization">
    /// Make <see cref="InitializeAsync"/> wait until <see cref="ReleaseInitialization"/> is called
    /// </param>
    /// <param name="initializationObservesCancellation">
    /// Whether a held <see cref="InitializeAsync"/> stops waiting when its token is cancelled; one that doesn't
    /// finishes when released, as an initialization that ignores its token (e.g. a Roslyn analyzer adapter's) does
    /// </param>
    /// <param name="initializationError">The exception <see cref="InitializeAsync"/> throws, if any</param>
    public TestAnalyzer(
        string id,
        bool holdInitialization = false,
        bool initializationObservesCancellation = true,
        Exception? initializationError = null)
    {
        Id = id;
        _initializationRelease = holdInitialization ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        _initializationObservesCancellation = initializationObservesCancellation;
        _initializationError = initializationError;
    }

    public string Id { get; }
    public string Name => Id;
    public string Description => "An analyzer for registration tests";
    public Version Version { get; } = new(1, 0, 0);
    public string Author => "MCPsharp tests";
    public ImmutableArray<string> SupportedExtensions { get; } = [".cs"];
    public bool IsEnabled { get; set; } = true;
    public AnalyzerConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Whether <see cref="DisposeAsync"/> has been called
    /// </summary>
    public bool IsDisposed => _disposed.Task.IsCompleted;

    /// <summary>
    /// Wait until <see cref="InitializeAsync"/> has been called; throws <see cref="TimeoutException"/> if it isn't
    /// </summary>
    public Task WaitUntilInitializingAsync() => _initializationStarted.Task.WaitAsync(WaitLimit);

    /// <summary>
    /// Wait until <see cref="DisposeAsync"/> has been called, which the registry may do in the background; throws
    /// <see cref="TimeoutException"/> if it isn't
    /// </summary>
    public Task WaitUntilDisposedAsync() => _disposed.Task.WaitAsync(WaitLimit);

    /// <summary>
    /// Wait for a disposal that should not happen, and return whether <see cref="DisposeAsync"/> was called: false
    /// means that the registry has left the analyzer alone. The registry disposes in the background, so a check made
    /// as soon as the registry returns finds nearly every analyzer undisposed, even one whose disposal the registry
    /// has started.
    /// </summary>
    public async Task<bool> IsDisposedSoonAsync()
    {
        try
        {
            await _disposed.Task.WaitAsync(UnwantedDisposalWait);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Let a held <see cref="InitializeAsync"/> finish
    /// </summary>
    public void ReleaseInitialization() => _initializationRelease?.TrySetResult();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initializationStarted.TrySetResult();
        if (_initializationRelease != null)
        {
            await _initializationRelease.Task.WaitAsync(_initializationObservesCancellation ? cancellationToken : CancellationToken.None);
        }

        if (_initializationError != null)
        {
            throw _initializationError;
        }
    }

    public Task DisposeAsync()
    {
        _disposed.TrySetResult();
        return Task.CompletedTask;
    }

    public bool CanAnalyze(string targetPath) => true;

    public Task<AnalysisResult> AnalyzeAsync(string filePath, string content, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AnalysisResult { FilePath = filePath, AnalyzerId = Id, Success = true });

    public ImmutableArray<AnalyzerRule> GetRules() => [];

    public ImmutableArray<AnalyzerFix> GetFixes(string ruleId) => [];

    public AnalyzerCapabilities GetCapabilities() => new();
}

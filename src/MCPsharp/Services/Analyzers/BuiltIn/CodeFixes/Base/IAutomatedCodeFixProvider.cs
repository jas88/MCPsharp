using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;

/// <summary>
/// Interface for automated code fix providers that combine analysis and fixing
/// </summary>
public interface IAutomatedCodeFixProvider
{
    /// <summary>
    /// Unique identifier for this fix provider
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Display name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what this fix does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Diagnostic IDs this provider can fix
    /// </summary>
    ImmutableArray<string> FixableDiagnosticIds { get; }

    /// <summary>
    /// Fix profile this belongs to (conservative, balanced, aggressive)
    /// </summary>
    FixProfile Profile { get; }

    /// <summary>
    /// Can this fix be applied automatically without user review?
    /// </summary>
    bool IsFullyAutomated { get; }

    /// <summary>
    /// Get the underlying DiagnosticAnalyzer
    /// </summary>
    DiagnosticAnalyzer GetAnalyzer();

    /// <summary>
    /// Get the underlying CodeFixProvider
    /// </summary>
    CodeFixProvider GetCodeFixProvider();

    /// <summary>
    /// Apply fixes in batch mode across multiple documents
    /// </summary>
    /// <param name="documents">Documents to process</param>
    /// <param name="configuration">Fix configuration</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch fix result</returns>
    Task<BatchFixResult> ApplyBatchFixesAsync(
        ImmutableArray<Document> documents,
        FixConfiguration configuration,
        IProgress<BatchFixProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Progress information for batch fix operations
/// </summary>
public record BatchFixProgress
{
    public string CurrentFile { get; init; } = string.Empty;
    public int FilesProcessed { get; init; }
    public int TotalFiles { get; init; }
    public int DiagnosticsFound { get; init; }
    public int DiagnosticsFixed { get; init; }
    public string CurrentOperation { get; init; } = string.Empty;
}

/// <summary>
/// Result of a batch fix operation
/// </summary>
public record BatchFixResult
{
    public bool Success { get; init; }
    public string FixProviderId { get; init; } = string.Empty;
    public ImmutableArray<DocumentFixResult> DocumentResults { get; init; } = ImmutableArray<DocumentFixResult>.Empty;
    public int TotalDiagnostics { get; init; }
    public int TotalFixed { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of fixing a single document
/// </summary>
public record DocumentFixResult
{
    public string FilePath { get; init; } = string.Empty;
    public bool Modified { get; init; }
    public int DiagnosticsFound { get; init; }
    public int DiagnosticsFixed { get; init; }
    public string? ErrorMessage { get; init; }
}

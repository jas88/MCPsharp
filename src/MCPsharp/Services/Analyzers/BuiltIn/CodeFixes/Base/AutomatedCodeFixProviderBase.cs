using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;

/// <summary>
/// Abstract base class for automated code fix providers
/// Provides common functionality for batch processing and progress reporting
/// </summary>
public abstract partial class AutomatedCodeFixProviderBase : IAutomatedCodeFixProvider
{
    protected ILogger Logger { get; }

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract ImmutableArray<string> FixableDiagnosticIds { get; }
    public abstract FixProfile Profile { get; }
    public virtual bool IsFullyAutomated => false;

    protected AutomatedCodeFixProviderBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public abstract DiagnosticAnalyzer GetAnalyzer();
    public abstract CodeFixProvider GetCodeFixProvider();

    /// <summary>
    /// Apply fixes in batch mode across multiple documents
    /// </summary>
    public virtual async Task<BatchFixResult> ApplyBatchFixesAsync(
        ImmutableArray<Document> documents,
        FixConfiguration configuration,
        IProgress<BatchFixProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var results = new List<DocumentFixResult>();
        var totalDiagnostics = 0;
        var totalFixed = 0;

        try
        {
            LogBatchStarting(Logger, Name, documents.Length);

            for (int i = 0; i < documents.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var doc = documents[i];
                var filePath = doc.FilePath ?? $"Document_{i}";

                progress?.Report(new BatchFixProgress
                {
                    CurrentFile = filePath,
                    FilesProcessed = i,
                    TotalFiles = documents.Length,
                    DiagnosticsFound = totalDiagnostics,
                    DiagnosticsFixed = totalFixed,
                    CurrentOperation = $"Processing {Path.GetFileName(filePath)}"
                });

                try
                {
                    var docResult = await ApplySingleDocumentFixAsync(doc, configuration, cancellationToken);
                    results.Add(docResult);
                    totalDiagnostics += docResult.DiagnosticsFound;
                    totalFixed += docResult.DiagnosticsFixed;

                    LogDocumentProcessed(Logger, filePath, docResult.DiagnosticsFixed, docResult.DiagnosticsFound);
                }
                catch (Exception ex)
                {
                    LogDocumentProcessingError(Logger, ex, filePath);
                    results.Add(new DocumentFixResult
                    {
                        FilePath = filePath,
                        Modified = false,
                        DiagnosticsFound = 0,
                        DiagnosticsFixed = 0,
                        ErrorMessage = ex.Message
                    });

                    if (configuration.StopOnFirstError)
                    {
                        break;
                    }
                }
            }

            var duration = DateTime.UtcNow - startTime;

            LogBatchCompleted(Logger, totalFixed, totalDiagnostics, duration.TotalMilliseconds);

            return new BatchFixResult
            {
                Success = true,
                FixProviderId = Id,
                DocumentResults = results.ToImmutableArray(),
                TotalDiagnostics = totalDiagnostics,
                TotalFixed = totalFixed,
                Duration = duration
            };
        }
        catch (Exception ex)
        {
            LogBatchFailed(Logger, ex, Name);

            return new BatchFixResult
            {
                Success = false,
                FixProviderId = Id,
                DocumentResults = results.ToImmutableArray(),
                TotalDiagnostics = totalDiagnostics,
                TotalFixed = totalFixed,
                Duration = DateTime.UtcNow - startTime,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Apply fixes to a single document
    /// Derived classes should override this to implement specific fix logic
    /// </summary>
    protected abstract Task<DocumentFixResult> ApplySingleDocumentFixAsync(
        Document document,
        FixConfiguration configuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Helper method to get all diagnostics for a document using the analyzer
    /// </summary>
    protected async Task<ImmutableArray<Diagnostic>> GetDocumentDiagnosticsAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        try
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
                return ImmutableArray<Diagnostic>.Empty;

            var compilation = semanticModel.Compilation;
            var analyzer = GetAnalyzer();

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create(analyzer),
                new CompilationWithAnalyzersOptions(
                    options: new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
                    onAnalyzerException: null,
                    concurrentAnalysis: true,
                    logAnalyzerExecutionTime: true));

            var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);

            // Filter to only this document
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
            return allDiagnostics
                .Where(d => d.Location.SourceTree == syntaxTree)
                .ToImmutableArray();
        }
        catch (Exception ex)
        {
            LogGetDiagnosticsError(Logger, ex, document.FilePath);
            return ImmutableArray<Diagnostic>.Empty;
        }
    }

    /// <summary>
    /// Helper method to apply a code action and return the modified document
    /// </summary>
    protected static async Task<Document> ApplyCodeActionAsync(
        Document document,
        CodeAction codeAction,
        CancellationToken cancellationToken)
    {
        var operations = await codeAction.GetOperationsAsync(cancellationToken);

        foreach (var operation in operations)
        {
            if (operation is ApplyChangesOperation applyChangesOp)
            {
                var changedSolution = applyChangesOp.ChangedSolution;
                var changedDocument = changedSolution.GetDocument(document.Id);

                if (changedDocument != null)
                {
                    return changedDocument;
                }
            }
        }

        return document;
    }

    /// <summary>
    /// Logs (at trace level) that a fix was applied for the given diagnostic
    /// </summary>
    protected void LogAppliedFix(Diagnostic diagnostic)
    {
        if (Logger.IsEnabled(LogLevel.Trace))
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            LogAppliedFixAt(Logger, lineSpan);
        }
    }

    [LoggerMessage(Level = LogLevel.Trace, Message = "Applied fix for diagnostic at {Location}")]
    private static partial void LogAppliedFixAt(ILogger logger, FileLinePositionSpan location);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error fixing document: {FilePath}")]
    protected static partial void LogFixDocumentError(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting batch fix operation for {ProviderName} on {DocumentCount} documents")]
    private static partial void LogBatchStarting(ILogger logger, string providerName, int documentCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {FilePath}: {Fixed}/{Found} diagnostics fixed")]
    private static partial void LogDocumentProcessed(ILogger logger, string filePath, int @fixed, int found);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error processing document: {FilePath}")]
    private static partial void LogDocumentProcessingError(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Batch fix operation completed: {Fixed}/{Total} diagnostics fixed in {Duration}ms")]
    private static partial void LogBatchCompleted(ILogger logger, int @fixed, int total, double duration);

    [LoggerMessage(Level = LogLevel.Error, Message = "Batch fix operation failed for {ProviderName}")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception, string providerName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error getting diagnostics for document: {FilePath}")]
    private static partial void LogGetDiagnosticsError(ILogger logger, Exception exception, string? filePath);
}

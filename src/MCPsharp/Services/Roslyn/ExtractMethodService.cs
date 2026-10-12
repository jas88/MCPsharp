using System.Text;
using MCPsharp.Models.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Extracts a selection of statements (or a single expression) into a new method using Roslyn.
/// Data flow analysis determines the parameters (by value or <c>ref</c>), the returned variables
/// (a single value or a tuple), async-ness, static-ness and method type parameters. The rewritten
/// document is formatted and compiled; in non-preview mode it is written to disk, applied to the
/// workspace and backed up in a rollback session first.
/// </summary>
public partial class ExtractMethodService
{
    private const string DefaultMethodName = "ExtractedMethod";
    private const string RollbackOperationType = "ExtractMethod";

    private static readonly HashSet<string> AllowedAccessibilities = new(StringComparer.Ordinal)
    {
        "private", "public", "protected", "internal", "protected internal", "private protected"
    };

    private readonly RoslynWorkspace _workspace;
    private readonly ILogger<ExtractMethodService> _logger;
    private readonly IBulkEditService? _rollbackStore;

    public ExtractMethodService(RoslynWorkspace workspace)
        : this(workspace, NullLogger<ExtractMethodService>.Instance)
    {
    }

    /// <summary>
    /// Create the service.
    /// </summary>
    /// <param name="workspace">Workspace holding the document to refactor</param>
    /// <param name="logger">Logger</param>
    /// <param name="rollbackStore">
    /// Service recording a backup/rollback session before a file is rewritten; when null, applied
    /// extractions are not backed up (a warning is returned).
    /// </param>
    public ExtractMethodService(
        RoslynWorkspace workspace,
        ILogger<ExtractMethodService> logger,
        IBulkEditService? rollbackStore = null)
    {
        _workspace = workspace;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rollbackStore = rollbackStore;
    }

    /// <summary>
    /// Extract the selected code into a new method. With <see cref="ExtractMethodRequest.Preview"/> set
    /// the extraction is only computed; otherwise the file is rewritten (and refused if the rewrite
    /// would introduce compilation errors).
    /// </summary>
    public async Task<ExtractMethodResult> ExtractMethodAsync(
        ExtractMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var requestError = ValidateRequest(request);
            if (requestError != null)
            {
                return requestError;
            }

            var document = _workspace.GetDocument(request.FilePath);
            if (document == null)
            {
                return ExtractMethodResult.CreateError("Document not found: " + request.FilePath);
            }

            document = await SyncWithDiskAsync(document, cancellationToken);

            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root == null)
            {
                return ExtractMethodResult.CreateError("Unable to get syntax root for document");
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            if (semanticModel == null)
            {
                return ExtractMethodResult.CreateError("Unable to get semantic model for document");
            }

            var lineCount = root.GetText().Lines.Count;
            if (request.EndLine > lineCount)
            {
                return ExtractMethodResult.CreateError($"Line numbers exceed document length (document has {lineCount} lines)");
            }

            var span = GetTextSpan(root, request);
            var (selection, selectionError) = ResolveSelection(root, semanticModel, span);
            if (selectionError != null)
            {
                return selectionError;
            }

            var (analysis, analysisError) = Analyze(selection!, semanticModel, request);
            if (analysisError != null)
            {
                return analysisError;
            }

            var plan = new ExtractionPlan
            {
                Document = document,
                Root = root,
                Model = semanticModel,
                Selection = selection!,
                Analysis = analysis!,
                MethodName = ChooseMethodName(request.MethodName, selection!, semanticModel),
                Accessibility = request.Accessibility?.Trim() is { Length: > 0 } a ? a : "private",
                SelectionStart = span.Start
            };

            return await RewriteAsync(plan, request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogExtractionFailed(_logger, ex, request.FilePath);
            return ExtractMethodResult.CreateError($"Extraction failed: {ex.Message}");
        }
    }

    private static ExtractMethodResult? ValidateRequest(ExtractMethodRequest request)
    {
        if (request.StartLine < 1 || request.EndLine < 1)
        {
            return ExtractMethodResult.CreateError("Line numbers must be greater than 0");
        }

        if (request.StartLine > request.EndLine)
        {
            return ExtractMethodResult.CreateError("Start line cannot be greater than end line");
        }

        if (request.MethodName != null && !IsValidMethodName(request.MethodName))
        {
            return ExtractMethodResult.CreateError(
                $"'{request.MethodName}' is not a valid C# method name", "INVALID_METHOD_NAME");
        }

        if (request.Accessibility != null && !AllowedAccessibilities.Contains(request.Accessibility.Trim()))
        {
            return ExtractMethodResult.CreateError(
                $"Unsupported accessibility '{request.Accessibility}'",
                "INVALID_ACCESSIBILITY",
                AllowedAccessibilities.ToList());
        }

        return null;
    }

    private static bool IsValidMethodName(string name) =>
        SyntaxFacts.IsValidIdentifier(name) &&
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None &&
        SyntaxFacts.GetContextualKeywordKind(name) is not (SyntaxKind.VarKeyword or SyntaxKind.NameOfKeyword);

    /// <summary>
    /// Line numbers come from the file as it is on disk; if another tool changed the file without
    /// updating the workspace, reload the document text first.
    /// </summary>
    private async Task<Document> SyncWithDiskAsync(Document document, CancellationToken ct)
    {
        var path = document.FilePath;
        if (path == null || !File.Exists(path))
        {
            return document;
        }

        var diskText = await File.ReadAllTextAsync(path, ct);
        var workspaceText = await document.GetTextAsync(ct);
        if (string.Equals(diskText, workspaceText.ToString(), StringComparison.Ordinal))
        {
            return document;
        }

        LogResyncingDocument(_logger, path);
        await _workspace.UpdateDocumentAsync(path, diskText, ct);
        return _workspace.GetDocument(path) ?? document;
    }

    /// <summary>
    /// Back up the file in a rollback session, apply the rewritten text to the workspace (which
    /// refreshes dependent projects) and write it to disk, preserving the file's encoding.
    /// </summary>
    private async Task<ExtractMethodResult> ApplyAsync(
        ExtractionPlan plan,
        RewriteOutcome outcome,
        ExtractedMethodInfo extraction,
        string filePath,
        List<ExtractionDiagnostic> newDiagnostics,
        CancellationToken ct)
    {
        if (!File.Exists(filePath))
        {
            return ExtractMethodResult.CreateError(
                $"Cannot apply the extraction: {filePath} does not exist on disk", "FILE_NOT_ON_DISK");
        }

        var warnings = new List<WarningInfo>();
        string? rollbackId = null;
        if (_rollbackStore != null)
        {
            var rollback = await _rollbackStore.CreateRollbackSessionAsync(RollbackOperationType, [filePath], ct);
            if (rollback.Files.Count == 0)
            {
                return ExtractMethodResult.CreateError(
                    $"Cannot apply the extraction: backing up {filePath} failed", "BACKUP_FAILED");
            }

            rollbackId = rollback.RollbackId;
        }
        else
        {
            warnings.Add(new WarningInfo { Code = "NO_ROLLBACK", Message = "No rollback service available; the change was not backed up" });
        }

        var newContent = outcome.NewText.ToString();
        var encoding = await DetectEncodingAsync(filePath, ct);
        if (!await _workspace.UpdateDocumentAsync(filePath, newContent, ct))
        {
            warnings.Add(new WarningInfo { Code = "WORKSPACE_NOT_UPDATED", Message = "The workspace could not be updated; it will resynchronise from disk on the next extraction" });
        }

        await File.WriteAllTextAsync(filePath, newContent, encoding, ct);
        LogExtractionApplied(_logger, plan.MethodName, filePath, rollbackId);

        warnings.AddRange(newDiagnostics.Select(d => new WarningInfo { Code = d.Id, Message = d.Message, Line = d.Line }));
        return ExtractMethodResult.CreateSuccess(extraction, warnings: warnings) with
        {
            Applied = true,
            FilePath = filePath,
            RollbackId = rollbackId,
            NewDiagnostics = newDiagnostics
        };
    }

    private static async Task<Encoding> DetectEncodingAsync(string filePath, CancellationToken ct)
    {
        var bom = new byte[3];
        int read;
        await using (var stream = File.OpenRead(filePath))
        {
            read = await stream.ReadAtLeastAsync(bom, bom.Length, throwOnEndOfStream: false, ct);
        }

        return read switch
        {
            >= 3 when bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            >= 2 when bom[0] == 0xFF && bom[1] == 0xFE => Encoding.Unicode,
            >= 2 when bom[0] == 0xFE && bom[1] == 0xFF => Encoding.BigEndianUnicode,
            _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
        };
    }

    private static TextSpan GetTextSpan(SyntaxNode root, ExtractMethodRequest request)
    {
        var text = root.GetText();

        var startLine = text.Lines[request.StartLine - 1];
        var endLine = text.Lines[request.EndLine - 1];

        var startColumn = Math.Max(1, request.StartColumn ?? 1);
        var endColumn = request.EndColumn ?? endLine.Span.Length;

        if (startColumn > startLine.Span.Length + 1)
        {
            throw new ArgumentException($"Invalid start column: {startColumn} for line {request.StartLine} (line length: {startLine.Span.Length + 1})");
        }

        if (endColumn < 0 || endColumn > endLine.Span.Length + 1)
        {
            throw new ArgumentException($"Invalid end column: {endColumn} for line {request.EndLine} (line length: {endLine.Span.Length + 1})");
        }

        var start = startLine.Start + startColumn - 1;
        var end = Math.Min(endLine.Start + endColumn, endLine.EndIncludingLineBreak);

        if (start > end)
        {
            throw new ArgumentException($"Invalid text span calculated: start={start}, end={end}, text length={text.Length}");
        }

        return TextSpan.FromBounds(start, end);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Method extraction failed for {FilePath}")]
    private static partial void LogExtractionFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Workspace text of {FilePath} differs from disk; reloading before extraction")]
    private static partial void LogResyncingDocument(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted method {MethodName} into {FilePath} (rollback {RollbackId})")]
    private static partial void LogExtractionApplied(ILogger logger, string methodName, string filePath, string? rollbackId);
}

using Microsoft.CodeAnalysis;

namespace MCPsharp.Models.Refactoring;

/// <summary>
/// Request for extracting a method from selected code
/// </summary>
public record ExtractMethodRequest
{
    public required string FilePath { get; init; }
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public int? StartColumn { get; init; }
    public int? EndColumn { get; init; }
    public string? MethodName { get; init; }
    public string? Accessibility { get; init; } = "private";
    public bool? MakeStatic { get; init; }
    public bool Preview { get; init; } = true;
}

/// <summary>
/// Result of extract method operation
/// </summary>
public record ExtractMethodResult
{
    public bool Success { get; init; }
    public ExtractedMethodInfo? Extraction { get; init; }
    public PreviewInfo? Preview { get; init; }
    public ErrorInfo? Error { get; init; }
    public List<WarningInfo> Warnings { get; init; } = new();

    public static ExtractMethodResult CreateSuccess(
        ExtractedMethodInfo extraction,
        PreviewInfo? preview = null,
        List<WarningInfo>? warnings = null)
    {
        return new ExtractMethodResult
        {
            Success = true,
            Extraction = extraction,
            Preview = preview,
            Warnings = warnings ?? new()
        };
    }

    public static ExtractMethodResult CreateError(string message, string? code = null, List<string>? suggestions = null)
    {
        return new ExtractMethodResult
        {
            Success = false,
            Error = new ErrorInfo
            {
                Code = code ?? "EXTRACTION_FAILED",
                Message = message,
                Suggestions = suggestions ?? new()
            }
        };
    }
}

/// <summary>
/// Information about the extracted method
/// </summary>
public record ExtractedMethodInfo
{
    public required MethodInfo Method { get; init; }
    public required CallSiteInfo CallSite { get; init; }
    public required List<ParameterInfo> Parameters { get; init; }
    public required string ReturnType { get; init; }
    public List<string>? ReturnVariables { get; init; }
    public required MethodCharacteristics Characteristics { get; init; }
}

/// <summary>
/// Information about the generated method
/// </summary>
public record MethodInfo
{
    public required string Name { get; init; }
    public required string Signature { get; init; }
    public required string Body { get; init; }
    public required LocationInfo Location { get; init; }
}

/// <summary>
/// Information about the call site replacement
/// </summary>
public record CallSiteInfo
{
    public required string Code { get; init; }
    public required LocationInfo Location { get; init; }
}

/// <summary>
/// Parameter information
/// </summary>
public record ParameterInfo
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string? Modifier { get; init; } // "ref", "out", "in", null
}

/// <summary>
/// Method characteristics
/// </summary>
public record MethodCharacteristics
{
    public bool IsAsync { get; init; }
    public bool IsStatic { get; init; }
    public bool IsGeneric { get; init; }
    public bool HasMultipleReturns { get; init; }
    public bool HasEarlyReturns { get; init; }
    public bool CapturesVariables { get; init; }
    public bool ContainsAwait { get; init; }
    public bool ContainsYield { get; init; }
}

/// <summary>
/// Location information
/// </summary>
public record LocationInfo
{
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required int Column { get; init; }
}

/// <summary>
/// Preview information showing before/after
/// </summary>
public record PreviewInfo
{
    public required string OriginalCode { get; init; }
    public required string ModifiedCode { get; init; }
    public string? Diff { get; init; }
}

/// <summary>
/// Error information
/// </summary>
public record ErrorInfo
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public List<string> Suggestions { get; init; } = new();
}

/// <summary>
/// Warning information
/// </summary>
public record WarningInfo
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public int? Line { get; init; }
}

/// <summary>
/// Internal data flow analysis result
/// </summary>
internal record DataFlowAnalysisResult
{
    public List<ISymbol> InputParameters { get; init; } = new();
    public List<ISymbol> OutputVariables { get; init; } = new();
    public List<ISymbol> RefParameters { get; init; } = new();
    public List<ISymbol> CapturedVariables { get; init; } = new();
    public bool HasMultipleReturns { get; init; }
    public bool HasEarlyReturns { get; init; }
}

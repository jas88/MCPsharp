namespace MCPsharp.Models.Database;

/// <summary>
/// Represents a cached code analysis diagnostic.
/// </summary>
public sealed class DbAnalysisResult
{
    public long Id { get; set; }
    public long FileId { get; set; }
    public required string DiagnosticId { get; set; }
    public required string Severity { get; set; }  // Error, Warning, Info, Hidden
    public required string Message { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public DateTime CachedAt { get; set; }
}

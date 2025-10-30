using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for predicting cross-file impact of code changes
/// Interface to be implemented by Phase 2 agents
/// </summary>
public interface IImpactAnalyzerService
{
    Task<ImpactAnalysisResult> AnalyzeImpactAsync(CodeChange change);
}

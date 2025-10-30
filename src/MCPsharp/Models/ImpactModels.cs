namespace MCPsharp.Models;

public class CodeChange
{
    public required string FilePath { get; init; }
    public required string ChangeType { get; init; } // "signature_change", "rename", "delete", "add"
    public required string SymbolName { get; init; }
    public string? OldSignature { get; init; }
    public string? NewSignature { get; init; }
}

public class ImpactAnalysisResult
{
    public required CodeChange Change { get; init; }
    public required IReadOnlyList<CSharpImpact> CSharpImpacts { get; init; }
    public required IReadOnlyList<ConfigImpact> ConfigImpacts { get; init; }
    public required IReadOnlyList<WorkflowImpact> WorkflowImpacts { get; init; }
    public required IReadOnlyList<DocumentationImpact> DocumentationImpacts { get; init; }
    public required int TotalImpactedFiles { get; init; }
}

public class CSharpImpact
{
    public required string FilePath { get; init; }
    public required int Line { get; init; }
    public required string ImpactType { get; init; } // "breaking_change", "reference", "inheritance"
    public required string Description { get; init; }
}

public class ConfigImpact
{
    public required string FilePath { get; init; }
    public required string ConfigKey { get; init; }
    public required string ImpactType { get; init; } // "behavior", "validation", "reference"
    public required string Description { get; init; }
}

public class WorkflowImpact
{
    public required string FilePath { get; init; }
    public required string JobName { get; init; }
    public required bool IsBreaking { get; init; }
    public required string Description { get; init; }
    public string? Suggestion { get; init; }
}

public class DocumentationImpact
{
    public required string FilePath { get; init; }
    public string? Section { get; init; }
    public required bool NeedsUpdate { get; init; }
    public required string Description { get; init; }
}

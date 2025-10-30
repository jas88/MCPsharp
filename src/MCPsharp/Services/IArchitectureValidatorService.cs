using MCPsharp.Models.Architecture;

namespace MCPsharp.Services;

/// <summary>
/// Service for validating architectural layers and detecting violations in C# projects
/// </summary>
public interface IArchitectureValidatorService
{
    /// <summary>
    /// Validate project architecture against defined rules
    /// </summary>
    Task<ArchitectureValidationResult> ValidateArchitectureAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detect all architectural layer violations in a project
    /// </summary>
    Task<List<LayerViolation>> DetectLayerViolationsAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze dependency graph and relationships between layers
    /// </summary>
    Task<DependencyAnalysisResult> AnalyzeDependenciesAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate comprehensive architecture compliance report
    /// </summary>
    Task<ArchitectureReport> GetArchitectureReportAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Define or update custom architecture definition
    /// </summary>
    Task<ArchitectureDefinition> DefineCustomArchitectureAsync(ArchitectureDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze circular dependencies between layers and components
    /// </summary>
    Task<List<MCPsharp.Models.Architecture.CircularDependency>> AnalyzeCircularDependenciesAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate architecture diagram data for visualization
    /// </summary>
    Task<ArchitectureDiagram> GenerateArchitectureDiagramAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recommended fixes for architectural violations
    /// </summary>
    Task<List<ArchitectureRecommendation>> GetRecommendationsAsync(List<LayerViolation> violations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a specific type follows architectural rules
    /// </summary>
    Task<TypeComplianceResult> CheckTypeComplianceAsync(string typeName, ArchitectureDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get predefined architecture templates (Clean Architecture, Onion, N-Tier, Hexagonal)
    /// </summary>
    Task<Dictionary<string, ArchitectureDefinition>> GetPredefinedArchitecturesAsync(CancellationToken cancellationToken = default);
}
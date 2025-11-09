using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models.Architecture;
using ConsolidatedArchitectureDefinition = MCPsharp.Models.Consolidated.ArchitectureDefinition;
using ConsolidatedArchitectureLayer = MCPsharp.Models.Consolidated.ArchitectureLayer;
using ConsolidatedArchitectureWarning = MCPsharp.Models.Consolidated.ArchitectureWarning;
using ArchitectureRecommendation = MCPsharp.Models.Architecture.ArchitectureRecommendation;
using ConsolidatedDependencyRule = MCPsharp.Models.Consolidated.DependencyRule;
using ArchitectureDependencyRule = MCPsharp.Models.Architecture.DependencyRule;
using ConsolidatedDependencyNode = MCPsharp.Models.Consolidated.DependencyNode;
using ArchitectureDependencyNode = MCPsharp.Models.Architecture.DependencyNode;
using ConsolidatedDependencyEdge = MCPsharp.Models.Consolidated.DependencyEdge;
using ArchitectureDependencyEdge = MCPsharp.Models.Architecture.DependencyEdge;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes architecture-level code organization and layer compliance.
/// </summary>
public class ArchitectureAnalyzer
{
    private readonly IArchitectureValidatorService? _architectureValidator;
    private readonly ILogger<ArchitectureAnalyzer> _logger;

    public ArchitectureAnalyzer(
        IArchitectureValidatorService? architectureValidator = null,
        ILogger<ArchitectureAnalyzer>? logger = null)
    {
        _architectureValidator = architectureValidator;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ArchitectureAnalyzer>.Instance;
    }

    public async Task<ConsolidatedArchitectureDefinition> GetDefaultArchitectureDefinitionAsync(CancellationToken ct)
    {
        try
        {
            return new ConsolidatedArchitectureDefinition
            {
                Name = "Default Layered Architecture",
                Layers = new List<ConsolidatedArchitectureLayer>
                {
                    new() { Name = "Presentation", Level = 1, NamespacePatterns = new List<string> { "*.Presentation", "*.UI", "*.Web" }, Types = new List<string> { "Controller", "ViewModel", "View" } },
                    new() { Name = "Application", Level = 2, NamespacePatterns = new List<string> { "*.Application", "*.Services" }, Types = new List<string> { "Service", "Handler", "UseCase" } },
                    new() { Name = "Domain", Level = 3, NamespacePatterns = new List<string> { "*.Domain", "*.Core" }, Types = new List<string> { "Entity", "Aggregate", "ValueObject" } },
                    new() { Name = "Infrastructure", Level = 4, NamespacePatterns = new List<string> { "*.Infrastructure", "*.Data" }, Types = new List<string> { "Repository", "DbContext", "Service" } }
                },
                Rules = new List<ConsolidatedDependencyRule>
                {
                    new() { FromLayer = "Presentation", ToLayer = "Application", Allowed = true, Reason = "Presentation can depend on Application" },
                    new() { FromLayer = "Application", ToLayer = "Domain", Allowed = true, Reason = "Application can depend on Domain" },
                    new() { FromLayer = "Infrastructure", ToLayer = "Domain", Allowed = true, Reason = "Infrastructure can depend on Domain" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting default architecture definition");
        }

        return new ConsolidatedArchitectureDefinition
        {
            Name = "Default Architecture",
            Layers = new List<ConsolidatedArchitectureLayer>(),
            Rules = new List<ConsolidatedDependencyRule>()
        };
    }

    public async Task<ArchitectureValidation?> ValidateArchitectureAsync(
        string scope,
        ConsolidatedArchitectureDefinition definition,
        CancellationToken ct)
    {
        try
        {
            if (_architectureValidator == null) return null;

            var architectureDefinition = ConvertToArchitectureDefinition(definition);
            var result = await _architectureValidator.ValidateArchitectureAsync(scope, architectureDefinition, ct);

            return ConvertToValidationResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating architecture for {Scope}", scope);
        }

        return null;
    }

    public async Task<List<ArchitectureViolation>> DetectLayerViolationsAsync(
        string scope,
        ConsolidatedArchitectureDefinition definition,
        CancellationToken ct)
    {
        try
        {
            if (_architectureValidator == null) return new List<ArchitectureViolation>();

            var architectureDefinition = ConvertToArchitectureDefinition(definition);
            var layerViolations = await _architectureValidator.DetectLayerViolationsAsync(scope, architectureDefinition, ct);

            return layerViolations.Select(v => new ArchitectureViolation
            {
                From = v.SourceLayer,
                To = v.TargetLayer,
                Rule = v.RuleViolated,
                Location = new Location { FilePath = v.FilePath, Line = v.LineNumber, Column = v.ColumnNumber },
                Description = v.Description
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting layer violations for {Scope}", scope);
        }

        return new List<ArchitectureViolation>();
    }

    public async Task<DependencyGraph?> AnalyzeDependenciesAsync(
        string scope,
        ConsolidatedArchitectureDefinition definition,
        CancellationToken ct)
    {
        try
        {
            if (_architectureValidator == null) return null;

            var architectureDefinition = ConvertToArchitectureDefinition(definition);
            var dependencyAnalysis = await _architectureValidator.AnalyzeDependenciesAsync(scope, architectureDefinition, ct);

            return ConvertToDependencyGraph(dependencyAnalysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing architecture dependencies for {Scope}", scope);
        }

        return null;
    }

    public async Task<List<ArchitectureRecommendation>> GetRecommendationsAsync(
        List<LayerViolation> violations,
        CancellationToken ct)
    {
        try
        {
            if (_architectureValidator == null) return new List<ArchitectureRecommendation>();

            var recommendations = await _architectureValidator.GetRecommendationsAsync(violations, ct);

            return recommendations.Select(r => new ArchitectureRecommendation
            {
                Type = r.Type,
                Title = r.Title,
                Description = r.Description,
                Priority = r.Priority,
                Steps = r.Steps,
                CodeExample = r.CodeExample,
                Effort = r.Effort,
                Impact = r.Impact
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting architecture recommendations");
        }

        return new List<ArchitectureRecommendation>();
    }

    public async Task<ArchitectureMetrics?> CalculateArchitectureMetricsAsync(
        string scope,
        MCPsharp.Models.Architecture.ArchitectureDefinition definition,
        CancellationToken ct)
    {
        try
        {
            var layerCount = definition.Layers.Count;

            return new ArchitectureMetrics
            {
                TotalLayers = layerCount,
                TotalViolations = 0,
                Compliance = 100.0,
                LayerMetrics = new Dictionary<string, int>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating architecture metrics for {Scope}", scope);
        }

        return null;
    }

    private static MCPsharp.Models.Architecture.ArchitectureDefinition ConvertToArchitectureDefinition(ConsolidatedArchitectureDefinition consolidatedDef)
    {
        return new MCPsharp.Models.Architecture.ArchitectureDefinition
        {
            Name = consolidatedDef.Name,
            Description = consolidatedDef.Description,
            Type = MCPsharp.Models.Architecture.ArchitectureType.Layered,
            Layers = consolidatedDef.Layers.Select(l => new MCPsharp.Models.Architecture.ArchitecturalLayer
            {
                Name = l.Name,
                Description = l.Description ?? "",
                Level = l.Order,
                NamespacePatterns = l.NamespacePatterns?.ToList() ?? new List<string>(),
                AllowedDependencies = new List<string>(),
                ForbiddenDependencies = new List<string>()
            }).ToList(),
            DependencyRules = consolidatedDef.DependencyRules.Select(r => new ArchitectureDependencyRule
            {
                FromLayer = r.FromLayer,
                ToLayer = r.ToLayer,
                Direction = r.Direction,
                Type = r.Type,
                Description = r.Description,
                IsStrict = r.IsStrict
            }).ToList(),
            NamingPatterns = new List<MCPsharp.Models.Architecture.NamingPattern>(),
            ForbiddenPatterns = new List<MCPsharp.Models.Architecture.ForbiddenPattern>()
        };
    }

    private static ArchitectureValidation ConvertToValidationResult(ArchitectureValidationResult result)
    {
        return new ArchitectureValidation
        {
            IsValid = result.IsValid,
            Violations = result.Violations.Select(v => new ArchitectureViolation
            {
                From = v.SourceLayer,
                To = v.TargetLayer,
                Rule = v.ViolationType,
                Location = new Location { FilePath = v.FilePath, Line = v.LineNumber, Column = v.ColumnNumber },
                Description = v.Description
            }).ToList(),
            Warnings = result.Warnings.Select(w => new ConsolidatedArchitectureWarning
            {
                Type = w.WarningType,
                Message = w.Description,
                Location = new Location { FilePath = w.FilePath, Line = w.LineNumber, Column = 0 }
            }).ToList()
        };
    }

    private static DependencyGraph ConvertToDependencyGraph(MCPsharp.Models.Architecture.DependencyAnalysisResult sourceAnalysis)
    {
        return new DependencyGraph
        {
            Nodes = sourceAnalysis.Nodes.Select(n => new ConsolidatedDependencyNode
            {
                Id = n.Name,
                Name = n.Name,
                Type = n.Type.ToString(),
                Labels = new List<string>()
            }).ToList(),
            Edges = sourceAnalysis.Edges.Select(e => new ConsolidatedDependencyEdge
            {
                From = e.From,
                To = e.To,
                Type = e.Type.ToString(),
                Weight = 1.0
            }).ToList()
        };
    }
}

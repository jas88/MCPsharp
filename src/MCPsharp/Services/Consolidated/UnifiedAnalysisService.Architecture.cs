using System.Globalization;
using MCPsharp.Models.Consolidated;
using MCPsharp.Models.Architecture;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    /// <summary>
    /// analyze_architecture - Architecture analysis
    /// Consolidates: Architecture validation tools
    /// </summary>
    public async Task<ArchitectureAnalysisResponse> AnalyzeArchitectureAsync(ArchitectureAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_architectureValidator == null)
            {
                return new ArchitectureAnalysisResponse
                {
                    Scope = request.Scope.ToString(),
                    Error = "Architecture validation service not available",
                    Metadata = new ResponseMetadata { Success = false, ProcessingTime = stopwatch.Elapsed }
                };
            }

            if (string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                return new ArchitectureAnalysisResponse
                {
                    Scope = request.Scope.ToString(),
                    Error = ProjectPathRequiredError,
                    Metadata = new ResponseMetadata { Success = false, ProcessingTime = stopwatch.Elapsed }
                };
            }

            var response = new ArchitectureAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // The scope selects the sections: Definition validates the project against the architecture
            // definition, References reports layer violations, Dependencies analyses layer dependencies.
            var projectPath = request.ProjectPath;
            var architectureDefinition = ConvertToArchitectureDefinition(request.Definition ?? GetDefaultArchitectureDefinition());
            var includeValidation = request.Scope is AnalysisScope.Definition or AnalysisScope.All;
            var includeViolations = request.Scope is AnalysisScope.References or AnalysisScope.All;
            var includeDependencies = request.Scope is AnalysisScope.Dependencies or AnalysisScope.All;
            var includeRecommendations = request.Options?.Include.HasFlag(IncludeOptions.Suggestions) == true;

            List<LayerViolation>? layerViolations = null;
            if (includeValidation)
            {
                var validation = await _architectureValidator.ValidateArchitectureAsync(projectPath, architectureDefinition, ct);
                response.Validation = ConvertToValidationResult(validation);
                layerViolations = validation.Violations;
            }

            if (includeViolations || includeRecommendations)
            {
                layerViolations ??= await _architectureValidator.DetectLayerViolationsAsync(projectPath, architectureDefinition, ct);
            }

            if (includeViolations)
            {
                response.Violations = layerViolations!.Select(v => new ArchitectureViolation
                {
                    From = v.SourceLayer,
                    To = v.TargetLayer,
                    Rule = v.RuleViolated,
                    Location = new MCPsharp.Models.Consolidated.Location { FilePath = v.FilePath, Line = v.LineNumber, Column = v.ColumnNumber },
                    Description = v.Description
                }).ToList();
            }

            if (includeDependencies)
            {
                var dependencyAnalysis = await _architectureValidator.AnalyzeDependenciesAsync(projectPath, architectureDefinition, ct);
                response.DependencyAnalysis = new DependencyAnalysisResponse
                {
                    Scope = request.Scope.ToString(),
                    Depth = 2,
                    Graph = ConvertToDependencyGraph(dependencyAnalysis),
                    Metadata = new ResponseMetadata { Success = true }
                };
            }

            // Generate recommendations
            if (includeRecommendations)
            {
                var recommendations = await _architectureValidator.GetRecommendationsAsync(layerViolations!, ct);
                response.Recommendations = recommendations.Select(r => new MCPsharp.Models.Consolidated.ArchitectureRecommendation
                {
                    Type = r.Type,
                    Description = r.Description,
                    Target = r.Title, // Use Title instead of TargetType
                    Priority = Convert.ToDouble(r.Priority == 0 ? 1.0 : r.Priority, CultureInfo.InvariantCulture), // Convert to double if needed
                    AffectedComponents = new List<string>() // No AffectedComponents property available
                }).ToList();
            }

            // Architecture metrics
            if (request.Options?.Include.HasFlag(IncludeOptions.Metrics) == true)
            {
                response.Metrics = CalculateArchitectureMetrics(projectPath, architectureDefinition);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingArchitectureFor(_logger, ex, request.Scope);

            return new ArchitectureAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    private MCPsharp.Models.Consolidated.ArchitectureDefinition GetDefaultArchitectureDefinition()
    {
        try
        {
            // Default layered architecture definition
            return new MCPsharp.Models.Consolidated.ArchitectureDefinition
            {
                Name = "Default Layered Architecture",
                Layers = new List<MCPsharp.Models.Consolidated.ArchitectureLayer>
                {
                    new() { Name = "Presentation", Level = 1, NamespacePatterns = new List<string> { "*.Presentation", "*.UI", "*.Web" }, Types = new List<string> { "Controller", "ViewModel", "View" } },
                    new() { Name = "Application", Level = 2, NamespacePatterns = new List<string> { "*.Application", "*.Services" }, Types = new List<string> { "Service", "Handler", "UseCase" } },
                    new() { Name = "Domain", Level = 3, NamespacePatterns = new List<string> { "*.Domain", "*.Core" }, Types = new List<string> { "Entity", "Aggregate", "ValueObject" } },
                    new() { Name = "Infrastructure", Level = 4, NamespacePatterns = new List<string> { "*.Infrastructure", "*.Data" }, Types = new List<string> { "Repository", "DbContext", "Service" } }
                },
                Rules = new List<MCPsharp.Models.Consolidated.DependencyRule>
                {
                    new() { FromLayer = "Presentation", ToLayer = "Application", Allowed = true, Reason = "Presentation can depend on Application" },
                    new() { FromLayer = "Application", ToLayer = "Domain", Allowed = true, Reason = "Application can depend on Domain" },
                    new() { FromLayer = "Infrastructure", ToLayer = "Domain", Allowed = true, Reason = "Infrastructure can depend on Domain" }
                }
            };
        }
        catch (Exception ex)
        {
            LogErrorGettingDefaultArchitectureDefinition(_logger, ex);
        }

        return new MCPsharp.Models.Consolidated.ArchitectureDefinition
        {
            Name = "Default Architecture",
            Layers = new List<MCPsharp.Models.Consolidated.ArchitectureLayer>(),
            Rules = new List<MCPsharp.Models.Consolidated.DependencyRule>()
        };
    }

    private ArchitectureMetrics? CalculateArchitectureMetrics(string scope, MCPsharp.Models.Architecture.ArchitectureDefinition definition)
    {
        try
        {
            // Basic metrics calculation
            var layerCount = definition.Layers.Count;

            return new ArchitectureMetrics
            {
                TotalLayers = layerCount,
                TotalViolations = 0, // Simplified - would need actual analysis
                Compliance = 100.0, // Simplified - would need actual analysis
                LayerMetrics = new Dictionary<string, int>() // Simplified layer metrics
            };
        }
        catch (Exception ex)
        {
            LogErrorCalculatingArchitectureMetricsFor(_logger, ex, scope);
        }

        return null;
    }
}

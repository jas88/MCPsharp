using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MCPsharp.Models.Architecture;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Service for validating architectural layers and detecting violations in C# projects
/// </summary>
public partial class ArchitectureValidatorService : IArchitectureValidatorService
{
    private readonly RoslynWorkspace _workspace;
    private readonly Dictionary<string, ArchitectureDefinition> _predefinedArchitectures;

    public ArchitectureValidatorService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ITypeUsageService typeUsage)
    {
        _workspace = workspace;
        _predefinedArchitectures = new Dictionary<string, ArchitectureDefinition>();
        InitializePredefinedArchitectures();
    }

    public async Task<ArchitectureValidationResult> ValidateArchitectureAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Use provided definition or detect best fit
        var architecture = definition ?? await DetectBestFitArchitecture(cancellationToken);

        var violations = new List<LayerViolation>();
        var warnings = new List<ArchitectureWarning>();
        var analyzedFiles = new List<string>();
        var totalTypesAnalyzed = 0;
        var compliantTypes = 0;
        var layerStats = new Dictionary<string, int>();

        if (!_workspace.IsInitialized)
        {
            return new ArchitectureValidationResult
            {
                IsValid = false,
                Violations = new List<LayerViolation>(),
                Warnings = new List<ArchitectureWarning>(),
                TotalTypesAnalyzed = 0,
                CompliantTypes = 0,
                CompliancePercentage = 0,
                AnalysisDuration = DateTime.UtcNow - startTime,
                LayerStatistics = layerStats,
                AnalyzedFiles = analyzedFiles
            };
        }

        // Analyze each syntax tree
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            if (string.IsNullOrEmpty(syntaxTree.FilePath) || !syntaxTree.FilePath.EndsWith(".cs", StringComparison.Ordinal))
                continue;

            analyzedFiles.Add(syntaxTree.FilePath);
            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Find all type declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDeclaration in typeDeclarations)
            {
                totalTypesAnalyzed++;
                var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
                if (symbol == null)
                    continue;

                var layer = IdentifyLayer(symbol, architecture);
                if (!string.IsNullOrEmpty(layer))
                {
                    layerStats[layer] = layerStats.GetValueOrDefault(layer, 0) + 1;
                }

                // Validate type against architectural rules
                var typeViolations = ValidateType(symbol, layer, architecture, syntaxTree.FilePath);
                var typeWarnings = AnalyzeTypeWarnings(symbol, layer, syntaxTree.FilePath);

                violations.AddRange(typeViolations);
                warnings.AddRange(typeWarnings);

                if (typeViolations.Count == 0)
                {
                    compliantTypes++;
                }
            }
        }

        var endTime = DateTime.UtcNow;
        var compliancePercentage = totalTypesAnalyzed > 0 ? (double)compliantTypes / totalTypesAnalyzed * 100 : 0;

        return new ArchitectureValidationResult
        {
            IsValid = violations.All(v => v.Severity != ViolationSeverity.Critical && v.Severity != ViolationSeverity.Blocker),
            Violations = violations,
            Warnings = warnings,
            TotalTypesAnalyzed = totalTypesAnalyzed,
            CompliantTypes = compliantTypes,
            CompliancePercentage = Math.Round(compliancePercentage, 2),
            AnalysisDuration = endTime - startTime,
            LayerStatistics = layerStats,
            AnalyzedFiles = analyzedFiles
        };
    }

    public async Task<List<LayerViolation>> DetectLayerViolationsAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var validationResult = await ValidateArchitectureAsync(projectPath, definition, cancellationToken);
        return validationResult.Violations;
    }

    public async Task<DependencyAnalysisResult> AnalyzeDependenciesAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var architecture = definition ?? await DetectBestFitArchitecture(cancellationToken);

        var nodes = new List<DependencyNode>();
        var edges = new List<DependencyEdge>();
        var layerDependencies = new Dictionary<string, List<string>>();

        if (!_workspace.IsInitialized)
        {
            return new DependencyAnalysisResult
            {
                Nodes = nodes,
                Edges = edges,
                CircularDependencies = new List<MCPsharp.Models.Architecture.CircularDependency>(),
                LayerDependencies = layerDependencies,
                DependencyMetrics = new Dictionary<string, int>(),
                AnalysisDuration = DateTime.UtcNow - startTime
            };
        }

        // Build dependency graph
        foreach (var syntaxTree in _workspace.GetSourceTrees())
        {
            if (!syntaxTree.FilePath?.EndsWith(".cs", StringComparison.Ordinal) == true)
                continue;

            var semanticModel = _workspace.GetSemanticModel(syntaxTree);
            if (semanticModel == null)
                continue;

            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDeclaration in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
                if (symbol == null || syntaxTree.FilePath == null)
                    continue;

                var layer = IdentifyLayer(symbol, architecture);
                var node = CreateDependencyNode(symbol, layer, syntaxTree.FilePath);
                nodes.Add(node);

                // Analyze dependencies
                var dependencies = AnalyzeTypeDependencies(symbol, architecture, syntaxTree.FilePath);
                edges.AddRange(dependencies);

                // Update layer dependencies
                if (!string.IsNullOrEmpty(layer))
                {
                    foreach (var toLayer in dependencies.Select(dep => dep.ToLayer).Where(l => !string.IsNullOrEmpty(l) && l != layer))
                    {
                        if (!layerDependencies.TryGetValue(layer, out var targets))
                        {
                            targets = new List<string>();
                            layerDependencies[layer] = targets;
                        }

                        if (!targets.Contains(toLayer))
                            targets.Add(toLayer);
                    }
                }
            }
        }

        // Detect circular dependencies
        var circularDependencies = DetectCircularDependencies(nodes, edges);

        // Calculate metrics
        var metrics = CalculateDependencyMetrics(nodes, edges);

        return new DependencyAnalysisResult
        {
            Nodes = nodes,
            Edges = edges,
            CircularDependencies = circularDependencies,
            LayerDependencies = layerDependencies,
            DependencyMetrics = metrics,
            AnalysisDuration = DateTime.UtcNow - startTime
        };
    }

    public async Task<ArchitectureReport> GetArchitectureReportAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var architecture = definition ?? await DetectBestFitArchitecture(cancellationToken);

        // Run all analyses
        var validationResult = await ValidateArchitectureAsync(projectPath, architecture, cancellationToken);
        var dependencyAnalysis = await AnalyzeDependenciesAsync(projectPath, architecture, cancellationToken);
        var diagram = await GenerateArchitectureDiagramAsync(projectPath, architecture, cancellationToken);

        // Generate recommendations
        var recommendations = await GetRecommendationsAsync(validationResult.Violations, cancellationToken);

        // Create summary
        var summary = CreateReportSummary(validationResult);

        return new ArchitectureReport
        {
            ValidationResult = validationResult,
            DependencyAnalysis = dependencyAnalysis,
            Diagram = diagram,
            Recommendations = recommendations,
            Summary = summary,
            GeneratedAt = DateTime.UtcNow,
            ProjectPath = projectPath,
            ArchitectureUsed = architecture
        };
    }

    public Task<ArchitectureDefinition> DefineCustomArchitectureAsync(ArchitectureDefinition definition, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate the definition
            ValidateArchitectureDefinition(definition);

            // Store for future use (could be persisted to file/database)

            return Task.FromResult(definition);
        }
        catch (Exception ex)
        {
            return Task.FromException<ArchitectureDefinition>(ex);
        }
    }

    public async Task<List<MCPsharp.Models.Architecture.CircularDependency>> AnalyzeCircularDependenciesAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var dependencyAnalysis = await AnalyzeDependenciesAsync(projectPath, definition, cancellationToken);
        return dependencyAnalysis.CircularDependencies;
    }

    public async Task<ArchitectureDiagram> GenerateArchitectureDiagramAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var architecture = definition ?? await DetectBestFitArchitecture(cancellationToken);
        var dependencyAnalysis = await AnalyzeDependenciesAsync(projectPath, architecture, cancellationToken);

        return CreateArchitectureDiagram(dependencyAnalysis, architecture);
    }

    public Task<List<ArchitectureRecommendation>> GetRecommendationsAsync(List<LayerViolation> violations, CancellationToken cancellationToken = default)
    {
        try
        {
            return Task.FromResult(GetRecommendations(violations));
        }
        catch (Exception ex)
        {
            return Task.FromException<List<ArchitectureRecommendation>>(ex);
        }
    }

    private static List<ArchitectureRecommendation> GetRecommendations(List<LayerViolation> violations)
    {
        var recommendations = new List<ArchitectureRecommendation>();

        foreach (var violation in violations)
        {
            var recs = GenerateRecommendationsForViolation(violation);
            recommendations.AddRange(recs);
        }

        // Sort by priority and impact
        return recommendations
            .OrderByDescending(r => r.Priority)
            .ThenByDescending(r => r.Impact)
            .ToList();
    }

    public async Task<TypeComplianceResult> CheckTypeComplianceAsync(string typeName, ArchitectureDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!_workspace.IsInitialized)
        {
            return new TypeComplianceResult
            {
                TypeName = typeName,
                Layer = "Unknown",
                IsCompliant = false,
                Violations = new List<string> { "Type not found" },
                Warnings = new List<string>(),
                Dependencies = new List<DependencyInfo>()
            };
        }

        var symbol = _workspace.GetTypeByMetadataName(typeName);
        if (symbol == null)
        {
            return new TypeComplianceResult
            {
                TypeName = typeName,
                Layer = "Unknown",
                IsCompliant = false,
                Violations = new List<string> { "Type not found in compilation" },
                Warnings = new List<string>(),
                Dependencies = new List<DependencyInfo>()
            };
        }

        var layer = IdentifyLayer(symbol, definition);
        var violations = new List<string>();
        var warnings = new List<string>();
        var dependencies = new List<DependencyInfo>();

        // Check compliance
        var syntaxReferences = symbol.DeclaringSyntaxReferences;
        if (syntaxReferences.Any())
        {
            var syntax = await syntaxReferences.First().GetSyntaxAsync(cancellationToken);
            var filePath = syntax.SyntaxTree.FilePath;

            var typeViolations = ValidateType(symbol, layer, definition, filePath);
            violations.AddRange(typeViolations.Select(v => v.Description));

            var typeDependencies = AnalyzeTypeDependencies(symbol, definition, filePath);
            dependencies.AddRange(typeDependencies.Select(d => new DependencyInfo
            {
                TargetType = d.To,
                TargetLayer = d.ToLayer,
                Type = d.Type,
                IsAllowed = d.IsAllowed,
                FilePath = d.FilePath,
                LineNumber = d.LineNumber
            }));
        }

        return new TypeComplianceResult
        {
            TypeName = typeName,
            Layer = layer,
            IsCompliant = violations.Count == 0,
            Violations = violations,
            Warnings = warnings,
            Dependencies = dependencies
        };
    }

    public Task<Dictionary<string, ArchitectureDefinition>> GetPredefinedArchitecturesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new Dictionary<string, ArchitectureDefinition>(_predefinedArchitectures));
    }
}

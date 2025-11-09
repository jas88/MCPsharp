using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;
using MCPsharp.Models.Architecture;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Service for validating architectural layers and detecting violations in C# projects
/// </summary>
public class ArchitectureValidatorService : IArchitectureValidatorService
{
    private readonly RoslynWorkspace _workspace;
    private readonly SymbolQueryService _symbolQuery;
    private readonly ICallerAnalysisService _callerAnalysis;
    private readonly ITypeUsageService _typeUsage;
    private readonly Dictionary<string, ArchitectureDefinition> _predefinedArchitectures;

    public ArchitectureValidatorService(
        RoslynWorkspace workspace,
        SymbolQueryService symbolQuery,
        ICallerAnalysisService callerAnalysis,
        ITypeUsageService typeUsage)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
        _callerAnalysis = callerAnalysis;
        _typeUsage = typeUsage;
        _predefinedArchitectures = new Dictionary<string, ArchitectureDefinition>();
        InitializePredefinedArchitectures();
    }

    public async Task<ArchitectureValidationResult> ValidateArchitectureAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        // Use provided definition or detect best fit
        var architecture = definition ?? await DetectBestFitArchitecture(projectPath, cancellationToken);

        var violations = new List<LayerViolation>();
        var warnings = new List<ArchitectureWarning>();
        var analyzedFiles = new List<string>();
        var totalTypesAnalyzed = 0;
        var compliantTypes = 0;
        var layerStats = new Dictionary<string, int>();

        var compilation = _workspace.GetCompilation();
        if (compilation == null)
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
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (string.IsNullOrEmpty(syntaxTree.FilePath) || !syntaxTree.FilePath.EndsWith(".cs"))
                continue;

            analyzedFiles.Add(syntaxTree.FilePath);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            // Find all type declarations
            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDeclaration in typeDeclarations)
            {
                totalTypesAnalyzed++;
                var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
                if (symbol == null) continue;

                var layer = IdentifyLayer(symbol, architecture);
                if (!string.IsNullOrEmpty(layer))
                {
                    layerStats[layer] = layerStats.GetValueOrDefault(layer, 0) + 1;
                }

                // Validate type against architectural rules
                var typeViolations = await ValidateTypeAsync(symbol, layer, architecture, syntaxTree.FilePath, cancellationToken);
                var typeWarnings = await AnalyzeTypeWarningsAsync(symbol, layer, architecture, syntaxTree.FilePath, cancellationToken);

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
        var architecture = definition ?? await DetectBestFitArchitecture(projectPath, cancellationToken);

        var nodes = new List<DependencyNode>();
        var edges = new List<DependencyEdge>();
        var layerDependencies = new Dictionary<string, List<string>>();

        var compilation = _workspace.GetCompilation();
        if (compilation == null)
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
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (!syntaxTree.FilePath?.EndsWith(".cs") == true) continue;

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            var typeDeclarations = root.DescendantNodes()
                .OfType<TypeDeclarationSyntax>();

            foreach (var typeDeclaration in typeDeclarations)
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDeclaration);
                if (symbol == null || syntaxTree.FilePath == null) continue;

                var layer = IdentifyLayer(symbol, architecture);
                var node = CreateDependencyNode(symbol, layer, syntaxTree.FilePath);
                nodes.Add(node);

                // Analyze dependencies
                var dependencies = await AnalyzeTypeDependencies(symbol, semanticModel, architecture, syntaxTree.FilePath, cancellationToken);
                edges.AddRange(dependencies);

                // Update layer dependencies
                if (!string.IsNullOrEmpty(layer))
                {
                    foreach (var dep in dependencies)
                    {
                        if (!string.IsNullOrEmpty(dep.ToLayer) && dep.ToLayer != layer)
                        {
                            if (!layerDependencies.ContainsKey(layer))
                                layerDependencies[layer] = new List<string>();

                            if (!layerDependencies[layer].Contains(dep.ToLayer))
                                layerDependencies[layer].Add(dep.ToLayer);
                        }
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
        var architecture = definition ?? await DetectBestFitArchitecture(projectPath, cancellationToken);

        // Run all analyses
        var validationResult = await ValidateArchitectureAsync(projectPath, architecture, cancellationToken);
        var dependencyAnalysis = await AnalyzeDependenciesAsync(projectPath, architecture, cancellationToken);
        var diagram = await GenerateArchitectureDiagramAsync(projectPath, architecture, cancellationToken);
        var circularDependencies = await AnalyzeCircularDependenciesAsync(projectPath, architecture, cancellationToken);

        // Generate recommendations
        var recommendations = await GetRecommendationsAsync(validationResult.Violations, cancellationToken);

        // Create summary
        var summary = CreateReportSummary(validationResult, dependencyAnalysis, recommendations);

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

    public async Task<ArchitectureDefinition> DefineCustomArchitectureAsync(ArchitectureDefinition definition, CancellationToken cancellationToken = default)
    {
        // Validate the definition
        ValidateArchitectureDefinition(definition);

        // Store for future use (could be persisted to file/database)
        var key = $"{definition.Name}_{DateTime.UtcNow:yyyyMMddHHmmss}";

        return definition;
    }

    public async Task<List<MCPsharp.Models.Architecture.CircularDependency>> AnalyzeCircularDependenciesAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var dependencyAnalysis = await AnalyzeDependenciesAsync(projectPath, definition, cancellationToken);
        return dependencyAnalysis.CircularDependencies;
    }

    public async Task<ArchitectureDiagram> GenerateArchitectureDiagramAsync(string projectPath, ArchitectureDefinition? definition = null, CancellationToken cancellationToken = default)
    {
        var architecture = definition ?? await DetectBestFitArchitecture(projectPath, cancellationToken);
        var dependencyAnalysis = await AnalyzeDependenciesAsync(projectPath, architecture, cancellationToken);

        return CreateArchitectureDiagram(dependencyAnalysis, architecture);
    }

    public async Task<List<ArchitectureRecommendation>> GetRecommendationsAsync(List<LayerViolation> violations, CancellationToken cancellationToken = default)
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
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
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

        var symbol = compilation.GetTypeByMetadataName(typeName);
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
            var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
            var filePath = syntax.SyntaxTree.FilePath;

            var typeViolations = await ValidateTypeAsync(symbol, layer, definition, filePath, cancellationToken);
            violations.AddRange(typeViolations.Select(v => v.Description));

            var typeDependencies = await AnalyzeTypeDependencies(symbol, semanticModel, definition, filePath, cancellationToken);
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

    public async Task<Dictionary<string, ArchitectureDefinition>> GetPredefinedArchitecturesAsync(CancellationToken cancellationToken = default)
    {
        return new Dictionary<string, ArchitectureDefinition>(_predefinedArchitectures);
    }

    private void InitializePredefinedArchitectures()
    {
        // Simple default architecture for testing
        _predefinedArchitectures["Default"] = new ArchitectureDefinition
        {
            Name = "Default",
            Description = "Simple layered architecture",
            Type = ArchitectureType.Layered,
            Layers = new List<ArchitecturalLayer>
            {
                new()
                {
                    Name = "UI",
                    Description = "User interface layer",
                    Level = 0,
                    NamespacePatterns = new List<string> { "*.UI", "*.Controllers" },
                    AllowedDependencies = new List<string> { "Service" },
                    ForbiddenDependencies = new List<string> { "Data" },
                    Type = LayerType.Presentation
                },
                new()
                {
                    Name = "Service",
                    Description = "Service layer",
                    Level = 1,
                    NamespacePatterns = new List<string> { "*.Services", "*.Business" },
                    AllowedDependencies = new List<string> { "Data" },
                    ForbiddenDependencies = new List<string> { "UI" },
                    Type = LayerType.Service
                },
                new()
                {
                    Name = "Data",
                    Description = "Data access layer",
                    Level = 2,
                    NamespacePatterns = new List<string> { "*.Data", "*.Repositories" },
                    AllowedDependencies = new List<string>(),
                    ForbiddenDependencies = new List<string> { "UI", "Service" },
                    Type = LayerType.Repository
                }
            },
            DependencyRules = new List<DependencyRule>(),
            NamingPatterns = new List<NamingPattern>(),
            ForbiddenPatterns = new List<ForbiddenPattern>()
        };
    }

    private async Task<ArchitectureDefinition> DetectBestFitArchitecture(string projectPath, CancellationToken cancellationToken)
    {
        // Simple heuristic-based detection based on namespace patterns
        var compilation = _workspace.GetCompilation();
        if (compilation == null) return _predefinedArchitectures["Clean Architecture"];

        var namespaces = new HashSet<string>();
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var namespaceDeclarations = root.DescendantNodes()
                .OfType<NamespaceDeclarationSyntax>();

            foreach (var ns in namespaceDeclarations)
            {
                namespaces.Add(ns.Name.ToString());
            }
        }

        // Score each predefined architecture
        var scores = new Dictionary<string, int>();
        foreach (var arch in _predefinedArchitectures.Values)
        {
            var score = 0;
            foreach (var layer in arch.Layers)
            {
                foreach (var pattern in layer.NamespacePatterns)
                {
                    if (namespaces.Any(ns => MatchesPattern(ns, pattern)))
                    {
                        score++;
                    }
                }
            }
            scores[arch.Name] = score;
        }

        var bestFit = scores.OrderByDescending(kvp => kvp.Value).First().Key;
        return _predefinedArchitectures[bestFit];
    }

    private bool MatchesPattern(string input, string pattern)
    {
        // Simple glob pattern matching
        var regexPattern = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return Regex.IsMatch(input, $"^{regexPattern}$", RegexOptions.IgnoreCase);
    }

    private string IdentifyLayer(INamedTypeSymbol symbol, ArchitectureDefinition architecture)
    {
        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";
        var className = symbol.Name;

        foreach (var layer in architecture.Layers.OrderBy(l => l.Level))
        {
            // Check namespace patterns
            foreach (var pattern in layer.NamespacePatterns)
            {
                if (MatchesPattern(namespaceName, pattern))
                {
                    return layer.Name;
                }
            }

            // Check naming patterns
            foreach (var namingPattern in architecture.NamingPatterns.Where(np => np.LayerName == layer.Name))
            {
                if (MatchesPattern(className, namingPattern.Pattern))
                {
                    return layer.Name;
                }
            }
        }

        return "Unknown";
    }

    private async Task<List<LayerViolation>> ValidateTypeAsync(INamedTypeSymbol symbol, string layer, ArchitectureDefinition architecture, string filePath, CancellationToken cancellationToken)
    {
        var violations = new List<LayerViolation>();

        if (string.IsNullOrEmpty(layer) || layer == "Unknown")
        {
            violations.Add(new LayerViolation
            {
                ViolationType = "Unknown Layer",
                Description = $"Type {symbol.Name} could not be assigned to any architectural layer",
                SourceLayer = "Unknown",
                TargetLayer = "Unknown",
                SourceType = symbol.Name,
                TargetType = "",
                FilePath = filePath,
                LineNumber = 0,
                ColumnNumber = 0,
                Severity = ViolationSeverity.Minor,
                CodeSnippet = "",
                Recommendations = new List<ArchitectureRecommendation>(),
                RuleViolated = "Layer Assignment"
            });
            return violations;
        }

        var sourceLayer = architecture.Layers.FirstOrDefault(l => l.Name == layer);
        if (sourceLayer == null) return violations;

        // Get all dependencies of this type
        var compilation = _workspace.GetCompilation();
        if (compilation == null) return violations;

        var semanticModel = compilation.GetSemanticModel(symbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree ?? compilation.SyntaxTrees.First());

        // Analyze dependencies for violations
        var dependencies = await AnalyzeTypeDependencies(symbol, semanticModel, architecture, filePath, cancellationToken);

        foreach (var dependency in dependencies)
        {
            if (!dependency.IsAllowed)
            {
                violations.Add(new LayerViolation
                {
                    ViolationType = "Forbidden Dependency",
                    Description = $"{sourceLayer.Name} layer cannot depend on {dependency.ToLayer} layer",
                    SourceLayer = sourceLayer.Name,
                    TargetLayer = dependency.ToLayer,
                    SourceType = symbol.Name,
                    TargetType = dependency.To,
                    FilePath = dependency.FilePath,
                    LineNumber = dependency.LineNumber,
                    ColumnNumber = 0,
                    Severity = DetermineSeverity(sourceLayer, dependency.ToLayer, architecture),
                    CodeSnippet = $"// {dependency.From} -> {dependency.To}",
                    Recommendations = GenerateRecommendationsForDependency(sourceLayer.Name, dependency.ToLayer),
                    RuleViolated = "Layer Dependency Rule"
                });
            }
        }

        return violations;
    }

    private async Task<List<ArchitectureWarning>> AnalyzeTypeWarningsAsync(INamedTypeSymbol symbol, string layer, ArchitectureDefinition architecture, string filePath, CancellationToken cancellationToken)
    {
        var warnings = new List<ArchitectureWarning>();

        // Check for common architectural warnings
        if (symbol.DeclaredAccessibility == Accessibility.Internal && layer == "Domain")
        {
            warnings.Add(new ArchitectureWarning
            {
                WarningType = "Accessibility Issue",
                Description = $"Domain entity {symbol.Name} should typically be public",
                FilePath = filePath,
                LineNumber = symbol.Locations.FirstOrDefault()?.GetLineSpan().StartLinePosition.Line ?? 0,
                Suggestion = "Consider making domain entities public for better testability"
            });
        }

        return warnings;
    }

    private async Task<List<DependencyEdge>> AnalyzeTypeDependencies(INamedTypeSymbol symbol, SemanticModel semanticModel, ArchitectureDefinition architecture, string filePath, CancellationToken cancellationToken)
    {
        var dependencies = new List<DependencyEdge>();
        var sourceLayer = IdentifyLayer(symbol, architecture);

        // Get all members and analyze their dependencies
        foreach (var member in symbol.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                // Return type dependency
                if (method.ReturnType is INamedTypeSymbol returnType)
                {
                    var targetLayer = IdentifyLayer(returnType, architecture);
                    dependencies.Add(CreateDependencyEdge(symbol.Name, returnType.Name, sourceLayer, targetLayer, DependencyType.Return, filePath, method.Locations.FirstOrDefault()));
                }

                // Parameter dependencies
                foreach (var param in method.Parameters)
                {
                    if (param.Type is INamedTypeSymbol paramType)
                    {
                        var targetLayer = IdentifyLayer(paramType, architecture);
                        dependencies.Add(CreateDependencyEdge(symbol.Name, paramType.Name, sourceLayer, targetLayer, DependencyType.Parameter, filePath, param.Locations.FirstOrDefault()));
                    }
                }
            }
        }

        // Base type dependency
        if (symbol.BaseType != null)
        {
            var targetLayer = IdentifyLayer(symbol.BaseType, architecture);
            dependencies.Add(CreateDependencyEdge(symbol.Name, symbol.BaseType.Name, sourceLayer, targetLayer, DependencyType.Inheritance, filePath, symbol.Locations.FirstOrDefault()));
        }

        // Interface implementations
        foreach (var iface in symbol.Interfaces)
        {
            var targetLayer = IdentifyLayer(iface, architecture);
            dependencies.Add(CreateDependencyEdge(symbol.Name, iface.Name, sourceLayer, targetLayer, DependencyType.Implementation, filePath, symbol.Locations.FirstOrDefault()));
        }

        return dependencies;
    }

    private DependencyEdge CreateDependencyEdge(string from, string to, string fromLayer, string toLayer, DependencyType type, string filePath, Location? location)
    {
        var lineSpan = location?.GetLineSpan();

        return new DependencyEdge
        {
            From = from,
            To = to,
            Type = type,
            FromLayer = fromLayer,
            ToLayer = toLayer,
            IsAllowed = IsDependencyAllowed(fromLayer, toLayer),
            FilePath = filePath,
            LineNumber = lineSpan?.StartLinePosition.Line ?? 0
        };
    }

    private bool IsDependencyAllowed(string fromLayer, string toLayer)
    {
        if (string.IsNullOrEmpty(fromLayer) || string.IsNullOrEmpty(toLayer)) return true;
        if (fromLayer == toLayer) return true;
        if (fromLayer == "Unknown" || toLayer == "Unknown") return true;

        // Basic rule: higher level numbers (inner layers) can depend on lower level numbers (outer layers)
        // But outer layers should not depend on inner layers
        var layerOrder = new Dictionary<string, int>
        {
            ["Presentation"] = 0,
            ["Application"] = 1,
            ["Service"] = 2,
            ["Domain"] = 3,
            ["Core"] = 4,
            ["Repository"] = 5,
            ["Infrastructure"] = 6
        };

        var fromLevel = layerOrder.GetValueOrDefault(fromLayer, -1);
        var toLevel = layerOrder.GetValueOrDefault(toLayer, -1);

        if (fromLevel == -1 || toLevel == -1) return true;

        // Dependency is allowed if going from outer to inner (lower level number to higher)
        // or within the same level
        return fromLevel <= toLevel;
    }

    private ViolationSeverity DetermineSeverity(ArchitecturalLayer sourceLayer, string targetLayer, ArchitectureDefinition architecture)
    {
        if (sourceLayer.IsCore && !targetLayer.Equals("Core", StringComparison.OrdinalIgnoreCase))
        {
            return ViolationSeverity.Critical;
        }

        if (sourceLayer.Type == LayerType.Domain && targetLayer.Equals("Presentation", StringComparison.OrdinalIgnoreCase))
        {
            return ViolationSeverity.Critical;
        }

        if (sourceLayer.Type == LayerType.Infrastructure && targetLayer.Equals("Domain", StringComparison.OrdinalIgnoreCase))
        {
            return ViolationSeverity.Major;
        }

        return ViolationSeverity.Minor;
    }

    private List<ArchitectureRecommendation> GenerateRecommendationsForDependency(string sourceLayer, string targetLayer)
    {
        var recommendations = new List<ArchitectureRecommendation>();

        if (sourceLayer == "Infrastructure" && targetLayer == "Domain")
        {
            recommendations.Add(new ArchitectureRecommendation
            {
                Type = "Dependency Inversion",
                Title = "Apply Dependency Inversion Principle",
                Description = "Infrastructure should depend on abstractions, not on Domain directly",
                Priority = RecommendationPriority.High,
                Steps = new List<string>
                {
                    "Create an interface in the Application layer",
                    "Implement the interface in Infrastructure layer",
                    "Inject the interface into Domain/Application services"
                },
                CodeExample = "// Example interface\npublic interface IRepository<T>\n{\n    Task<T> GetByIdAsync(int id);\n    Task SaveAsync(T entity);\n}",
                Effort = 7,
                Impact = 8
            });
        }

        return recommendations;
    }

    private List<ArchitectureRecommendation> GenerateRecommendationsForViolation(LayerViolation violation)
    {
        var recommendations = new List<ArchitectureRecommendation>();

        switch (violation.ViolationType)
        {
            case "Forbidden Dependency":
                recommendations.AddRange(GenerateRecommendationsForDependency(violation.SourceLayer, violation.TargetLayer));
                break;
            case "Unknown Layer":
                recommendations.Add(new ArchitectureRecommendation
                {
                    Type = "Layer Assignment",
                    Title = "Assign Type to Architectural Layer",
                    Description = "Move type to appropriate layer or update architecture definition",
                    Priority = RecommendationPriority.Medium,
                    Steps = new List<string>
                    {
                        "Identify the correct layer for this type",
                        "Move the type to the appropriate namespace",
                        "Or update the architecture definition with new patterns"
                    },
                    CodeExample = "// Move class to appropriate namespace\nnamespace MyProject.Services\n{\n    public class UserService\n    {\n        // Implementation\n    }\n}",
                    Effort = 3,
                    Impact = 5
                });
                break;
        }

        return recommendations;
    }

    private DependencyNode CreateDependencyNode(INamedTypeSymbol symbol, string layer, string filePath)
    {
        var nodeType = symbol.TypeKind switch
        {
            TypeKind.Class => NodeType.Class,
            TypeKind.Interface => NodeType.Interface,
            TypeKind.Enum => NodeType.Enum,
            TypeKind.Struct => NodeType.Struct,
            _ => NodeType.Unknown
        };

        return new DependencyNode
        {
            Name = symbol.Name,
            Layer = layer,
            Type = nodeType,
            FilePath = filePath,
            OutgoingDependencies = new List<string>(),
            IncomingDependencies = new List<string>(),
            Complexity = CalculateComplexity(symbol),
            Metadata = new Dictionary<string, object>
            {
                ["Namespace"] = symbol.ContainingNamespace?.ToDisplayString() ?? "",
                ["Accessibility"] = symbol.DeclaredAccessibility.ToString(),
                ["IsAbstract"] = symbol.IsAbstract,
                ["IsStatic"] = symbol.IsStatic
            }
        };
    }

    private int CalculateComplexity(INamedTypeSymbol symbol)
    {
        // Simple complexity calculation based on members
        var memberCount = symbol.GetMembers().Count();
        var methodCount = symbol.GetMembers().OfType<IMethodSymbol>().Count();
        var propertyCount = symbol.GetMembers().OfType<IPropertySymbol>().Count();

        return memberCount + (methodCount * 2) + propertyCount;
    }

    private List<MCPsharp.Models.Architecture.CircularDependency> DetectCircularDependencies(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var circularDependencies = new List<MCPsharp.Models.Architecture.CircularDependency>();
        var graph = BuildDependencyGraph(nodes, edges);
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in nodes)
        {
            if (!visited.Contains(node.Name))
            {
                var path = new List<string>();
                if (HasCycle(node.Name, graph, visited, recursionStack, path))
                {
                    var cycle = ExtractCycle(path, graph);
                    if (cycle.Count > 1)
                    {
                        circularDependencies.Add(new MCPsharp.Models.Architecture.CircularDependency
                        {
                            Cycle = cycle,
                            LayersInvolved = cycle.Select(n => nodes.FirstOrDefault(node => node.Name == n)?.Layer ?? "Unknown").Distinct().ToList(),
                            CycleLength = cycle.Count,
                            Severity = cycle.Count > 5 ? ViolationSeverity.Critical : ViolationSeverity.Major,
                            Description = $"Circular dependency detected: {string.Join(" -> ", cycle)} -> {cycle.First()}",
                            Edges = edges.Where(e => cycle.Contains(e.From) && cycle.Contains(e.To)).ToList()
                        });
                    }
                }
            }
        }

        return circularDependencies;
    }

    private Dictionary<string, List<string>> BuildDependencyGraph(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var node in nodes)
        {
            graph[node.Name] = new List<string>();
        }

        foreach (var edge in edges)
        {
            if (graph.ContainsKey(edge.From))
            {
                graph[edge.From].Add(edge.To);
            }
        }

        return graph;
    }

    private bool HasCycle(string node, Dictionary<string, List<string>> graph, HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
    {
        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        foreach (var neighbor in graph.GetValueOrDefault(node, new List<string>()))
        {
            if (!visited.Contains(neighbor))
            {
                if (HasCycle(neighbor, graph, visited, recursionStack, path))
                    return true;
            }
            else if (recursionStack.Contains(neighbor))
            {
                return true;
            }
        }

        recursionStack.Remove(node);
        path.RemoveAt(path.Count - 1);
        return false;
    }

    private List<string> ExtractCycle(List<string> path, Dictionary<string, List<string>> graph)
    {
        if (path.Count < 2) return path;

        // Find the start of the cycle
        var lastNode = path.Last();
        var startIndex = path.IndexOf(lastNode);

        if (startIndex >= 0)
        {
            return path.Skip(startIndex).ToList();
        }

        return path;
    }

    private Dictionary<string, int> CalculateDependencyMetrics(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        return new Dictionary<string, int>
        {
            ["TotalNodes"] = nodes.Count,
            ["TotalEdges"] = edges.Count,
            ["ViolatingEdges"] = edges.Count(e => !e.IsAllowed),
            ["MaxDependencies"] = nodes.Max(n => n.OutgoingDependencies.Count),
            ["AverageDependencies"] = nodes.Count > 0 ? (int)nodes.Average(n => n.OutgoingDependencies.Count) : 0
        };
    }

    private ArchitectureDiagram CreateArchitectureDiagram(DependencyAnalysisResult analysis, ArchitectureDefinition architecture)
    {
        var nodes = new List<DiagramNode>();
        var edges = new List<DiagramEdge>();
        var layers = new List<DiagramLayer>();

        // Create layer representations
        var layerY = 50.0;
        foreach (var layer in architecture.Layers.OrderByDescending(l => l.Level))
        {
            var diagramLayer = new DiagramLayer
            {
                Name = layer.Name,
                X = 50,
                Y = layerY,
                Width = 800,
                Height = 150,
                Color = GetLayerColor(layer.Type),
                Level = layer.Level,
                NodeIds = new List<string>()
            };
            layers.Add(diagramLayer);
            layerY += 170;
        }

        // Create nodes positioned within their layers
        var layerPositions = new Dictionary<string, (double x, double y)>();
        foreach (var layer in layers)
        {
            layerPositions[layer.Name] = (layer.X + 50, layer.Y + 50);
        }

        var nodeIndex = 0;
        foreach (var node in analysis.Nodes)
        {
            if (layerPositions.TryGetValue(node.Layer, out var position))
            {
                var diagramNode = new DiagramNode
                {
                    Id = node.Name,
                    Name = node.Name,
                    Layer = node.Layer,
                    X = position.x + (nodeIndex % 5) * 140,
                    Y = position.y + (nodeIndex / 5) * 40,
                    Width = 120,
                    Height = 30,
                    Color = GetNodeTypeColor(node.Type),
                    Type = node.Type,
                    Properties = node.Metadata
                };
                nodes.Add(diagramNode);

                // Add to layer
                var layerNode = layers.FirstOrDefault(l => l.Name == node.Layer);
                layerNode?.NodeIds.Add(node.Name);

                nodeIndex++;
            }
        }

        // Create edges
        foreach (var edge in analysis.Edges)
        {
            var fromNode = nodes.FirstOrDefault(n => n.Name == edge.From);
            var toNode = nodes.FirstOrDefault(n => n.Name == edge.To);

            if (fromNode != null && toNode != null)
            {
                edges.Add(new DiagramEdge
                {
                    From = edge.From,
                    To = edge.To,
                    Type = edge.Type == DependencyType.Inheritance ? EdgeType.Solid : EdgeType.Dashed,
                    Color = edge.IsAllowed ? "#666" : "#ff4444",
                    IsViolating = !edge.IsAllowed,
                    ControlPoints = new List<string>()
                });
            }
        }

        return new ArchitectureDiagram
        {
            Nodes = nodes,
            Edges = edges,
            Layers = layers,
            Layout = new DiagramLayout
            {
                Type = LayoutType.Hierarchical,
                Width = 900,
                Height = 800,
                Properties = new Dictionary<string, object>()
            },
            ColorScheme = new Dictionary<string, string>
            {
                ["presentation"] = "#ffeb3b",
                ["application"] = "#4caf50",
                ["domain"] = "#2196f3",
                ["infrastructure"] = "#9c27b0"
            }
        };
    }

    private string GetLayerColor(LayerType layerType)
    {
        return layerType switch
        {
            LayerType.Presentation => "#ffeb3b",
            LayerType.Application => "#4caf50",
            LayerType.Domain => "#2196f3",
            LayerType.Infrastructure => "#9c27b0",
            LayerType.Service => "#ff9800",
            LayerType.Repository => "#795548",
            LayerType.Core => "#f44336",
            _ => "#9e9e9e"
        };
    }

    private string GetNodeTypeColor(NodeType nodeType)
    {
        return nodeType switch
        {
            NodeType.Class => "#ffffff",
            NodeType.Interface => "#e3f2fd",
            NodeType.Enum => "#fff3e0",
            NodeType.Struct => "#f3e5f5",
            _ => "#fafafa"
        };
    }

    private ReportSummary CreateReportSummary(ArchitectureValidationResult validation, DependencyAnalysisResult dependency, List<ArchitectureRecommendation> recommendations)
    {
        var violationsBySeverity = validation.Violations.GroupBy(v => v.Severity).ToDictionary(g => g.Key, g => g.Count());
        var violationsByLayer = validation.Violations.GroupBy(v => v.SourceLayer).ToDictionary(g => g.Key, g => g.Count());
        var violationsByType = validation.Violations.GroupBy(v => v.ViolationType).ToDictionary(g => g.Key, g => g.Count());

        var topIssues = validation.Violations
            .Where(v => v.Severity == ViolationSeverity.Critical || v.Severity == ViolationSeverity.Major)
            .Take(5)
            .Select(v => v.Description)
            .ToList();

        return new ReportSummary
        {
            TotalViolations = validation.Violations.Count,
            CriticalViolations = violationsBySeverity.GetValueOrDefault(ViolationSeverity.Critical, 0),
            MajorViolations = violationsBySeverity.GetValueOrDefault(ViolationSeverity.Major, 0),
            MinorViolations = violationsBySeverity.GetValueOrDefault(ViolationSeverity.Minor, 0),
            TotalWarnings = validation.Warnings.Count,
            OverallCompliance = validation.CompliancePercentage,
            TopIssues = topIssues,
            ViolationsByLayer = violationsByLayer,
            ViolationsByType = violationsByType
        };
    }

    private void ValidateArchitectureDefinition(ArchitectureDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("Architecture definition must have a name");

        if (definition.Layers == null || !definition.Layers.Any())
            throw new ArgumentException("Architecture definition must have at least one layer");

        // Check for duplicate layer names
        var duplicateLayers = definition.Layers.GroupBy(l => l.Name).Where(g => g.Count() > 1).Select(g => g.Key);
        if (duplicateLayers.Any())
            throw new ArgumentException($"Duplicate layer names found: {string.Join(", ", duplicateLayers)}");

        // Check for valid dependency rules
        foreach (var rule in definition.DependencyRules)
        {
            if (!definition.Layers.Any(l => l.Name == rule.FromLayer))
                throw new ArgumentException($"Dependency rule references unknown layer: {rule.FromLayer}");

            if (!definition.Layers.Any(l => l.Name == rule.ToLayer))
                throw new ArgumentException($"Dependency rule references unknown layer: {rule.ToLayer}");
        }
    }
}
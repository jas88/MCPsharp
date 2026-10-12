using System.Text.RegularExpressions;
using MCPsharp.Models.Architecture;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Layer identification, per-type rule validation and violation recommendations
/// </summary>
public partial class ArchitectureValidatorService
{
    private static bool MatchesPattern(string input, string pattern)
    {
        // Simple glob pattern matching
        var regexPattern = Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        return Regex.IsMatch(input, $"^{regexPattern}$", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }

    private static string IdentifyLayer(INamedTypeSymbol symbol, ArchitectureDefinition architecture)
    {
        var namespaceName = symbol.ContainingNamespace?.ToDisplayString() ?? "";
        var className = symbol.Name;

        // Check namespace patterns, then naming patterns
        var match = architecture.Layers
            .OrderBy(l => l.Level)
            .FirstOrDefault(layer => layer.NamespacePatterns.Any(pattern => MatchesPattern(namespaceName, pattern))
                || architecture.NamingPatterns.Any(np => np.LayerName == layer.Name && MatchesPattern(className, np.Pattern)));

        return match?.Name ?? "Unknown";
    }

    private List<LayerViolation> ValidateType(INamedTypeSymbol symbol, string layer, ArchitectureDefinition architecture, string filePath)
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
        if (sourceLayer == null)
            return violations;

        // Get all dependencies of this type
        if (!_workspace.IsInitialized)
            return violations;

        // Analyze dependencies for violations
        var dependencies = AnalyzeTypeDependencies(symbol, architecture, filePath);

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
                    Severity = DetermineSeverity(sourceLayer, dependency.ToLayer),
                    CodeSnippet = $"// {dependency.From} -> {dependency.To}",
                    Recommendations = GenerateRecommendationsForDependency(sourceLayer.Name, dependency.ToLayer),
                    RuleViolated = "Layer Dependency Rule"
                });
            }
        }

        return violations;
    }

    private static List<ArchitectureWarning> AnalyzeTypeWarnings(INamedTypeSymbol symbol, string layer, string filePath)
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

    private static List<DependencyEdge> AnalyzeTypeDependencies(INamedTypeSymbol symbol, ArchitectureDefinition architecture, string filePath)
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

    private static DependencyEdge CreateDependencyEdge(string from, string to, string fromLayer, string toLayer, DependencyType type, string filePath, Location? location)
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

    private static bool IsDependencyAllowed(string fromLayer, string toLayer)
    {
        if (string.IsNullOrEmpty(fromLayer) || string.IsNullOrEmpty(toLayer))
            return true;
        if (fromLayer == toLayer)
            return true;
        if (fromLayer == "Unknown" || toLayer == "Unknown")
            return true;

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

        if (fromLevel == -1 || toLevel == -1)
            return true;

        // Dependency is allowed if going from outer to inner (lower level number to higher)
        // or within the same level
        return fromLevel <= toLevel;
    }

    private static ViolationSeverity DetermineSeverity(ArchitecturalLayer sourceLayer, string targetLayer)
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

    private static List<ArchitectureRecommendation> GenerateRecommendationsForDependency(string sourceLayer, string targetLayer)
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

    private static List<ArchitectureRecommendation> GenerateRecommendationsForViolation(LayerViolation violation)
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
}

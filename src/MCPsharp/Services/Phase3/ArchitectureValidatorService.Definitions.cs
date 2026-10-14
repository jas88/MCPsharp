using MCPsharp.Models.Architecture;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Predefined architecture definitions, best-fit detection and definition validation
/// </summary>
public partial class ArchitectureValidatorService
{
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

    private async Task<ArchitectureDefinition> DetectBestFitArchitecture(CancellationToken cancellationToken)
    {
        // Simple heuristic-based detection based on namespace patterns
        if (!_workspace.IsInitialized)
            return _predefinedArchitectures["Clean Architecture"];

        var namespaces = new HashSet<string>();
        foreach (var syntaxTree in _workspace.GetSourceTrees())
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
            scores[arch.Name] = arch.Layers
                .Sum(layer => layer.NamespacePatterns.Count(pattern => namespaces.Any(ns => MatchesPattern(ns, pattern))));
        }

        var bestFit = scores.OrderByDescending(kvp => kvp.Value).First().Key;
        return _predefinedArchitectures[bestFit];
    }

    private static void ValidateArchitectureDefinition(ArchitectureDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("Architecture definition must have a name");

        if (definition.Layers == null || definition.Layers.Count == 0)
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

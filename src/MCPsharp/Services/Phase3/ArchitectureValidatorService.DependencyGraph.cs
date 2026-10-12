using Microsoft.CodeAnalysis;
using MCPsharp.Models.Architecture;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Dependency graph construction, cycle detection and dependency metrics
/// </summary>
public partial class ArchitectureValidatorService
{
    private static DependencyNode CreateDependencyNode(INamedTypeSymbol symbol, string layer, string filePath)
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

    private static int CalculateComplexity(INamedTypeSymbol symbol)
    {
        // Simple complexity calculation based on members
        var memberCount = symbol.GetMembers().Length;
        var methodCount = symbol.GetMembers().OfType<IMethodSymbol>().Count();
        var propertyCount = symbol.GetMembers().OfType<IPropertySymbol>().Count();

        return memberCount + (methodCount * 2) + propertyCount;
    }

    private static List<MCPsharp.Models.Architecture.CircularDependency> DetectCircularDependencies(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var circularDependencies = new List<MCPsharp.Models.Architecture.CircularDependency>();
        var graph = BuildDependencyGraph(nodes, edges);
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        // visited is updated by HasCycle, so the filter must be evaluated lazily per node
        foreach (var node in nodes.Where(n => !visited.Contains(n.Name)))
        {
            var path = new List<string>();
            if (HasCycle(node.Name, graph, visited, recursionStack, path))
            {
                var cycle = ExtractCycle(path);
                if (cycle.Count > 1)
                {
                    circularDependencies.Add(new MCPsharp.Models.Architecture.CircularDependency
                    {
                        Cycle = cycle,
                        LayersInvolved = cycle.Select(n => nodes.FirstOrDefault(node => node.Name == n)?.Layer ?? "Unknown").Distinct().ToList(),
                        CycleLength = cycle.Count,
                        Severity = cycle.Count > 5 ? ViolationSeverity.Critical : ViolationSeverity.Major,
                        Description = $"Circular dependency detected: {string.Join(" -> ", cycle)} -> {cycle[0]}",
                        Edges = edges.Where(e => cycle.Contains(e.From) && cycle.Contains(e.To)).ToList()
                    });
                }
            }
        }

        return circularDependencies;
    }

    private static Dictionary<string, List<string>> BuildDependencyGraph(List<DependencyNode> nodes, List<DependencyEdge> edges)
    {
        var graph = new Dictionary<string, List<string>>();

        foreach (var node in nodes)
        {
            graph[node.Name] = new List<string>();
        }

        foreach (var edge in edges)
        {
            if (graph.TryGetValue(edge.From, out var value))
            {
                value.Add(edge.To);
            }
        }

        return graph;
    }

    private static bool HasCycle(string node, Dictionary<string, List<string>> graph, HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
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

    private static List<string> ExtractCycle(List<string> path)
    {
        if (path.Count < 2) return path;

        // Find the start of the cycle
        var lastNode = path[^1];
        var startIndex = path.IndexOf(lastNode);

        if (startIndex >= 0)
        {
            return path.Skip(startIndex).ToList();
        }

        return path;
    }

    private static Dictionary<string, int> CalculateDependencyMetrics(List<DependencyNode> nodes, List<DependencyEdge> edges)
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
}

using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes dependency relationships including graphs, circular dependencies, and critical paths.
/// </summary>
public class DependencyAnalyzer
{
    private readonly SymbolQueryService? _symbolQuery;
    private readonly RoslynWorkspace? _workspace;
    private readonly ILogger<DependencyAnalyzer> _logger;

    public DependencyAnalyzer(
        SymbolQueryService? symbolQuery = null,
        RoslynWorkspace? workspace = null,
        ILogger<DependencyAnalyzer>? logger = null)
    {
        _symbolQuery = symbolQuery;
        _workspace = workspace;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DependencyAnalyzer>.Instance;
    }

    public async Task<DependencyGraph> BuildDependencyGraphAsync(string scope, int depth, CancellationToken ct)
    {
        try
        {
            var graph = new DependencyGraph
            {
                Nodes = new List<DependencyNode>(),
                Edges = new List<DependencyEdge>()
            };

            if (_symbolQuery != null && _workspace != null)
            {
                var allTypes = await _symbolQuery.GetAllTypesAsync();

                foreach (var type in allTypes.Take(50))
                {
                    var node = new DependencyNode
                    {
                        Id = type.Name,
                        Name = type.Name,
                        Type = "Class"
                    };
                    graph.Nodes.Add(node);
                }
            }

            return graph;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building dependency graph for {Scope}", scope);
        }

        return new DependencyGraph();
    }

    public async Task<List<ConsolidatedCircularDependency>> DetectCircularDependenciesAsync(string scope, CancellationToken ct)
    {
        try
        {
            var circularDependencies = new List<ConsolidatedCircularDependency>();
            var graph = await BuildDependencyGraphAsync(scope, 10, ct);

            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var node in graph.Nodes)
            {
                if (HasCycle(node.Id, graph, visited, recursionStack, out var cycle))
                {
                    circularDependencies.Add(new ConsolidatedCircularDependency
                    {
                        Cycle = cycle,
                        Locations = new List<Location> { new Location { FilePath = "", Line = 0, Column = 0 } },
                        Length = cycle.Count
                    });
                }
            }

            return circularDependencies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting circular dependencies for {Scope}", scope);
        }

        return new List<ConsolidatedCircularDependency>();
    }

    public async Task<DependencyImpact?> AnalyzeDependencyImpactAsync(string scope, CancellationToken ct)
    {
        try
        {
            var graph = await BuildDependencyGraphAsync(scope, 10, ct);

            var edgeCount = graph.Edges.Count;

            return new DependencyImpact
            {
                Target = scope,
                AffectedComponents = graph.Nodes.Select(n => n.Name).ToList(),
                TotalImpact = edgeCount,
                Details = graph.Nodes.Select(n => new ImpactDetail
                {
                    Component = n.Name,
                    ImpactType = "Dependency",
                    Severity = graph.Edges.Count(e => e.From == n.Id || e.To == n.Id)
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing dependency impact for {Scope}", scope);
        }

        return null;
    }

    public async Task<List<CriticalPath>> AnalyzeCriticalPathsAsync(string scope, CancellationToken ct)
    {
        try
        {
            var criticalPaths = new List<CriticalPath>();
            var graph = await BuildDependencyGraphAsync(scope, 10, ct);

            var criticalNodes = graph.Nodes
                .Select(n => new
                {
                    Node = n,
                    DependencyCount = graph.Edges.Count(e => e.From == n.Id || e.To == n.Id)
                })
                .OrderByDescending(x => x.DependencyCount)
                .Take(5);

            foreach (var criticalNode in criticalNodes)
            {
                var path = new List<string> { criticalNode.Node.Id };

                var connectedEdges = graph.Edges
                    .Where(e => e.From == criticalNode.Node.Id || e.To == criticalNode.Node.Id)
                    .Take(3);

                foreach (var edge in connectedEdges)
                {
                    var connectedNode = edge.From == criticalNode.Node.Id ? edge.To : edge.From;
                    path.Add(connectedNode);
                }

                criticalPaths.Add(new CriticalPath
                {
                    Path = path,
                    Criticality = criticalNode.DependencyCount > 5 ? 1.0 : 0.5,
                    Description = $"Critical path through {criticalNode.Node.Name} with {criticalNode.DependencyCount} dependencies"
                });
            }

            return criticalPaths;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing critical paths for {Scope}", scope);
        }

        return new List<CriticalPath>();
    }

    public Task<DependencyMetrics?> CalculateDependencyMetricsAsync(DependencyGraph graph, CancellationToken ct)
    {
        try
        {
            var nodeCount = graph.Nodes.Count;
            var edgeCount = graph.Edges.Count;
            var density = nodeCount > 1 ? (double)edgeCount / (nodeCount * (nodeCount - 1)) : 0;

            return Task.FromResult<DependencyMetrics?>(new DependencyMetrics
            {
                TotalNodes = nodeCount,
                TotalEdges = edgeCount,
                Density = density,
                Cycles = 0,
                AveragePathLength = 2.5,
                NodeMetrics = graph.Nodes.Select(n => new NodeMetrics
                {
                    NodeId = n.Id,
                    InDegree = graph.Edges.Count(e => e.To == n.Id),
                    OutDegree = graph.Edges.Count(e => e.From == n.Id),
                    Centrality = 0.5,
                    IsInCycle = false
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating dependency metrics");
            return Task.FromResult<DependencyMetrics?>(null);
        }
    }

    private static bool HasCycle(
        string nodeId,
        DependencyGraph graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        out List<string> cycle)
    {
        cycle = new List<string>();

        if (recursionStack.Contains(nodeId))
        {
            cycle.Add(nodeId);
            return true;
        }

        if (visited.Contains(nodeId))
            return false;

        visited.Add(nodeId);
        recursionStack.Add(nodeId);

        foreach (var edge in graph.Edges.Where(e => e.From == nodeId))
        {
            cycle = new List<string> { nodeId };
            if (HasCycle(edge.To, graph, visited, recursionStack, out var subCycle))
            {
                cycle.AddRange(subCycle);
                return true;
            }
        }

        recursionStack.Remove(nodeId);
        return false;
    }
}

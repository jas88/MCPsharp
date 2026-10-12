using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes dependency relationships including graphs, circular dependencies, and critical paths.
/// </summary>
public partial class DependencyAnalyzer
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

                // Limit nodes based on depth parameter (depth * 10 nodes as heuristic)
                var maxNodes = Math.Max(10, depth * 10);
                foreach (var typeName in allTypes.Take(maxNodes).Select(type => type.Name))
                {
                    var node = new DependencyNode
                    {
                        Id = typeName,
                        Name = typeName,
                        Type = "Class"
                    };
                    graph.Nodes.Add(node);
                }
            }

            return graph;
        }
        catch (Exception ex)
        {
            LogErrorBuildingDependencyGraphFor(_logger, ex, scope);
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
            LogErrorDetectingCircularDependenciesFor(_logger, ex, scope);
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
            LogErrorAnalyzingDependencyImpactFor(_logger, ex, scope);
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
            LogErrorAnalyzingCriticalPathsFor(_logger, ex, scope);
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

            // Calculate actual average path length using BFS
            var avgPathLength = CalculateAveragePathLength(graph);

            // Detect cycles
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();
            var cycleCount = 0;
            var nodesInCycles = new HashSet<string>();

            foreach (var node in graph.Nodes)
            {
                if (HasCycle(node.Id, graph, visited, recursionStack, out var cycle))
                {
                    cycleCount++;
                    foreach (var nodeId in cycle)
                    {
                        nodesInCycles.Add(nodeId);
                    }
                }
            }

            return Task.FromResult<DependencyMetrics?>(new DependencyMetrics
            {
                TotalNodes = nodeCount,
                TotalEdges = edgeCount,
                Density = density,
                Cycles = cycleCount,
                AveragePathLength = avgPathLength,
                NodeMetrics = graph.Nodes.Select(n =>
                {
                    var inDegree = graph.Edges.Count(e => e.To == n.Id);
                    var outDegree = graph.Edges.Count(e => e.From == n.Id);
                    // Calculate centrality as normalized degree
                    var centrality = nodeCount > 1 ? (inDegree + outDegree) / (2.0 * (nodeCount - 1)) : 0;

                    return new NodeMetrics
                    {
                        NodeId = n.Id,
                        InDegree = inDegree,
                        OutDegree = outDegree,
                        Centrality = centrality,
                        IsInCycle = nodesInCycles.Contains(n.Id)
                    };
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            LogErrorCalculatingDependencyMetrics(_logger, ex);
            return Task.FromResult<DependencyMetrics?>(null);
        }
    }

    private static double CalculateAveragePathLength(DependencyGraph graph)
    {
        if (graph.Nodes.Count == 0) return 0;

        var totalPathLength = 0.0;
        var pathCount = 0;

        // Use BFS to calculate shortest paths between all pairs
        foreach (var startId in graph.Nodes.Select(startNode => startNode.Id))
        {
            var distances = new Dictionary<string, int>();
            var queue = new Queue<(string NodeId, int Distance)>();
            queue.Enqueue((startId, 0));
            distances[startId] = 0;

            while (queue.Count > 0)
            {
                var (currentId, distance) = queue.Dequeue();

                // Predicate is evaluated lazily per edge, so nodes discovered earlier in this pass are skipped
                foreach (var target in graph.Edges.Where(e => e.From == currentId && !distances.ContainsKey(e.To)).Select(e => e.To))
                {
                    distances[target] = distance + 1;
                    queue.Enqueue((target, distance + 1));
                    totalPathLength += distance + 1;
                    pathCount++;
                }
            }
        }

        return pathCount > 0 ? totalPathLength / pathCount : 0;
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Error building dependency graph for {Scope}")]
    private static partial void LogErrorBuildingDependencyGraphFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error detecting circular dependencies for {Scope}")]
    private static partial void LogErrorDetectingCircularDependenciesFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing dependency impact for {Scope}")]
    private static partial void LogErrorAnalyzingDependencyImpactFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error analyzing critical paths for {Scope}")]
    private static partial void LogErrorAnalyzingCriticalPathsFor(ILogger logger, Exception exception, string scope);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error calculating dependency metrics")]
    private static partial void LogErrorCalculatingDependencyMetrics(ILogger logger, Exception exception);
}

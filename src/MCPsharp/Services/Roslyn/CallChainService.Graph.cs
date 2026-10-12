using MCPsharp.Models.Roslyn;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Call graph algorithms: cycle detection and depth calculation.
/// </summary>
public partial class CallChainService
{
    private static bool IsMethodInScope(ISymbol symbol, string? typeName, string? namespaceName)
    {
        if (string.IsNullOrEmpty(typeName) && string.IsNullOrEmpty(namespaceName))
            return true;

        var containingType = symbol.ContainingType?.ToDisplayString() ?? "";
        var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";

        if (!string.IsNullOrEmpty(typeName) && containingType.Contains(typeName))
            return true;

        if (!string.IsNullOrEmpty(namespaceName) && containingNamespace.Contains(namespaceName))
            return true;

        return false;
    }

    private static List<CircularDependency> FindCircularDependenciesInGraph(Dictionary<string, List<string>> callGraph)
    {
        var circularDependencies = new List<CircularDependency>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        // visited is updated during traversal; the lazy Where re-checks it for each key
        foreach (var method in callGraph.Keys.Where(m => !visited.Contains(m)))
        {
            var cycle = DetectCycleInGraph(method, callGraph, visited, recursionStack, new List<string>());
            if (cycle.Count > 0)
            {
                var dependency = new CircularDependency
                {
                    Methods = cycle.Select(m => CreateMethodSignatureFromString(m)).ToList(),
                    CycleLength = cycle.Count,
                    Steps = cycle.Zip(cycle.Skip(1).Append(cycle[0]), (from, to) => new CallChainStep
                    {
                        FromMethod = CreateMethodSignatureFromString(from),
                        ToMethod = CreateMethodSignatureFromString(to),
                        File = "",
                        Line = 0,
                        Column = 0,
                        CallType = CallType.Direct,
                        Confidence = ConfidenceLevel.High
                    }).ToList(),
                    Confidence = ConfidenceLevel.Medium,
                    FilesInvolved = new List<string>()
                };
                circularDependencies.Add(dependency);
            }
        }

        return circularDependencies;
    }

    private static List<string> DetectCycleInGraph(string method, Dictionary<string, List<string>> callGraph, HashSet<string> visited, HashSet<string> recursionStack, List<string> path)
    {
        visited.Add(method);
        recursionStack.Add(method);
        path.Add(method);

        if (callGraph.TryGetValue(method, out var calledMethods))
        {
            foreach (var calledMethod in calledMethods)
            {
                if (!visited.Contains(calledMethod))
                {
                    var cycle = DetectCycleInGraph(calledMethod, callGraph, visited, recursionStack, new List<string>(path));
                    if (cycle.Count > 0)
                        return cycle;
                }
                else if (recursionStack.Contains(calledMethod))
                {
                    // Found a cycle
                    var cycleStart = path.IndexOf(calledMethod);
                    return path.Skip(cycleStart).ToList();
                }
            }
        }

        recursionStack.Remove(method);
        return new List<string>();
    }

    private static int CalculateMaxCallDepth(Dictionary<string, List<string>> callGraph, List<string> entryPoints)
    {
        var maxDepth = 0;
        foreach (var entryPoint in entryPoints)
        {
            var depth = CalculateMaxDepthFromNode(entryPoint, callGraph, new HashSet<string>());
            maxDepth = Math.Max(maxDepth, depth);
        }
        return maxDepth;
    }

    private static int CalculateMaxDepthFromNode(string node, Dictionary<string, List<string>> callGraph, HashSet<string> visited)
    {
        if (visited.Contains(node) || !callGraph.TryGetValue(node, out var children))
            return 0;

        visited.Add(node);
        var maxChildDepth = 0;

        foreach (var child in children)
        {
            var childDepth = CalculateMaxDepthFromNode(child, callGraph, new HashSet<string>(visited));
            maxChildDepth = Math.Max(maxChildDepth, childDepth);
        }

        return maxChildDepth + 1;
    }

    private static double CalculateAverageCallDepth(Dictionary<string, List<string>> callGraph, List<string> entryPoints)
    {
        var depths = new List<int>();
        foreach (var entryPoint in entryPoints)
        {
            var depth = CalculateMaxDepthFromNode(entryPoint, callGraph, new HashSet<string>());
            depths.Add(depth);
        }

        return depths.Count > 0 ? depths.Average() : 0;
    }
}

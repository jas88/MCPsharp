using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services;

/// <summary>
/// Call analysis tool execution methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    private async Task<ToolCallResult> ExecuteFindCallers(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var includeIndirect = true;
        if (arguments.RootElement.TryGetProperty("includeIndirect", out var indirectElement))
        {
            includeIndirect = indirectElement.GetBoolean();
        }

        var result = await _advancedReferenceFinder.FindCallersAsync(methodName, containingType, includeIndirect);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"No callers found for method '{methodName}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindCallChains(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var direction = "backward";
        if (arguments.RootElement.TryGetProperty("direction", out var directionElement))
        {
            direction = directionElement.GetString() ?? "backward";
        }

        var maxDepth = 10;
        if (arguments.RootElement.TryGetProperty("maxDepth", out var depthElement))
        {
            maxDepth = depthElement.GetInt32();
        }

        var callDirection = direction.ToLower() switch
        {
            "forward" => CallDirection.Forward,
            "both" => CallDirection.Both,
            _ => CallDirection.Backward
        };

        var result = await _advancedReferenceFinder.FindCallChainsAsync(methodName, containingType, callDirection, maxDepth);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"No call chains found for method '{methodName}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindTypeUsages(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("typeName", out var typeNameElement) ||
            string.IsNullOrEmpty(typeNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var typeName = typeNameElement.GetString()!;

        var result = await _advancedReferenceFinder.FindTypeUsagesAsync(typeName);
        if (result == null)
        {
            return new ToolCallResult { Success = false, Error = $"No usages found for type '{typeName}'" };
        }

        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCallPatterns(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var result = await _advancedReferenceFinder.AnalyzeCallPatternsAsync(methodName, containingType);
        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeInheritance(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("typeName", out var typeNameElement) ||
            string.IsNullOrEmpty(typeNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var typeName = typeNameElement.GetString()!;

        var result = await _advancedReferenceFinder.AnalyzeInheritanceAsync(typeName);
        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindCircularDependencies(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? namespaceFilter = null;
        if (arguments.RootElement.TryGetProperty("namespaceFilter", out var nsElement))
        {
            namespaceFilter = nsElement.GetString();
        }

        var result = await _advancedReferenceFinder.FindCircularDependenciesAsync(namespaceFilter);
        return new ToolCallResult { Success = true, Result = new { CircularDependencies = result, TotalCount = result.Count } };
    }

    private async Task<ToolCallResult> ExecuteFindUnusedMethods(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? namespaceFilter = null;
        if (arguments.RootElement.TryGetProperty("namespaceFilter", out var nsElement))
        {
            namespaceFilter = nsElement.GetString();
        }

        var result = await _advancedReferenceFinder.FindUnusedMethodsAsync(namespaceFilter);
        return new ToolCallResult { Success = true, Result = new { UnusedMethods = result, TotalCount = result.Count } };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeCallGraph(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        string? typeName = null;
        if (arguments.RootElement.TryGetProperty("typeName", out var typeElement))
        {
            typeName = typeElement.GetString();
        }

        string? namespaceName = null;
        if (arguments.RootElement.TryGetProperty("namespaceName", out var nsElement))
        {
            namespaceName = nsElement.GetString();
        }

        var result = await _advancedReferenceFinder.AnalyzeCallGraphAsync(typeName, namespaceName);
        return new ToolCallResult { Success = true, Result = result };
    }

    private async Task<ToolCallResult> ExecuteFindRecursiveCalls(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("methodName", out var methodNameElement) ||
            string.IsNullOrEmpty(methodNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "MethodName is required" };
        }

        var methodName = methodNameElement.GetString()!;

        string? containingType = null;
        if (arguments.RootElement.TryGetProperty("containingType", out var typeElement))
        {
            containingType = typeElement.GetString();
        }

        var maxDepth = 20;
        if (arguments.RootElement.TryGetProperty("maxDepth", out var depthElement))
        {
            maxDepth = depthElement.GetInt32();
        }

        var result = await _advancedReferenceFinder.FindRecursiveCallChainsAsync(methodName, containingType, maxDepth);
        return new ToolCallResult { Success = true, Result = new { RecursiveCalls = result, TotalCount = result.Count } };
    }

    private async Task<ToolCallResult> ExecuteAnalyzeTypeDependencies(JsonDocument arguments)
    {
        await EnsureWorkspaceInitializedAsync();
        if (_advancedReferenceFinder == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Workspace not initialized. Open a project first."
            };
        }

        if (!arguments.RootElement.TryGetProperty("typeName", out var typeNameElement) ||
            string.IsNullOrEmpty(typeNameElement.GetString()))
        {
            return new ToolCallResult { Success = false, Error = "TypeName is required" };
        }

        var typeName = typeNameElement.GetString()!;

        var result = await _advancedReferenceFinder.AnalyzeTypeDependenciesAsync(typeName);
        return new ToolCallResult { Success = true, Result = result };
    }
}

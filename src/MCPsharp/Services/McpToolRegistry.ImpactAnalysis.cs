using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Models.Phase2;

namespace MCPsharp.Services;

/// <summary>
/// Impact analysis and feature tracing tool execution methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    private async Task<ToolCallResult> ExecuteAnalyzeImpact(JsonDocument arguments)
    {
        if (_impactAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. ImpactAnalyzerService is not available."
            };
        }

        var filePath = arguments.RootElement.GetProperty("filePath").GetString();
        var changeType = arguments.RootElement.GetProperty("changeType").GetString();

        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(changeType))
        {
            return new ToolCallResult { Success = false, Error = "FilePath and changeType are required" };
        }

        string? symbolName = null;
        if (arguments.RootElement.TryGetProperty("symbolName", out var symbolElement))
        {
            symbolName = symbolElement.GetString();
        }

        try
        {
            var change = new CodeChange
            {
                FilePath = filePath,
                ChangeType = changeType,
                SymbolName = symbolName ?? ""  // CodeChange requires SymbolName, use empty string if not provided
            };

            var impact = await _impactAnalyzer.AnalyzeImpactAsync(change);
            return new ToolCallResult { Success = true, Result = impact };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }

    private async Task<ToolCallResult> ExecuteTraceFeature(JsonDocument arguments)
    {
        if (_featureTracer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. FeatureTracerService is not available."
            };
        }

        string? featureName = null;
        string? entryPoint = null;

        if (arguments.RootElement.TryGetProperty("featureName", out var featureElement))
        {
            featureName = featureElement.GetString();
        }

        if (arguments.RootElement.TryGetProperty("entryPoint", out var entryElement))
        {
            entryPoint = entryElement.GetString();
        }

        if (string.IsNullOrEmpty(featureName) && string.IsNullOrEmpty(entryPoint))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Either featureName or entryPoint is required"
            };
        }

        try
        {
            FeatureMap featureMap;
            if (!string.IsNullOrEmpty(featureName))
            {
                featureMap = await _featureTracer.TraceFeatureAsync(featureName);
            }
            else
            {
                featureMap = await _featureTracer.DiscoverFeatureComponentsAsync(entryPoint!);
            }

            return new ToolCallResult { Success = true, Result = featureMap };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = ex.Message };
        }
    }
}

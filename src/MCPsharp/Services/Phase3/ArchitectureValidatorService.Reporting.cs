using MCPsharp.Models.Architecture;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Phase3;

/// <summary>
/// Architecture diagram and report summary generation
/// </summary>
public partial class ArchitectureValidatorService
{
    private static ArchitectureDiagram CreateArchitectureDiagram(DependencyAnalysisResult analysis, ArchitectureDefinition architecture)
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

    private static string GetLayerColor(LayerType layerType)
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

    private static string GetNodeTypeColor(NodeType nodeType)
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

    private static ReportSummary CreateReportSummary(ArchitectureValidationResult validation)
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
}

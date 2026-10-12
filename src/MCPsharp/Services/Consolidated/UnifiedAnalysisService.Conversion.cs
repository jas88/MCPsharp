using MCPsharp.Models.Consolidated;
using MCPsharp.Models.Roslyn;
using MCPsharp.Models.Architecture;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    #region Helper Methods for Type Conversion

    private static Models.Architecture.ArchitectureDefinition ConvertToArchitectureDefinition(MCPsharp.Models.Consolidated.ArchitectureDefinition consolidatedDef)
    {
        return new Models.Architecture.ArchitectureDefinition
        {
            Name = consolidatedDef.Name,
            Description = consolidatedDef.Description ?? string.Empty,
            Type = Models.Architecture.ArchitectureType.Layered,
            Layers = consolidatedDef.Layers.Select(l => new Models.Architecture.ArchitecturalLayer
            {
                Name = l.Name,
                Description = l.Description ?? "",
                Level = l.Order,
                NamespacePatterns = l.NamespacePatterns?.ToList() ?? new List<string>(),
                AllowedDependencies = new List<string>(),
                ForbiddenDependencies = new List<string>()
            }).ToList(),
            DependencyRules = consolidatedDef.DependencyRules.Select(r => new Models.Architecture.DependencyRule
            {
                FromLayer = r.FromLayer,
                ToLayer = r.ToLayer,
                Direction = r.Direction,
                Type = r.Type,
                Description = r.Description,
                IsStrict = r.IsStrict
            }).ToList(),
            NamingPatterns = new List<Models.Architecture.NamingPattern>(),
            ForbiddenPatterns = new List<Models.Architecture.ForbiddenPattern>()
        };
    }

    private static ArchitectureValidation ConvertToValidationResult(ArchitectureValidationResult result)
    {
        return new ArchitectureValidation
        {
            IsValid = result.IsValid,
            Violations = result.Violations.Select(v => new ArchitectureViolation
            {
                From = v.SourceLayer,
                To = v.TargetLayer,
                Rule = v.ViolationType,
                Location = new MCPsharp.Models.Consolidated.Location { FilePath = v.FilePath, Line = v.LineNumber, Column = v.ColumnNumber },
                Description = v.Description
            }).ToList(),
            Warnings = result.Warnings.Select(w => new MCPsharp.Models.Consolidated.ArchitectureWarning
            {
                Type = w.WarningType,
                Message = w.Description,
                Location = new MCPsharp.Models.Consolidated.Location { FilePath = w.FilePath, Line = w.LineNumber, Column = 0 }
            }).ToList()
        };
    }

    private static DependencyGraph ConvertToDependencyGraph(MCPsharp.Models.Architecture.DependencyAnalysisResult sourceAnalysis)
    {
        return new DependencyGraph
        {
            Nodes = sourceAnalysis.Nodes.Select(n => new MCPsharp.Models.Consolidated.DependencyNode
            {
                Id = n.Name,
                Name = n.Name,
                Type = n.Type.ToString(),
                Labels = new List<string>()
            }).ToList(),
            Edges = sourceAnalysis.Edges.Select(e => new MCPsharp.Models.Consolidated.DependencyEdge
            {
                From = e.From,
                To = e.To,
                Type = e.Type.ToString(),
                Weight = 1.0
            }).ToList()
        };
    }

    private static List<TypeMember> ConvertToTypeMembers(ClassStructure structure)
    {
        var members = new List<TypeMember>();

        // Convert properties
        members.AddRange(structure.Properties.Select(p => new TypeMember
        {
            Name = p.Name,
            Kind = "property",
            Accessibility = p.Accessibility,
            IsStatic = false,
            Location = new MCPsharp.Models.Consolidated.Location { FilePath = "", Line = p.Line, Column = 0 }
        }));

        // Convert methods
        members.AddRange(structure.Methods.Select(m => new TypeMember
        {
            Name = m.Name,
            Kind = "method",
            Accessibility = m.Accessibility,
            IsStatic = m.IsStatic,
            Location = new MCPsharp.Models.Consolidated.Location { FilePath = "", Line = m.Line, Column = 0 }
        }));

        // Convert fields
        members.AddRange(structure.Fields.Select(f => new TypeMember
        {
            Name = f.Name,
            Kind = "field",
            Accessibility = f.Accessibility,
            IsStatic = f.IsStatic,
            Location = new MCPsharp.Models.Consolidated.Location { FilePath = "", Line = f.Line, Column = 0 }
        }));

        return members;
    }
    #endregion
}

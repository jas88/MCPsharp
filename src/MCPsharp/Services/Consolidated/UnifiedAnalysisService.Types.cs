using MCPsharp.Models.Consolidated;
using Microsoft.CodeAnalysis;
using ConsolidatedLocation = MCPsharp.Models.Consolidated.Location;

namespace MCPsharp.Services.Consolidated;

public partial class UnifiedAnalysisService
{
    private async Task<TypeStructure?> GetTypeStructureAsync(string typeName, string? context)
    {
        try
        {
            if (_classStructure == null) return null;

            var structure = await _classStructure.GetClassStructureAsync(typeName, context);

            if (structure != null)
            {
                return new TypeStructure
                {
                    Name = structure.Name,
                    Kind = structure.Kind,
                    BaseType = structure.BaseTypes.FirstOrDefault(),
                    Interfaces = structure.Interfaces.ToList(),
                    Members = ConvertToTypeMembers(structure)
                };
            }
        }
        catch (Exception ex)
        {
            LogErrorGettingTypeStructureFor(_logger, ex, typeName);
        }

        return null;
    }

    private async Task<TypeInheritance?> GetTypeInheritanceAsync(string typeName, string? context)
    {
        try
        {
            if (_classStructure == null) return null;

            var structure = await _classStructure.GetClassStructureAsync(typeName, context);
            if (structure == null) return null;

            var inheritance = new TypeInheritance
            {
                TypeName = typeName,
                BaseClass = structure.BaseTypes.FirstOrDefault(),
                Interfaces = structure.Interfaces.ToList(),
                InheritanceDepth = 0
            };

            // Calculate inheritance depth (simplified)
            if (structure.BaseTypes.Count > 0)
            {
                inheritance.InheritanceDepth = 1;
                // Could recursively calculate full depth here
            }

            // Find derived types (basic implementation)
            if (_workspace != null && _symbolQuery != null)
            {
                var allTypes = await _symbolQuery.GetAllTypesAsync();
                inheritance.DerivedTypes = allTypes
                    .Where(t => t.ContainerName?.Contains(typeName) == true)
                    .Select(t => t.Name)
                    .ToList();
            }

            return inheritance;
        }
        catch (Exception ex)
        {
            LogErrorGettingTypeInheritanceFor(_logger, ex, typeName);
        }

        return null;
    }

    private async Task<List<TypeUsage>?> GetTypeUsagesAsync(string typeName)
    {
        try
        {
            if (_referenceFinder == null || _workspace == null) return null;

            // Find all references to the type using the available method
            var referenceResult = await _referenceFinder.FindReferencesAsync(typeName);

            return referenceResult?.References.Select(r => new TypeUsage
            {
                FilePath = r.File,
                UsageType = "Reference",
                Location = new ConsolidatedLocation
                {
                    FilePath = r.File,
                    Line = r.Line,
                    Column = r.Column
                },
                Context = r.Context
            }).ToList() ?? new List<TypeUsage>();
        }
        catch (Exception ex)
        {
            LogErrorGettingTypeUsagesFor(_logger, ex, typeName);
        }

        return null;
    }

    private async Task<List<TypeImplementation>?> GetTypeImplementationsAsync(string typeName, string? context)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            // Check if the type is an interface or abstract class
            var typeSymbols = await _symbolQuery.FindSymbolsAsync(typeName, context);
            var typeKind = typeSymbols.FirstOrDefault()?.Kind;

            if (typeKind != "interface" && typeKind != "class")
                return new List<TypeImplementation>();

            // Find all types that implement or inherit from this type
            var allTypes = await _symbolQuery.GetAllTypesAsync();
            // For now, create a simplified implementation since SymbolResult doesn't have BaseType/Interfaces
            var implementations = allTypes
                .Where(t => t.ContainerName?.Contains(typeName) == true || t.Name.Contains(typeName))
                .Select(t => new TypeImplementation
                {
                    TypeName = t.Name,
                    ImplementationType = "Related", // Simplified since we can't determine inheritance easily
                    Location = new ConsolidatedLocation
                    {
                        FilePath = t.File,
                        Line = t.Line,
                        Column = t.Column
                    },
                    ImplementedMembers = new List<string>() // Would need more detailed analysis
                }).ToList();

            return implementations;
        }
        catch (Exception ex)
        {
            LogErrorGettingTypeImplementationsFor(_logger, ex, typeName);
        }

        return null;
    }

    private async Task<TypeMemberAnalysis?> AnalyzeTypeMembersAsync(string typeName, string? context)
    {
        try
        {
            if (_classStructure == null) return null;

            var structure = await _classStructure.GetClassStructureAsync(typeName, context);
            if (structure == null) return null;

            // Combine properties, methods, and fields into a members list
            var allMembers = new List<object>();
            allMembers.AddRange(structure.Properties);
            allMembers.AddRange(structure.Methods);
            allMembers.AddRange(structure.Fields);

            // Convert to TypeMember format
            var members = allMembers.SelectMany(m =>
            {
                if (m is MCPsharp.Models.Roslyn.PropertyStructure prop)
                {
                    return new[] { new TypeMember
                    {
                        Name = prop.Name,
                        Kind = "Property",
                        Accessibility = prop.Accessibility,
                        IsStatic = false,
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = "",
                            Line = prop.Line,
                            Column = 0
                        }
                    } };
                }
                else if (m is MCPsharp.Models.Roslyn.MethodStructure method)
                {
                    return new[] { new TypeMember
                    {
                        Name = method.Name,
                        Kind = "Method",
                        Accessibility = method.Accessibility,
                        IsStatic = method.IsStatic,
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = "",
                            Line = method.Line,
                            Column = 0
                        }
                    } };
                }
                else if (m is MCPsharp.Models.Roslyn.FieldStructure field)
                {
                    return new[] { new TypeMember
                    {
                        Name = field.Name,
                        Kind = "Field",
                        Accessibility = field.Accessibility,
                        IsStatic = field.IsStatic,
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = "",
                            Line = field.Line,
                            Column = 0
                        }
                    } };
                }
                return Array.Empty<TypeMember>();
            }).ToList();

            var publicMembers = members.Count(m => m.Accessibility == "Public");
            var privateMembers = members.Count(m => m.Accessibility == "Private");
            var staticMembers = members.Count(m => m.IsStatic);
            var abstractMembers = members.Count(m => m.Accessibility == "Abstract" || m.Accessibility == "Protected");

            // Identify complex members (methods with many parameters or large types)
            var complexMembers = members
                .Where(m => m.Kind == "Method" || m.Kind == "Property")
                .Take(5)
                .ToList();

            return new TypeMemberAnalysis
            {
                TotalMembers = members.Count,
                PublicMembers = publicMembers,
                PrivateMembers = privateMembers,
                StaticMembers = staticMembers,
                AbstractMembers = abstractMembers,
                ComplexMembers = complexMembers
            };
        }
        catch (Exception ex)
        {
            LogErrorAnalyzingTypeMembersFor(_logger, ex, typeName);
        }

        return null;
    }

    private async Task<TypeMetrics?> CalculateTypeMetricsAsync(string typeName, string? context)
    {
        try
        {
            if (_classStructure == null) return null;

            var structure = await _classStructure.GetClassStructureAsync(typeName, context);
            if (structure == null) return null;

            // Combine all members for counting
            var allMembers = new List<object>();
            allMembers.AddRange(structure.Properties);
            allMembers.AddRange(structure.Methods);
            allMembers.AddRange(structure.Fields);
            var members = allMembers.Count;
            var linesOfCode = members * 5; // Rough estimate

            // Calculate complexity metrics (simplified)
            var cyclomaticComplexity = structure.Methods.Count * 2.0;
            var couplingFactor = structure.Interfaces.Count + (structure.BaseTypes.Count > 0 ? 1 : 0);
            var responseForClass = structure.Methods.Count + structure.Properties.Count;
            var lackOfCohesion = Math.Max(0, members - (members / 2.0));

            // Calculate maintainability index
            var maintainabilityIndex = Math.Max(0, 171 - 5.2 * Math.Log(responseForClass + 1) - 0.23 * cyclomaticComplexity - 16.2 * Math.Log(linesOfCode + 1));

            return new TypeMetrics
            {
                LinesOfCode = linesOfCode,
                CyclomaticComplexity = cyclomaticComplexity,
                MaintainabilityIndex = maintainabilityIndex,
                CouplingFactor = couplingFactor,
                ResponseForClass = responseForClass,
                LackOfCohesion = lackOfCohesion
            };
        }
        catch (Exception ex)
        {
            LogErrorCalculatingTypeMetricsFor(_logger, ex, typeName);
        }

        return null;
    }
}

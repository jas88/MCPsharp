using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;

namespace MCPsharp.Services.Consolidated.Analyzers;

/// <summary>
/// Analyzes type-level code elements including structure, inheritance, implementations, and members.
/// </summary>
public class TypeAnalyzer
{
    private readonly ClassStructureService? _classStructure;
    private readonly SymbolQueryService? _symbolQuery;
    private readonly ReferenceFinderService? _referenceFinder;
    private readonly RoslynWorkspace? _workspace;
    private readonly ILogger<TypeAnalyzer> _logger;

    public TypeAnalyzer(
        ClassStructureService? classStructure = null,
        SymbolQueryService? symbolQuery = null,
        ReferenceFinderService? referenceFinder = null,
        RoslynWorkspace? workspace = null,
        ILogger<TypeAnalyzer>? logger = null)
    {
        _classStructure = classStructure;
        _symbolQuery = symbolQuery;
        _referenceFinder = referenceFinder;
        _workspace = workspace;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TypeAnalyzer>.Instance;
    }

    public async Task<TypeStructure?> GetTypeStructureAsync(string typeName, string? context, CancellationToken ct)
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
            _logger.LogError(ex, "Error getting type structure for {Type}", typeName);
        }

        return null;
    }

    public async Task<TypeInheritance?> GetTypeInheritanceAsync(string typeName, string? context, CancellationToken ct)
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

            if (structure.BaseTypes.Any())
            {
                inheritance.InheritanceDepth = 1;
            }

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
            _logger.LogError(ex, "Error getting type inheritance for {Type}", typeName);
        }

        return null;
    }

    public async Task<List<TypeUsage>?> GetTypeUsagesAsync(string typeName, string? context, CancellationToken ct)
    {
        try
        {
            if (_referenceFinder == null || _workspace == null) return null;

            var referenceResult = await _referenceFinder.FindReferencesAsync(typeName);

            return referenceResult?.References.Select(r => new TypeUsage
            {
                FilePath = r.File,
                UsageType = "Reference",
                Location = new Location
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
            _logger.LogError(ex, "Error getting type usages for {Type}", typeName);
        }

        return null;
    }

    public async Task<List<TypeImplementation>?> GetTypeImplementationsAsync(string typeName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var typeSymbols = await _symbolQuery.FindSymbolsAsync(typeName, context);
            var typeKind = typeSymbols.FirstOrDefault()?.Kind;

            if (typeKind != "interface" && typeKind != "class")
                return new List<TypeImplementation>();

            var allTypes = await _symbolQuery.GetAllTypesAsync();
            var implementations = allTypes
                .Where(t => t.ContainerName?.Contains(typeName) == true || t.Name.Contains(typeName))
                .Select(t => new TypeImplementation
                {
                    TypeName = t.Name,
                    ImplementationType = "Related",
                    Location = new Location
                    {
                        FilePath = t.File,
                        Line = t.Line,
                        Column = t.Column
                    },
                    ImplementedMembers = new List<string>()
                }).ToList();

            return implementations;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting type implementations for {Type}", typeName);
        }

        return null;
    }

    public async Task<TypeMemberAnalysis?> AnalyzeTypeMembersAsync(string typeName, string? context, CancellationToken ct)
    {
        try
        {
            if (_classStructure == null) return null;

            var structure = await _classStructure.GetClassStructureAsync(typeName, context);
            if (structure == null) return null;

            var allMembers = new List<object>();
            allMembers.AddRange(structure.Properties);
            allMembers.AddRange(structure.Methods);
            allMembers.AddRange(structure.Fields);

            var members = allMembers.Select(m =>
            {
                if (m is PropertyStructure prop)
                {
                    return new TypeMember
                    {
                        Name = prop.Name,
                        Kind = "Property",
                        Accessibility = prop.Accessibility,
                        IsStatic = false,
                        Location = new Location
                        {
                            FilePath = "",
                            Line = prop.Line,
                            Column = 0
                        }
                    };
                }
                else if (m is MethodStructure method)
                {
                    return new TypeMember
                    {
                        Name = method.Name,
                        Kind = "Method",
                        Accessibility = method.Accessibility,
                        IsStatic = method.IsStatic,
                        Location = new Location
                        {
                            FilePath = "",
                            Line = method.Line,
                            Column = 0
                        }
                    };
                }
                else if (m is FieldStructure field)
                {
                    return new TypeMember
                    {
                        Name = field.Name,
                        Kind = "Field",
                        Accessibility = field.Accessibility,
                        IsStatic = field.IsStatic,
                        Location = new Location
                        {
                            FilePath = "",
                            Line = field.Line,
                            Column = 0
                        }
                    };
                }
                return null;
            }).Where(m => m != null).ToList()!;

            var publicMembers = members.Count(m => m?.Accessibility == "Public");
            var privateMembers = members.Count(m => m?.Accessibility == "Private");
            var staticMembers = members.Count(m => m?.IsStatic == true);
            var abstractMembers = members.Count(m => m?.Accessibility == "Abstract" || m?.Accessibility == "Protected");

            var complexMembers = members
                .Where(m => m?.Kind == "Method" || m?.Kind == "Property")
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
            _logger.LogError(ex, "Error analyzing type members for {Type}", typeName);
        }

        return null;
    }

    public async Task<TypeMetrics?> CalculateTypeMetricsAsync(string typeName, string? context, CancellationToken ct)
    {
        try
        {
            if (_classStructure == null) return null;

            var structure = await _classStructure.GetClassStructureAsync(typeName, context);
            if (structure == null) return null;

            var allMembers = new List<object>();
            allMembers.AddRange(structure.Properties);
            allMembers.AddRange(structure.Methods);
            allMembers.AddRange(structure.Fields);
            var members = allMembers.Count;
            var linesOfCode = members * 5;

            var cyclomaticComplexity = structure.Methods.Count * 2.0;
            var couplingFactor = structure.Interfaces.Count + (structure.BaseTypes.Count > 0 ? 1 : 0);
            var responseForClass = structure.Methods.Count + structure.Properties.Count;
            var lackOfCohesion = Math.Max(0, members - (members / 2.0));

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
            _logger.LogError(ex, "Error calculating type metrics for {Type}", typeName);
        }

        return null;
    }

    private static List<TypeMember> ConvertToTypeMembers(ClassStructure structure)
    {
        var members = new List<TypeMember>();

        members.AddRange(structure.Properties.Select(p => new TypeMember
        {
            Name = p.Name,
            Kind = "property",
            Accessibility = p.Accessibility,
            IsStatic = false,
            Location = new Location { FilePath = "", Line = p.Line, Column = 0 }
        }));

        members.AddRange(structure.Methods.Select(m => new TypeMember
        {
            Name = m.Name,
            Kind = "method",
            Accessibility = m.Accessibility,
            IsStatic = m.IsStatic,
            Location = new Location { FilePath = "", Line = m.Line, Column = 0 }
        }));

        members.AddRange(structure.Fields.Select(f => new TypeMember
        {
            Name = f.Name,
            Kind = "field",
            Accessibility = f.Accessibility,
            IsStatic = f.IsStatic,
            Location = new Location { FilePath = "", Line = f.Line, Column = 0 }
        }));

        return members;
    }
}

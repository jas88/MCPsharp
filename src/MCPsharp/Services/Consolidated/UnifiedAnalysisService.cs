using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Roslyn;
using MCPsharp.Models.Roslyn;
using MCPsharp.Models.Architecture;
using Microsoft.CodeAnalysis;
using ConsolidatedLocation = MCPsharp.Models.Consolidated.Location;
using ConsolidatedProjectReference = MCPsharp.Models.Consolidated.ProjectReference;
using ConsolidatedArchitectureDefinition = MCPsharp.Models.Consolidated.ArchitectureDefinition;
using ConsolidatedArchitectureLayer = MCPsharp.Models.Consolidated.ArchitectureLayer;
using ConsolidatedArchitectureWarning = MCPsharp.Models.Consolidated.ArchitectureWarning;
using ArchitectureDependencyNode = MCPsharp.Models.Architecture.DependencyNode;
using ArchitectureDependencyEdge = MCPsharp.Models.Architecture.DependencyEdge;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Unified analysis service consolidating symbol, type, file, project, architecture, dependency, and quality analysis
/// Phase 2: Consolidate 70 analysis tools into 15 unified analysis tools
/// </summary>
public class UnifiedAnalysisService
{
    private readonly RoslynWorkspace? _workspace;
    private readonly SymbolQueryService? _symbolQuery;
    private readonly ClassStructureService? _classStructure;
    private readonly ReferenceFinderService? _referenceFinder;
    private readonly ProjectParserService? _projectParser;
    private readonly IArchitectureValidatorService? _architectureValidator;
    private readonly IDuplicateCodeDetectorService? _duplicateDetector;
    private readonly ILogger<UnifiedAnalysisService> _logger;

    public UnifiedAnalysisService(
        RoslynWorkspace? workspace = null,
        SymbolQueryService? symbolQuery = null,
        ClassStructureService? classStructure = null,
        ReferenceFinderService? referenceFinder = null,
        ProjectParserService? projectParser = null,
        IArchitectureValidatorService? architectureValidator = null,
        IDuplicateCodeDetectorService? duplicateDetector = null,
        ILogger<UnifiedAnalysisService>? logger = null)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
        _classStructure = classStructure;
        _referenceFinder = referenceFinder;
        _projectParser = projectParser;
        _architectureValidator = architectureValidator;
        _duplicateDetector = duplicateDetector;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UnifiedAnalysisService>.Instance;
    }

    /// <summary>
    /// analyze_symbol - Symbol-level analysis
    /// Consolidates: find_symbol, get_symbol_info, find_references, find_callers, analyze_call_patterns
    /// </summary>
    public async Task<SymbolAnalysisResponse> AnalyzeSymbolAsync(SymbolAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_symbolQuery == null || _workspace == null)
            {
                return new SymbolAnalysisResponse
                {
                    SymbolName = request.SymbolName,
                    Error = "Symbol analysis services not available",
                    Metadata = new ResponseMetadata { Success = false, ProcessingTime = stopwatch.Elapsed }
                };
            }

            var response = new SymbolAnalysisResponse
            {
                SymbolName = request.SymbolName,
                Context = request.Context,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Basic symbol information
            if (request.Scope == AnalysisScope.Definition || request.Scope == AnalysisScope.All)
            {
                response.Definition = await GetSymbolDefinitionAsync(request.SymbolName, request.Context, ct);
            }

            // References
            if (request.Options?.Include.HasFlag(IncludeFlags.Dependencies) == true || request.Scope == AnalysisScope.References || request.Scope == AnalysisScope.All)
            {
                response.References = await GetSymbolReferencesAsync(request.SymbolName, request.Context, ct);
            }

            // Callers
            if (request.Options?.Include.HasFlag(IncludeFlags.Dependencies) == true && request.Scope == AnalysisScope.All)
            {
                response.Callers = await GetSymbolCallersAsync(request.SymbolName, request.Context, ct);
            }

            // Usage patterns
            if (request.Options?.Detail >= DetailLevel.Detailed)
            {
                response.UsagePatterns = await AnalyzeSymbolUsageAsync(request.SymbolName, request.Context, ct);
            }

            // Related symbols
            if (request.Options?.Include.HasFlag(IncludeFlags.Suggestions) == true)
            {
                response.RelatedSymbols = await FindRelatedSymbolsAsync(request.SymbolName, request.Context, ct);
            }

            // Metrics
            if (request.Options?.Include.HasFlag(IncludeFlags.Metrics) == true)
            {
                response.Metrics = await CalculateSymbolMetricsAsync(request.SymbolName, request.Context, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing symbol {Symbol}", request.SymbolName);

            return new SymbolAnalysisResponse
            {
                SymbolName = request.SymbolName,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// analyze_type - Type-level analysis
    /// Consolidates: get_class_structure, find_type_usages, find_implementations
    /// </summary>
    public async Task<TypeAnalysisResponse> AnalyzeTypeAsync(TypeAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_classStructure == null || _workspace == null)
            {
                return new TypeAnalysisResponse
                {
                    TypeName = request.TypeName,
                    Error = "Type analysis services not available",
                    Metadata = new ResponseMetadata { Success = false, ProcessingTime = stopwatch.Elapsed }
                };
            }

            var response = new TypeAnalysisResponse
            {
                TypeName = request.TypeName,
                Context = request.Context,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Basic type structure
            if (request.Scope == AnalysisScope.Definition || request.Scope == AnalysisScope.All)
            {
                response.Structure = await GetTypeStructureAsync(request.TypeName, request.Context, ct);
            }

            // Inheritance hierarchy
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                response.Inheritance = await GetTypeInheritanceAsync(request.TypeName, request.Context, ct);
            }

            // Usage analysis
            if (request.Scope == AnalysisScope.References || request.Scope == AnalysisScope.All)
            {
                response.Usages = await GetTypeUsagesAsync(request.TypeName, request.Context, ct);
            }

            // Implementations
            if (request.Options?.Include.HasFlag(IncludeFlags.Dependencies) == true)
            {
                response.Implementations = await GetTypeImplementationsAsync(request.TypeName, request.Context, ct);
            }

            // Members analysis
            if (request.Options?.Detail >= DetailLevel.Detailed)
            {
                response.Members = await AnalyzeTypeMembersAsync(request.TypeName, request.Context, ct);
            }

            // Type metrics
            if (request.Options?.Include.HasFlag(IncludeFlags.Metrics) == true)
            {
                response.Metrics = await CalculateTypeMetricsAsync(request.TypeName, request.Context, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing type {Type}", request.TypeName);

            return new TypeAnalysisResponse
            {
                TypeName = request.TypeName,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// analyze_file - File-level analysis
    /// Consolidates: Multiple file-specific analysis tools
    /// </summary>
    public async Task<FileAnalysisResponse> AnalyzeFileAsync(FileAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new FileAnalysisResponse
            {
                FilePath = request.FilePath,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Basic file information
            if (request.Options?.Detail >= DetailLevel.Basic)
            {
                response.Info = await GetFileBasicInfoAsync(request.FilePath, ct);
            }

            // Structure analysis
            if (request.AnalysisTypes.Contains(FileAnalysisType.Structure) || request.AnalysisTypes.Contains(FileAnalysisType.All))
            {
                response.Structure = await AnalyzeFileStructureAsync(request.FilePath, ct);
            }

            // Complexity analysis
            if (request.AnalysisTypes.Contains(FileAnalysisType.Complexity) || request.AnalysisTypes.Contains(FileAnalysisType.All))
            {
                response.Complexity = await AnalyzeFileComplexityAsync(request.FilePath, ct);
            }

            // Dependencies
            if (request.AnalysisTypes.Contains(FileAnalysisType.Dependencies) || request.AnalysisTypes.Contains(FileAnalysisType.All))
            {
                response.Dependencies = await AnalyzeFileDependenciesAsync(request.FilePath, ct);
            }

            // Quality metrics
            if (request.AnalysisTypes.Contains(FileAnalysisType.Quality) || request.AnalysisTypes.Contains(FileAnalysisType.All))
            {
                response.Quality = await AnalyzeFileQualityAsync(request.FilePath, ct);
            }

            // Issues and suggestions
            if (request.Options?.Include.HasFlag(IncludeFlags.Suggestions) == true)
            {
                response.Issues = await DetectFileIssuesAsync(request.FilePath, ct);
                response.Suggestions = await GenerateFileSuggestionsAsync(request.FilePath, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file {File}", request.FilePath);

            return new FileAnalysisResponse
            {
                FilePath = request.FilePath,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// analyze_project - Project-level analysis
    /// Consolidates: parse_project, project-specific tools
    /// </summary>
    public async Task<ProjectAnalysisResponse> AnalyzeProjectAsync(ProjectAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_projectParser == null)
            {
                return new ProjectAnalysisResponse
                {
                    ProjectPath = request.ProjectPath,
                    Error = "Project analysis service not available",
                    Metadata = new ResponseMetadata { Success = false, ProcessingTime = stopwatch.Elapsed }
                };
            }

            var response = new ProjectAnalysisResponse
            {
                ProjectPath = request.ProjectPath,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Basic project structure
            response.Structure = await GetProjectStructureAsync(request.ProjectPath, ct);

            // File inventory
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                response.Files = await GetProjectFileInventoryAsync(request.ProjectPath, request.Options, ct);
            }

            // Dependencies
            if (request.AnalysisTypes.Contains(ProjectAnalysisType.Dependencies) || request.AnalysisTypes.Contains(ProjectAnalysisType.All))
            {
                response.Dependencies = await AnalyzeProjectDependenciesAsync(request.ProjectPath, ct);
            }

            // Build configuration
            if (request.AnalysisTypes.Contains(ProjectAnalysisType.Build) || request.AnalysisTypes.Contains(ProjectAnalysisType.All))
            {
                response.BuildInfo = await AnalyzeProjectBuildAsync(request.ProjectPath, ct);
            }

            // Metrics
            if (request.Options?.Include.HasFlag(IncludeFlags.Metrics) == true)
            {
                response.Metrics = await CalculateProjectMetricsAsync(request.ProjectPath, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project {Project}", request.ProjectPath);

            return new ProjectAnalysisResponse
            {
                ProjectPath = request.ProjectPath,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// analyze_architecture - Architecture analysis
    /// Consolidates: Architecture validation tools
    /// </summary>
    public async Task<ArchitectureAnalysisResponse> AnalyzeArchitectureAsync(ArchitectureAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (_architectureValidator == null)
            {
                return new ArchitectureAnalysisResponse
                {
                    Scope = request.Scope.ToString(),
                    Error = "Architecture validation service not available",
                    Metadata = new ResponseMetadata { Success = false, ProcessingTime = stopwatch.Elapsed }
                };
            }

            var response = new ArchitectureAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Load or use provided architecture definition
            var scopeString = request.Scope.ToString();
            var architectureDefinition = ConvertToArchitectureDefinition(request.Definition ?? await GetDefaultArchitectureDefinitionAsync(ct));

            // Validate architecture
            response.Validation = ConvertToValidationResult(await _architectureValidator.ValidateArchitectureAsync(scopeString, architectureDefinition, ct));

            // Detect violations
            var layerViolations = await _architectureValidator.DetectLayerViolationsAsync(scopeString, architectureDefinition, ct);
            response.Violations = layerViolations.Select(v => new ArchitectureViolation
            {
                From = v.SourceLayer,
                To = v.TargetLayer,
                Rule = v.RuleViolated,
                Location = new MCPsharp.Models.Consolidated.Location { FilePath = v.FilePath, Line = v.LineNumber, Column = v.ColumnNumber },
                Description = v.Description
            }).ToList();

            // Analyze dependencies
            var dependencyAnalysis = await _architectureValidator.AnalyzeDependenciesAsync(scopeString, architectureDefinition, ct);
            response.DependencyAnalysis = new DependencyAnalysisResponse
            {
                Scope = scopeString,
                Depth = 2,
                Graph = ConvertToDependencyGraph(dependencyAnalysis),
                Metadata = new ResponseMetadata { Success = true }
            };

            // Generate recommendations
            if (request.Options?.Include.HasFlag(IncludeFlags.Suggestions) == true)
            {
                var recommendations = await _architectureValidator.GetRecommendationsAsync(layerViolations, ct);
                response.Recommendations = recommendations.Select(r => new MCPsharp.Models.Consolidated.ArchitectureRecommendation
                {
                    Type = r.Type,
                    Description = r.Description,
                    Target = r.Title, // Use Title instead of TargetType
                    Priority = Convert.ToDouble(r.Priority == 0 ? 1.0 : r.Priority), // Convert to double if needed
                    AffectedComponents = new List<string>() // No AffectedComponents property available
                }).ToList();
            }

            // Architecture metrics
            if (request.Options?.Include.HasFlag(IncludeFlags.Metrics) == true)
            {
                response.Metrics = await CalculateArchitectureMetricsAsync(scopeString, architectureDefinition, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing architecture for {Scope}", request.Scope);

            return new ArchitectureAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// analyze_dependencies - Dependency analysis
    /// Consolidates: Dependency analysis tools
    /// </summary>
    public async Task<DependencyAnalysisResponse> AnalyzeDependenciesAsync(DependencyAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new DependencyAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                Depth = request.Depth,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Build dependency graph
            response.Graph = await BuildDependencyGraphAsync(request.Scope.ToString(), request.Depth, ct);

            // Detect circular dependencies
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                var circularDeps = await DetectCircularDependenciesAsync(request.Scope.ToString(), ct);
                response.CircularDependencies = circularDeps.Select(cd => new ConsolidatedCircularDependency
                {
                    Cycle = cd.Methods.Select(m => $"{m.ContainingType}.{m.Name}").ToList(),
                    Locations = cd.FilesInvolved.Select(f => new MCPsharp.Models.Consolidated.Location { FilePath = f, Line = 0, Column = 0 }).ToList(),
                    Length = cd.CycleLength
                }).ToList();
            }

            // Impact analysis
            if (request.Options?.Include.HasFlag(IncludeFlags.Dependencies) == true)
            {
                response.ImpactAnalysis = await AnalyzeDependencyImpactAsync(request.Scope.ToString(), ct);
            }

            // Critical path analysis
            if (request.Options?.Detail >= DetailLevel.Detailed)
            {
                response.CriticalPaths = await AnalyzeCriticalPathsAsync(request.Scope.ToString(), ct);
            }

            // Dependency metrics
            if (request.Options?.Include.HasFlag(IncludeFlags.Metrics) == true)
            {
                response.Metrics = await CalculateDependencyMetricsAsync(response.Graph, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing dependencies for {Scope}", request.Scope);

            return new DependencyAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    /// <summary>
    /// analyze_quality - Code quality analysis
    /// Consolidates: Duplicate detection, quality tools
    /// </summary>
    public async Task<QualityAnalysisResponse> AnalyzeQualityAsync(QualityAnalysisRequest request, CancellationToken ct = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = new QualityAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Duplicate code detection
            if (request.Metrics.Contains(AnalysisQualityMetric.Duplicates) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                if (_duplicateDetector != null)
                {
                    var duplicates = await _duplicateDetector.DetectDuplicatesAsync(request.Scope.ToString(), new DuplicateDetectionOptions(), ct);
                    response.Duplicates = duplicates.DuplicateGroups.Select(d => new DuplicateGroup
                    {
                        Type = d.DuplicationType.ToString(),
                        Similarity = d.SimilarityScore,
                        Files = d.CodeBlocks.Select(f => new DuplicateFile
                        {
                            FilePath = f.FilePath,
                            Locations = new List<MCPsharp.Models.Consolidated.Location>
                            {
                                new MCPsharp.Models.Consolidated.Location { FilePath = f.FilePath, Line = f.StartLine, Column = f.StartColumn }
                            },
                            Lines = f.EndLine - f.StartLine + 1,
                            MatchPercentage = d.SimilarityScore * 100
                        }).ToList(),
                        Description = $"Found {d.CodeBlocks.Count} duplicate blocks with similarity {d.SimilarityScore:P1}"
                    }).ToList();
                }
            }

            // Complexity analysis
            if (request.Metrics.Contains(AnalysisQualityMetric.Complexity) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.Complexity = await AnalyzeComplexityAsync(request.Scope.ToString(), ct);
            }

            // Maintainability analysis
            if (request.Metrics.Contains(AnalysisQualityMetric.Maintainability) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.Maintainability = await AnalyzeMaintainabilityAsync(request.Scope.ToString(), ct);
            }

            // Test coverage
            if (request.Metrics.Contains(AnalysisQualityMetric.TestCoverage) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.TestCoverage = await AnalyzeTestCoverageAsync(request.Scope.ToString(), ct);
            }

            // Code smells
            if (request.Metrics.Contains(AnalysisQualityMetric.CodeSmells) || request.Metrics.Contains(AnalysisQualityMetric.All))
            {
                response.CodeSmells = await DetectCodeSmellsAsync(request.Scope.ToString(), ct);
            }

            // Quality score
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                response.QualityScore = await CalculateQualityScoreAsync(response, ct);
            }

            // Recommendations
            if (request.Options?.Include.HasFlag(IncludeFlags.Suggestions) == true)
            {
                response.Recommendations = await GenerateQualityRecommendationsAsync(response, ct);
            }

            stopwatch.Stop();
            // Update metadata with final processing time and results
            var existingMetadata = response.Metadata;
            existingMetadata.ProcessingTime = stopwatch.Elapsed;
            existingMetadata.ResultCount = CountAnalysisResults(response);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing quality for {Scope}", request.Scope);

            return new QualityAnalysisResponse
            {
                Scope = request.Scope.ToString(),
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    #region Private Helper Methods

    private static int CountAnalysisResults(object analysisResult)
    {
        return analysisResult switch
        {
            SymbolAnalysisResponse s => (s.Definition != null ? 1 : 0) + (s.References?.Count ?? 0) + (s.Callers?.Count ?? 0),
            TypeAnalysisResponse t => (t.Structure != null ? 1 : 0) + (t.Usages?.Count ?? 0) + (t.Implementations?.Count ?? 0),
            FileAnalysisResponse f => (f.Structure != null ? 1 : 0) + (f.Issues?.Count ?? 0) + (f.Suggestions?.Count ?? 0),
            ProjectAnalysisResponse p => (p.Files?.Count ?? 0) + (p.Dependencies?.PackageReferences?.Count ?? 0) + (p.Dependencies?.ProjectReferences?.Count ?? 0) + (p.Dependencies?.AssemblyReferences?.Count ?? 0),
            ArchitectureAnalysisResponse a => (a.Violations?.Count ?? 0) + (a.Recommendations?.Count ?? 0),
            DependencyAnalysisResponse d => (d.Graph?.Nodes?.Count ?? 0) + (d.CircularDependencies?.Count ?? 0),
            QualityAnalysisResponse q => (q.Duplicates?.Count ?? 0) + (q.CodeSmells?.Count ?? 0),
            _ => 1
        };
    }

    private async Task<SymbolDefinition?> GetSymbolDefinitionAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var symbols = await _symbolQuery.FindSymbolsAsync(symbolName, context);
            var definition = symbols.FirstOrDefault();

            if (definition != null)
            {
                return new SymbolDefinition
                {
                    Name = definition.Name,
                    Kind = definition.Kind,
                    Location = new MCPsharp.Models.Consolidated.Location
                    {
                        FilePath = definition.File,
                        Line = definition.Line,
                        Column = definition.Column
                    },
                    ContainingType = definition.ContainerName,
                    Namespace = definition.ContainerName,
                    Accessibility = "Public", // Default since SymbolResult doesn't have this info
                    IsStatic = false, // Default since SymbolResult doesn't have this info
                    IsVirtual = false, // Default since SymbolResult doesn't have this info
                    IsAbstract = false // Default since SymbolResult doesn't have this info
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol definition for {Symbol}", symbolName);
        }

        return null;
    }

    private async Task<List<SymbolReference>?> GetSymbolReferencesAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_referenceFinder == null || _workspace == null) return null;

            var references = await _referenceFinder.FindReferencesAsync(symbolName, context);
            if (references == null || references.References == null)
                return new List<SymbolReference>();

            return references.References.Select(r => new SymbolReference
            {
                FilePath = r.File,
                Line = r.Line,
                Column = r.Column,
                ReferenceType = "Reference", // Default since ReferenceLocation doesn't have ReferenceType
                ContainingMember = r.Context
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol references for {Symbol}", symbolName);
        }

        return null;
    }

    private async Task<List<SymbolCaller>?> GetSymbolCallersAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            // Get all references to the symbol
            var references = await GetSymbolReferencesAsync(symbolName, context, ct);
            if (references == null) return null;

            var callers = new List<SymbolCaller>();

            foreach (var reference in references)
            {
                try
                {
                    // Get the symbol at the reference location to find the calling method
                    var callingSymbol = await _symbolQuery.GetSymbolAtLocationAsync(
                        reference.FilePath, reference.Line, reference.Column);

                    if (callingSymbol != null &&
                        (callingSymbol.Kind == "method" || callingSymbol.Kind == "property" || callingSymbol.Kind == "constructor"))
                    {
                        callers.Add(new SymbolCaller
                        {
                            FilePath = reference.FilePath, // Use reference location since SymbolInfo doesn't have file location
                            Line = reference.Line,
                            Column = reference.Column,
                            CallerName = callingSymbol.Name,
                            CallType = "Direct",
                            IsAsync = callingSymbol.Name.Contains("Async")
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing caller for reference at {File}:{Line}",
                        reference.FilePath, reference.Line);
                }
            }

            return callers.DistinctBy(c => $"{c.CallerName}@{c.FilePath}:{c.Line}").ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting symbol callers for {Symbol}", symbolName);
        }

        return null;
    }

    private async Task<SymbolUsagePatterns?> AnalyzeSymbolUsageAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context, ct);
            var callers = await GetSymbolCallersAsync(symbolName, context, ct);

            if (references == null || callers == null) return null;

            var contexts = references
                .Where(r => !string.IsNullOrEmpty(r.ContainingMember))
                .GroupBy(r => r.ContainingMember!)
                .Select(g => new { Context = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .Select(x => x.Context)
                .ToList();

            var patterns = new List<string>();
            if (callers.Count > 10) patterns.Add("Frequently called");
            if (references.Count > callers.Count) patterns.Add("Multiple references per caller");
            if (contexts.Any(c => c.Contains("Test") || c.Contains("test"))) patterns.Add("Used in tests");

            return new SymbolUsagePatterns
            {
                TotalUsages = references.Count,
                UniqueCallers = callers.Count,
                CommonContexts = contexts,
                UsagePatterns = patterns
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing symbol usage patterns for {Symbol}", symbolName);
        }

        return null;
    }

    private async Task<List<RelatedSymbol>?> FindRelatedSymbolsAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var relatedSymbols = new List<RelatedSymbol>();

            // Get the symbol definition to understand its context
            var definition = await GetSymbolDefinitionAsync(symbolName, context, ct);
            if (definition == null) return null;

            // Find symbols in the same containing type (simplified - would need type symbol info)
            if (!string.IsNullOrEmpty(definition.ContainingType))
            {
                var allSymbols = await _symbolQuery.GetAllTypesAsync();
                var sameTypeSymbols = allSymbols
                    .Where(s => s.ContainerName == definition.ContainingType)
                    .Take(20);
                relatedSymbols.AddRange(sameTypeSymbols
                    .Where(s => s.Name != symbolName)
                    .Select(s => new RelatedSymbol
                    {
                        Name = s.Name,
                        Relationship = "Same type",
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = s.File,
                            Line = s.Line,
                            Column = s.Column
                        },
                        Relevance = 0.8
                    }));
            }

            // Find symbols in the same namespace
            if (!string.IsNullOrEmpty(definition.Namespace))
            {
                var sameNamespaceSymbols = await _symbolQuery.GetSymbolsInNamespaceAsync(definition.Namespace);
                relatedSymbols.AddRange(sameNamespaceSymbols
                    .Where(s => s.Name != symbolName && s.ContainerName != definition.ContainingType)
                    .Take(10)
                    .Select(s => new RelatedSymbol
                    {
                        Name = s.Name,
                        Relationship = "Same namespace",
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = s.File,
                            Line = s.Line,
                            Column = s.Column
                        },
                        Relevance = 0.6
                    }));
            }

            // Find symbols with similar names
            var allTypesForSimilarity = await _symbolQuery.GetAllTypesAsync();
            var similarSymbols = allTypesForSimilarity
                .Where(s => Math.Abs(s.Name.Length - symbolName.Length) <= 2 &&
                           (s.Name.Contains(symbolName.Substring(0, Math.Min(3, symbolName.Length))) ||
                            symbolName.Contains(s.Name.Substring(0, Math.Min(3, s.Name.Length)))))
                .Where(s => s.Name != symbolName)
                .Take(5)
                .Select(s => new RelatedSymbol
                {
                    Name = s.Name,
                    Relationship = "Similar name",
                    Location = new ConsolidatedLocation
                    {
                        FilePath = s.File,
                        Line = s.Line,
                        Column = s.Column
                    },
                    Relevance = 0.4
                });

            relatedSymbols.AddRange(similarSymbols);

            return relatedSymbols
                .GroupBy(s => s.Name)
                .Select(g => g.First())
                .OrderByDescending(s => s.Relevance)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding related symbols for {Symbol}", symbolName);
        }

        return null;
    }

    private async Task<SymbolMetrics?> CalculateSymbolMetricsAsync(string symbolName, string? context, CancellationToken ct)
    {
        try
        {
            if (_symbolQuery == null || _workspace == null) return null;

            var definition = await GetSymbolDefinitionAsync(symbolName, context, ct);
            if (definition == null) return null;

            var references = await GetSymbolReferencesAsync(symbolName, context, ct);

            // Basic metrics
            var usageCount = references?.Count ?? 0;
            var parameterCount = 0; // Would need to parse method signature for this
            var linesOfCode = 0; // Would need to analyze method body for this

            // Calculate maintainability index (simplified version)
            var maintainabilityIndex = Math.Max(0, 171 - 5.2 * Math.Log(usageCount + 1) - 0.23 * 10 - 16.2 * Math.Log(linesOfCode + 1));

            return new SymbolMetrics
            {
                UsageCount = usageCount,
                ParameterCount = parameterCount,
                LinesOfCode = linesOfCode,
                CyclomaticComplexity = 1, // Simplified
                CognitiveComplexity = 1, // Simplified
                MaintainabilityIndex = maintainabilityIndex
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating symbol metrics for {Symbol}", symbolName);
        }

        return null;
    }

    private async Task<TypeStructure?> GetTypeStructureAsync(string typeName, string? context, CancellationToken ct)
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

    private async Task<TypeInheritance?> GetTypeInheritanceAsync(string typeName, string? context, CancellationToken ct)
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
            if (structure.BaseTypes.Any())
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
            _logger.LogError(ex, "Error getting type inheritance for {Type}", typeName);
        }

        return null;
    }

    private async Task<List<TypeUsage>?> GetTypeUsagesAsync(string typeName, string? context, CancellationToken ct)
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
            _logger.LogError(ex, "Error getting type usages for {Type}", typeName);
        }

        return null;
    }

    private async Task<List<TypeImplementation>?> GetTypeImplementationsAsync(string typeName, string? context, CancellationToken ct)
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
            _logger.LogError(ex, "Error getting type implementations for {Type}", typeName);
        }

        return null;
    }

    private async Task<TypeMemberAnalysis?> AnalyzeTypeMembersAsync(string typeName, string? context, CancellationToken ct)
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
            var staticMembers = members.Count(m => m.IsStatic == true);
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
            _logger.LogError(ex, "Error analyzing type members for {Type}", typeName);
        }

        return null;
    }

    private async Task<TypeMetrics?> CalculateTypeMetricsAsync(string typeName, string? context, CancellationToken ct)
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
            _logger.LogError(ex, "Error calculating type metrics for {Type}", typeName);
        }

        return null;
    }

    private async Task<FileBasicInfo?> GetFileBasicInfoAsync(string filePath, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            var fileInfo = new FileInfo(filePath);
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Basic language detection
            var language = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".cs" => "C#",
                ".js" => "JavaScript",
                ".ts" => "TypeScript",
                ".py" => "Python",
                ".java" => "Java",
                ".cpp" or ".cxx" or ".cc" => "C++",
                ".c" => "C",
                ".json" => "JSON",
                ".xml" => "XML",
                ".yaml" or ".yml" => "YAML",
                ".md" => "Markdown",
                _ => "Unknown"
            };

            // Extract namespaces and types (basic implementation for C#)
            var namespaces = new List<string>();
            var types = new List<string>();

            if (language == "C#")
            {
                var namespaceMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"namespace\s+([^\s{;]+)");
                namespaces.AddRange(namespaceMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value));

                var typeMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"(class|interface|struct|enum|record)\s+([^\s<:,{]+)");
                types.AddRange(typeMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[2].Value));
            }

            return new FileBasicInfo
            {
                FilePath = filePath,
                Language = language,
                LineCount = lines.Length,
                Size = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                Namespaces = namespaces.Distinct().ToList(),
                Types = types.Distinct().ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file basic info for {File}", filePath);
        }

        return null;
    }

    private async Task<FileStructure?> AnalyzeFileStructureAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var basicInfo = await GetFileBasicInfoAsync(filePath, ct);
            if (basicInfo == null) return null;

            var content = await File.ReadAllTextAsync(filePath, ct);
            var namespaces = new List<string>();
            var types = new List<FileTypeDefinition>();
            var members = new List<FileMemberDefinition>();
            var imports = new List<FileImport>();

            if (basicInfo.Language == "C#")
            {
                // Parse using regex for basic structure
                var namespaceMatches = System.Text.RegularExpressions.Regex.Matches(
                    content, @"namespace\s+([^\s{]+)");
                namespaces.AddRange(namespaceMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value));

                // Find type definitions
                var typePattern = @"(class|interface|struct|enum|record)\s+([^\s<:,{]+)\s*(?::\s*([^{]+))?";
                var typeMatches = System.Text.RegularExpressions.Regex.Matches(content, typePattern);
                foreach (System.Text.RegularExpressions.Match match in typeMatches)
                {
                    types.Add(new FileTypeDefinition
                    {
                        Name = match.Groups[2].Value,
                        Kind = match.Groups[1].Value,
                        Location = new ConsolidatedLocation
                        {
                            FilePath = filePath,
                            Line = content.Substring(0, match.Index).Split('\n').Length,
                            Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                        },
                        BaseClass = match.Groups[3].Value?.Split(',').FirstOrDefault()?.Trim()
                    });
                }

                // Find using statements
                var usingPattern = @"using\s+([^\s;]+)";
                var usingMatches = System.Text.RegularExpressions.Regex.Matches(content, usingPattern);
                foreach (System.Text.RegularExpressions.Match match in usingMatches)
                {
                    imports.Add(new FileImport
                    {
                        ImportPath = match.Groups[1].Value,
                        ImportType = "Using",
                        Location = new ConsolidatedLocation
                        {
                            FilePath = filePath,
                            Line = content.Substring(0, match.Index).Split('\n').Length,
                            Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                        },
                        IsUsed = true // Simplified - would need actual usage analysis
                    });
                }
            }

            return new FileStructure
            {
                Namespaces = namespaces.Distinct().ToList(),
                Types = types,
                Members = members,
                Imports = imports
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file structure for {File}", filePath);
        }

        return null;
    }

    private async Task<FileComplexity?> AnalyzeFileComplexityAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var linesOfCode = lines.Length;

            // Basic complexity analysis
            var methodPattern = @"(?:public|private|protected|internal)?\s*(?:static|async|virtual|override)?\s*[\w<>]+\s+(\w+)\s*\(";
            var methodMatches = System.Text.RegularExpressions.Regex.Matches(content, methodPattern);

            var methodComplexities = new List<MethodComplexity>();
            foreach (System.Text.RegularExpressions.Match match in methodMatches)
            {
                var methodName = match.Groups[1].Value;
                var lineNum = content.Substring(0, match.Index).Split('\n').Length;

                // Simplified complexity calculation
                var methodContent = ExtractMethodContent(content, match.Index);
                var complexity = CalculateCyclomaticComplexity(methodContent);

                methodComplexities.Add(new MethodComplexity
                {
                    MethodName = methodName,
                    Location = new ConsolidatedLocation
                    {
                        FilePath = filePath,
                        Line = lineNum,
                        Column = match.Index - content.LastIndexOf('\n', match.Index) - 1
                    },
                    CyclomaticComplexity = complexity
                });
            }

            var totalComplexity = methodComplexities.Sum(m => m.CyclomaticComplexity);
            var cognitiveComplexity = totalComplexity * 1.5; // Simplified cognitive complexity

            return new FileComplexity
            {
                FilePath = filePath,
                CyclomaticComplexity = totalComplexity,
                CognitiveComplexity = cognitiveComplexity,
                LinesOfCode = linesOfCode,
                MethodComplexities = methodComplexities
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file complexity for {File}", filePath);
        }

        return null;
    }

    private async Task<FileDependencies?> AnalyzeFileDependenciesAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var dependencies = new List<string>();
            var externalDependencies = new List<string>();

            if (Path.GetExtension(filePath).ToLowerInvariant() == ".cs")
            {
                // Extract using statements
                var usingPattern = @"using\s+([^\s;]+)";
                var matches = System.Text.RegularExpressions.Regex.Matches(content, usingPattern);
                dependencies.AddRange(matches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => m.Groups[1].Value));

                // External dependencies are those that start with known external libraries
                externalDependencies = dependencies
                    .Where(d => d.StartsWith("System.") || d.StartsWith("Microsoft.") ||
                                d.Contains('.') && !d.StartsWith("Project"))
                    .ToList();
            }

            return new FileDependencies
            {
                FilePath = filePath,
                InternalDependencies = dependencies.Except(externalDependencies).Select(d => new FileDependency
                {
                    DependencyPath = d,
                    DependencyType = "Using",
                    Locations = new List<MCPsharp.Models.Consolidated.Location> { new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 } },
                    Strength = 1.0
                }).ToList(),
                ExternalDependencies = externalDependencies.Select(d => new FileDependency
                {
                    DependencyPath = d,
                    DependencyType = "External",
                    Locations = new List<MCPsharp.Models.Consolidated.Location> { new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 } },
                    Strength = 1.0
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file dependencies for {File}", filePath);
        }

        return null;
    }

    private async Task<FileQuality?> AnalyzeFileQualityAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var complexity = await AnalyzeFileComplexityAsync(filePath, ct);
            var dependencies = await AnalyzeFileDependenciesAsync(filePath, ct);

            if (complexity == null || dependencies == null) return null;

            // Calculate quality metrics
            var maintainabilityIndex = Math.Max(0, 171 - 5.2 * Math.Log(complexity.LinesOfCode + 1)
                - 0.23 * complexity.CyclomaticComplexity - 16.2 * Math.Log(complexity.LinesOfCode + 1));

            var qualityScore = maintainabilityIndex;

            return new FileQuality
            {
                FilePath = filePath,
                QualityScore = qualityScore,
                Issues = new List<QualityIssue>(),
                Metrics = new List<QualityMetric>
                {
                    new QualityMetric { Name = "MaintainabilityIndex", Value = maintainabilityIndex, Unit = "index" },
                    new QualityMetric { Name = "ComplexityScore", Value = complexity.CyclomaticComplexity, Unit = "score" },
                    new QualityMetric { Name = "DependencyCount", Value = dependencies.InternalDependencies.Count + dependencies.ExternalDependencies.Count, Unit = "count" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing file quality for {File}", filePath);
        }

        return null;
    }

    private async Task<List<FileIssue>?> DetectFileIssuesAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var issues = new List<FileIssue>();
            var content = await File.ReadAllTextAsync(filePath, ct);
            var lines = content.Split('\n');

            // Check for common issues
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();

                // Long lines
                if (line.Length > 120)
                {
                    issues.Add(new FileIssue
                    {
                        Type = "Style",
                        Severity = "Warning",
                        Message = "Line exceeds 120 characters",
                        Location = new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = i + 1, Column = 120 }
                    });
                }

                // TODO comments
                if (line.Contains("TODO") || line.Contains("FIXME"))
                {
                    issues.Add(new FileIssue
                    {
                        Type = "Documentation",
                        Severity = "Info",
                        Message = "Unresolved TODO or FIXME comment",
                        Location = new MCPsharp.Models.Consolidated.Location
                        {
                            FilePath = filePath,
                            Line = i + 1,
                            Column = line.IndexOf("TODO") >= 0 ? line.IndexOf("TODO") : line.IndexOf("FIXME")
                        }
                    });
                }
            }

            return issues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting file issues for {File}", filePath);
        }

        return null;
    }

    private async Task<List<FileSuggestion>?> GenerateFileSuggestionsAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var suggestions = new List<FileSuggestion>();
            var complexity = await AnalyzeFileComplexityAsync(filePath, ct);
            var dependencies = await AnalyzeFileDependenciesAsync(filePath, ct);

            if (complexity != null && complexity.CyclomaticComplexity > 10)
            {
                suggestions.Add(new FileSuggestion
                {
                    Type = "Refactoring",
                    Description = "Consider refactoring to reduce cyclomatic complexity",
                    Location = new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 }
                });
            }

            if (dependencies != null && (dependencies.InternalDependencies.Count + dependencies.ExternalDependencies.Count) > 20)
            {
                suggestions.Add(new FileSuggestion
                {
                    Type = "Dependency",
                    Description = "Consider reducing the number of dependencies",
                    Location = new MCPsharp.Models.Consolidated.Location { FilePath = filePath, Line = 0, Column = 0 }
                });
            }

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating file suggestions for {File}", filePath);
        }

        return null;
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<ProjectStructure> GetProjectStructureAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var structure = new ProjectStructure
            {
                ProjectPath = projectPath,
                ProjectType = "Unknown",
                Configurations = new List<string>(),
                TargetFrameworks = new List<string>(),
                References = new List<ConsolidatedProjectReference>()
            };

            if (Directory.Exists(Path.GetDirectoryName(projectPath)))
            {

                // Add basic configurations
                structure.Configurations.Add("Debug");
                structure.Configurations.Add("Release");

                // Could parse project file for target frameworks, but simplified for now
                structure.TargetFrameworks.Add("Unknown");
            }

            return structure;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project structure for {Project}", projectPath);
        }

        return new ProjectStructure { ProjectPath = projectPath, ProjectType = "Unknown" };
    }

    private async Task<List<ProjectFile>> GetProjectFileInventoryAsync(string projectPath, ToolOptions? options, CancellationToken ct)
    {
        try
        {
            var files = new List<ProjectFile>();

                      // Get all C# files in the project directory
            var projectDir = Path.GetDirectoryName(projectPath);
            var filePaths = projectDir != null ? Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories) : Array.Empty<string>();

            foreach (var filePath in filePaths)
            {
                var fileInfo = new FileInfo(filePath);
                var basicInfo = await GetFileBasicInfoAsync(filePath, ct);

                if (basicInfo != null)
                {
                    files.Add(new ProjectFile
                    {
                        Path = filePath,
                        Type = Path.GetExtension(filePath).TrimStart('.'),
                        Language = basicInfo.Language,
                        Size = fileInfo.Length,
                        LineCount = basicInfo.LineCount,
                        LastModified = fileInfo.LastWriteTimeUtc
                    });
                }
            }

            return files.OrderBy(f => Path.GetFileName(f.Path)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting project file inventory for {Project}", projectPath);
        }

        return new List<ProjectFile>();
    }

    private async Task<ProjectDependencies?> AnalyzeProjectDependenciesAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var dependencies = new ProjectDependencies
            {
                PackageReferences = new List<PackageDependency>(),
                ProjectReferences = new List<ConsolidatedProjectReference>(),
                AssemblyReferences = new List<AssemblyReference>()
            };

            if (File.Exists(projectPath) && Path.GetExtension(projectPath).ToLowerInvariant() == ".csproj")
            {
                var content = await File.ReadAllTextAsync(projectPath, ct);

                // Extract NuGet package references
                var packagePattern = @"<PackageReference\s+Include=""([^""]+)""";
                var packageMatches = System.Text.RegularExpressions.Regex.Matches(content, packagePattern);
                dependencies.PackageReferences.AddRange(packageMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => new PackageDependency { Name = m.Groups[1].Value, Version = "Unknown", IsDevelopmentDependency = false, UsedBy = new List<string>() }));

                // Extract project references
                var projectRefPattern = @"<ProjectReference\s+Include=""([^""]+)""";
                var projectRefMatches = System.Text.RegularExpressions.Regex.Matches(content, projectRefPattern);
                dependencies.ProjectReferences.AddRange(projectRefMatches.Cast<System.Text.RegularExpressions.Match>()
                    .Select(m => new ConsolidatedProjectReference { Name = Path.GetFileNameWithoutExtension(m.Groups[1].Value), ReferenceType = "Project", Path = m.Groups[1].Value }));
            }

            return dependencies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project dependencies for {Project}", projectPath);
        }

        return null;
    }

    private async Task<ProjectBuildInfo?> AnalyzeProjectBuildAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var buildInfo = new ProjectBuildInfo
            {
                BuildSystem = "MSBuild",
                BuildConfigurations = new List<string>(),
                BuildSteps = new List<BuildStep>(),
                BuildArtifacts = new List<string>()
            };

            if (File.Exists(projectPath) && Path.GetExtension(projectPath).ToLowerInvariant() == ".csproj")
            {
                var content = await File.ReadAllTextAsync(projectPath, ct);

                // Extract target framework
                var tfPattern = @"<TargetFramework>([^<]+)</TargetFramework>";
                var tfMatch = System.Text.RegularExpressions.Regex.Match(content, tfPattern);
                if (tfMatch.Success)
                    buildInfo.BuildConfigurations.Add($"TargetFramework: {tfMatch.Groups[1].Value}");

                // Extract output path as build artifact
                var outputPathPattern = @"<OutputPath>([^<]+)</OutputPath>";
                var outputPathMatch = System.Text.RegularExpressions.Regex.Match(content, outputPathPattern);
                if (outputPathMatch.Success)
                    buildInfo.BuildArtifacts.Add(outputPathMatch.Groups[1].Value);

                // Add basic build configurations
                buildInfo.BuildConfigurations.Add("Debug");
                buildInfo.BuildConfigurations.Add("Release");

                // Add a basic build step
                buildInfo.BuildSteps.Add(new BuildStep
                {
                    Name = "Build",
                    Command = "dotnet build",
                    Inputs = new List<string> { projectPath },
                    Outputs = new List<string>()
                });
            }

            return buildInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing project build info for {Project}", projectPath);
        }

        return null;
    }

    private async Task<ProjectMetrics?> CalculateProjectMetricsAsync(string projectPath, CancellationToken ct)
    {
        try
        {
            var files = await GetProjectFileInventoryAsync(projectPath, null, ct);

            var csharpFiles = files.Where(f => f.Language == "C#").ToList();
            var totalLines = csharpFiles.Sum(f => f.LineCount);

            return new ProjectMetrics
            {
                TotalFiles = files.Count,
                LinesOfCode = totalLines,
                TypesCount = 0, // Would need more detailed analysis
                MethodsCount = 0, // Would need more detailed analysis
                AverageComplexity = 0, // Would need complexity analysis
                FileMetrics = csharpFiles.Select(f => new FileMetric
                {
                    FilePath = f.Path,
                    LinesOfCode = f.LineCount,
                    Complexity = 1.0, // Simplified
                    QualityScore = 100.0 // Simplified
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating project metrics for {Project}", projectPath);
        }

        return null;
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<MCPsharp.Models.Consolidated.ArchitectureDefinition> GetDefaultArchitectureDefinitionAsync(CancellationToken ct)
    {
        try
        {
            // Default layered architecture definition
            return new MCPsharp.Models.Consolidated.ArchitectureDefinition
            {
                Name = "Default Layered Architecture",
                Layers = new List<MCPsharp.Models.Consolidated.ArchitectureLayer>
                {
                    new() { Name = "Presentation", Level = 1, NamespacePatterns = new List<string> { "*.Presentation", "*.UI", "*.Web" }, Types = new List<string> { "Controller", "ViewModel", "View" } },
                    new() { Name = "Application", Level = 2, NamespacePatterns = new List<string> { "*.Application", "*.Services" }, Types = new List<string> { "Service", "Handler", "UseCase" } },
                    new() { Name = "Domain", Level = 3, NamespacePatterns = new List<string> { "*.Domain", "*.Core" }, Types = new List<string> { "Entity", "Aggregate", "ValueObject" } },
                    new() { Name = "Infrastructure", Level = 4, NamespacePatterns = new List<string> { "*.Infrastructure", "*.Data" }, Types = new List<string> { "Repository", "DbContext", "Service" } }
                },
                Rules = new List<MCPsharp.Models.Consolidated.DependencyRule>
                {
                    new() { FromLayer = "Presentation", ToLayer = "Application", Allowed = true, Reason = "Presentation can depend on Application" },
                    new() { FromLayer = "Application", ToLayer = "Domain", Allowed = true, Reason = "Application can depend on Domain" },
                    new() { FromLayer = "Infrastructure", ToLayer = "Domain", Allowed = true, Reason = "Infrastructure can depend on Domain" }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting default architecture definition");
        }

        return new MCPsharp.Models.Consolidated.ArchitectureDefinition
            {
                Name = "Default Architecture",
                Layers = new List<MCPsharp.Models.Consolidated.ArchitectureLayer>(),
                Rules = new List<MCPsharp.Models.Consolidated.DependencyRule>()
            };
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<ArchitectureMetrics?> CalculateArchitectureMetricsAsync(string scope, MCPsharp.Models.Architecture.ArchitectureDefinition definition, CancellationToken ct)
    {
        try
        {
            // Basic metrics calculation
            var layerCount = definition.Layers.Count;

            return new ArchitectureMetrics
            {
                TotalLayers = layerCount,
                TotalViolations = 0, // Simplified - would need actual analysis
                Compliance = 100.0, // Simplified - would need actual analysis
                LayerMetrics = new Dictionary<string, int>() // Simplified layer metrics
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating architecture metrics for {Scope}", scope);
        }

        return null;
    }

    private async Task<DependencyGraph> BuildDependencyGraphAsync(string scope, int depth, CancellationToken ct)
    {
        try
        {
            var graph = new DependencyGraph
            {
                Nodes = new List<MCPsharp.Models.Consolidated.DependencyNode>(),
                Edges = new List<MCPsharp.Models.Consolidated.DependencyEdge>()
            };

            // Simplified dependency graph construction
            if (_symbolQuery != null && _workspace != null)
            {
                var allTypes = await _symbolQuery.GetAllTypesAsync();

                foreach (var type in allTypes.Take(50)) // Limit for performance
                {
                    var node = new MCPsharp.Models.Consolidated.DependencyNode
                    {
                        Id = type.Name,
                        Name = type.Name,
                        Type = "Class"
                    };
                    graph.Nodes.Add(node);

                    // Simplified dependency edges - SymbolResult doesn't have BaseType/Interfaces
                    // This would need to be implemented using Roslyn symbol analysis for full functionality
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

    private async Task<List<ConsolidatedCircularDependency>> DetectCircularDependenciesAsync(string scope, CancellationToken ct)
    {
        try
        {
            var circularDependencies = new List<ConsolidatedCircularDependency>();
            var graph = await BuildDependencyGraphAsync(scope, 10, ct);

            // Simple cycle detection (basic implementation)
            var visited = new HashSet<string>();
            var recursionStack = new HashSet<string>();

            foreach (var node in graph.Nodes)
            {
                if (HasCycle(node.Id, graph, visited, recursionStack, out var cycle))
                {
                    circularDependencies.Add(new ConsolidatedCircularDependency
                    {
                        Cycle = cycle,
                        Locations = new List<MCPsharp.Models.Consolidated.Location> { new MCPsharp.Models.Consolidated.Location { FilePath = "", Line = 0, Column = 0 } },
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

    private async Task<DependencyImpact?> AnalyzeDependencyImpactAsync(string scope, CancellationToken ct)
    {
        try
        {
            var graph = await BuildDependencyGraphAsync(scope, 10, ct);

            // Calculate impact metrics
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

    private async Task<List<CriticalPath>> AnalyzeCriticalPathsAsync(string scope, CancellationToken ct)
    {
        try
        {
            var criticalPaths = new List<CriticalPath>();
            var graph = await BuildDependencyGraphAsync(scope, 10, ct);

            // Find nodes with most dependencies (simplified critical path analysis)
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

                // Find connected nodes
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

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<DependencyMetrics?> CalculateDependencyMetricsAsync(DependencyGraph graph, CancellationToken ct)
    {
        try
        {
            var nodeCount = graph.Nodes.Count;
            var edgeCount = graph.Edges.Count;
            var density = nodeCount > 1 ? (double)edgeCount / (nodeCount * (nodeCount - 1)) : 0;

            return new DependencyMetrics
            {
                TotalNodes = nodeCount,
                TotalEdges = edgeCount,
                Density = density,
                Cycles = 0, // Simplified - would need cycle detection
                AveragePathLength = 2.5, // Simplified calculation
                NodeMetrics = graph.Nodes.Select(n => new NodeMetrics
                {
                    NodeId = n.Id,
                    InDegree = graph.Edges.Count(e => e.To == n.Id),
                    OutDegree = graph.Edges.Count(e => e.From == n.Id),
                    Centrality = 0.5, // Simplified
                    IsInCycle = false // Simplified
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating dependency metrics");
        }

        return null;
    }

    #region Helper Methods

    private static bool HasCycle(string nodeId, DependencyGraph graph, HashSet<string> visited, HashSet<string> recursionStack, out List<string> cycle)
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

    #endregion

    private async Task<ComplexityAnalysis?> AnalyzeComplexityAsync(string scope, CancellationToken ct)
    {
        try
        {
            // Get all C# files in the scope
            var files = Directory.GetFiles(scope, "*.cs", SearchOption.AllDirectories);
            var totalComplexity = 0;
            var fileComplexities = new List<FileComplexity>();

            foreach (var file in files.Take(20)) // Limit for performance
            {
                var complexity = await AnalyzeFileComplexityAsync(file, ct);
                if (complexity != null)
                {
                    totalComplexity += (int)complexity.CyclomaticComplexity;
                    fileComplexities.Add(complexity);
                }
            }

            var averageComplexity = fileComplexities.Any() ? (double)totalComplexity / fileComplexities.Count : 0;
            var maxComplexity = fileComplexities.Any() ? fileComplexities.Max(f => f.CyclomaticComplexity) : 0;

            return new ComplexityAnalysis
            {
                Scope = scope,
                TotalComplexity = totalComplexity,
                AverageComplexity = averageComplexity,
                MaxComplexity = maxComplexity,
                FileCount = fileComplexities.Count,
                ComplexFiles = fileComplexities.Where(f => f.CyclomaticComplexity > 10).Select(f => f.FilePath).ToList(),
                TotalMethods = fileComplexities.Sum(f => f.MethodComplexities?.Count ?? 0),
                ComplexMethods = fileComplexities.SelectMany(f => f.MethodComplexities ?? new List<MethodComplexity>())
                    .Where(m => m.CyclomaticComplexity > 10)
                    .Select(m => new ComplexityIssue
                    {
                        FilePath = m.Location.FilePath,
                        MethodName = m.MethodName,
                        Complexity = m.CyclomaticComplexity,
                        Location = m.Location
                    }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing complexity for {Scope}", scope);
        }

        return null;
    }

    private async Task<MaintainabilityAnalysis?> AnalyzeMaintainabilityAsync(string scope, CancellationToken ct)
    {
        try
        {
            var complexity = await AnalyzeComplexityAsync(scope, ct);
            if (complexity == null) return null;

            // Calculate maintainability index (simplified version)
            var averageMaintainability = Math.Max(0, 171 - 5.2 * Math.Log(complexity.AverageComplexity + 1) - 0.23 * complexity.AverageComplexity);

            return new MaintainabilityAnalysis
            {
                Scope = scope,
                MaintainabilityIndex = averageMaintainability,
                TechnicalDebt = Math.Max(0, 100 - averageMaintainability), // Simplified
                CodeQuality = averageMaintainability > 85 ? "Excellent" :
                             averageMaintainability > 70 ? "Good" :
                             averageMaintainability > 50 ? "Fair" : "Poor",
                RefactoringPriority = averageMaintainability < 60 ? 1.0 :
                                     averageMaintainability < 80 ? 0.5 : 0.1,
                AverageMaintainabilityIndex = averageMaintainability,
                Issues = new List<MaintainabilityIssue>() // Empty list for now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing maintainability for {Scope}", scope);
        }

        return null;
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<TestCoverageAnalysis?> AnalyzeTestCoverageAsync(string scope, CancellationToken ct)
    {
        try
        {
            var allFiles = Directory.GetFiles(scope, "*.cs", SearchOption.AllDirectories);
            var testFiles = allFiles.Where(f => Path.GetFileName(f).Contains("Test") ||
                                               Path.GetDirectoryName(f)?.Contains("Test") == true ||
                                               Path.GetDirectoryName(f)?.Contains("test") == true).ToList();

            var sourceFiles = allFiles.Except(testFiles).ToList();
            var coveragePercentage = sourceFiles.Any() ? (double)testFiles.Count / sourceFiles.Count * 100 : 0;

            return new TestCoverageAnalysis
            {
                Scope = scope,
                TotalFiles = allFiles.Length,
                TestFiles = testFiles.Count,
                SourceFiles = sourceFiles.Count,
                CoveragePercentage = coveragePercentage,
                CoverageLevel = coveragePercentage > 80 ? "Excellent" :
                               coveragePercentage > 60 ? "Good" :
                               coveragePercentage > 40 ? "Fair" : "Poor",
                OverallCoverage = coveragePercentage,
                Reports = new List<CoverageReport>() // Empty list for now
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing test coverage for {Scope}", scope);
        }

        return null;
    }

    private async Task<List<CodeSmell>> DetectCodeSmellsAsync(string scope, CancellationToken ct)
    {
        try
        {
            var codeSmells = new List<CodeSmell>();
            var files = Directory.GetFiles(scope, "*.cs", SearchOption.AllDirectories);

            foreach (var file in files.Take(10)) // Limit for performance
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var lines = content.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();

                    // Long method detection
                    if (line.Contains("public") || line.Contains("private"))
                    {
                        var methodLines = CountMethodLines(content, i);
                        if (methodLines > 50)
                        {
                            codeSmells.Add(new CodeSmell
                            {
                                Type = "Long Method",
                                Severity = "Warning",
                                Location = new ConsolidatedLocation
                                {
                                    FilePath = file,
                                    Line = i + 1,
                                    Column = 0
                                },
                                Description = $"Method exceeds 50 lines ({methodLines} lines)",
                                Suggestion = "Consider breaking this method into smaller methods"
                            });
                        }
                    }

                    // Large class detection
                    if (line.Contains("class "))
                    {
                        var classLines = CountClassLines(content, i);
                        if (classLines > 300)
                        {
                            codeSmells.Add(new CodeSmell
                            {
                                Type = "Large Class",
                                Severity = "Warning",
                                Location = new ConsolidatedLocation
                                {
                                    FilePath = file,
                                    Line = i + 1,
                                    Column = 0
                                },
                                Description = $"Class exceeds 300 lines ({classLines} lines)",
                                Suggestion = "Consider splitting this class into smaller classes"
                            });
                        }
                    }
                }
            }

            return codeSmells;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting code smells for {Scope}", scope);
        }

        return new List<CodeSmell>();
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<QualityScore?> CalculateQualityScoreAsync(QualityAnalysisResponse analysis, CancellationToken ct)
    {
        try
        {
            var score = 100.0;

            // Deduct points for duplicates
            if (analysis.Duplicates?.Count > 0)
                score -= analysis.Duplicates.Count * 2;

            // Deduct points for code smells
            if (analysis.CodeSmells?.Count > 0)
                score -= analysis.CodeSmells.Count * 3;

            // Factor in maintainability
            if (analysis.Maintainability?.MaintainabilityIndex > 0)
                score = Math.Min(score, analysis.Maintainability.MaintainabilityIndex);

            // Factor in test coverage
            if (analysis.TestCoverage?.CoveragePercentage > 0)
                score = score * (analysis.TestCoverage.CoveragePercentage / 100.0);

            score = Math.Max(0, Math.Min(100, score));

            return new QualityScore
            {
                OverallScore = score,
                CategoryScores = new Dictionary<string, double>
                {
                    ["Complexity"] = analysis.Complexity?.AverageComplexity > 0 ? Math.Max(0, 100 - analysis.Complexity.AverageComplexity * 2) : 100,
                    ["Maintainability"] = analysis.Maintainability?.MaintainabilityIndex ?? 100,
                    ["TestCoverage"] = analysis.TestCoverage?.CoveragePercentage ?? 0,
                    ["CodeSmells"] = Math.Max(0, 100 - (analysis.CodeSmells?.Count ?? 0) * 5)
                },
                Grade = score >= 90 ? "A" :
                        score >= 80 ? "B" :
                        score >= 70 ? "C" :
                        score >= 60 ? "D" : "F"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating quality score");
        }

        return null;
    }

    #pragma warning disable CS1998 // Async method lacks await (synchronous implementation)
    private async Task<List<QualityRecommendation>> GenerateQualityRecommendationsAsync(QualityAnalysisResponse analysis, CancellationToken ct)
    {
        try
        {
            var recommendations = new List<QualityRecommendation>();

            // Recommendations based on duplicates
            if (analysis.Duplicates?.Count > 0)
            {
                recommendations.Add(new QualityRecommendation
                {
                    Type = "Refactoring",
                    Description = $"Found {analysis.Duplicates.Count} instances of duplicate code",
                    Priority = 0.5, // Medium priority
                    Impact = 0.7, // Medium-high impact
                    Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                });
            }

            // Recommendations based on code smells
            if (analysis.CodeSmells?.Count > 0)
            {
                var codeSmellRecs = analysis.CodeSmells
                    .GroupBy(cs => cs.Type)
                    .Select(g => new QualityRecommendation
                    {
                        Type = "Code Quality",
                        Description = $"Found {g.Count()} instances of {g.Key}",
                        Priority = g.Count() > 5 ? 0.8 : 0.5, // High/Medium priority
                        Impact = 0.6, // Medium impact
                        Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                    });
                recommendations.AddRange(codeSmellRecs);
            }

            // Recommendations based on test coverage
            if (analysis.TestCoverage?.CoveragePercentage < 80)
            {
                recommendations.Add(new QualityRecommendation
                {
                    Type = "Testing",
                    Description = $"Current coverage: {analysis.TestCoverage.CoveragePercentage:F1}%",
                    Priority = analysis.TestCoverage.CoveragePercentage < 50 ? 0.8 : 0.5, // High/Medium priority
                    Impact = 0.7, // High impact
                    Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                });
            }

            // Recommendations based on maintainability
            if (analysis.Maintainability?.MaintainabilityIndex < 70)
            {
                recommendations.Add(new QualityRecommendation
                {
                    Type = "Architecture",
                    Description = $"Maintainability index: {analysis.Maintainability.MaintainabilityIndex:F1}",
                    Priority = 0.8, // High priority
                    Impact = 0.8, // High impact
                    Locations = new List<MCPsharp.Models.Consolidated.Location>() // Empty list for now
                });
            }

            return recommendations.OrderByDescending(r => r.Priority).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating quality recommendations");
        }

        return new List<QualityRecommendation>();
    }

    #region Helper Methods

    private static int CountMethodLines(string content, int startIndex)
    {
        var lines = content.Split('\n');
        var braceCount = 0;
        var methodLines = 0;
        var inMethod = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            methodLines++;

            foreach (var c in line)
            {
                if (c == '{')
                {
                    braceCount++;
                    inMethod = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0 && inMethod)
                    {
                        return methodLines;
                    }
                }
            }
        }

        return methodLines;
    }

    private static int CountClassLines(string content, int startIndex)
    {
        var lines = content.Split('\n');
        var braceCount = 0;
        var classLines = 0;
        var inClass = false;

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];
            classLines++;

            foreach (var c in line)
            {
                if (c == '{')
                {
                    braceCount++;
                    inClass = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (braceCount == 0 && inClass)
                    {
                        return classLines;
                    }
                }
            }
        }

        return classLines;
    }

    #endregion

    #region Additional Helper Methods

    private static string ExtractMethodContent(string content, int startIndex)
    {
        var braceCount = 0;
        var inMethod = false;
        var startIdx = startIndex;
        var endIdx = startIndex;

        for (int i = startIndex; i < content.Length; i++)
        {
            if (content[i] == '{')
            {
                if (!inMethod)
                {
                    inMethod = true;
                    startIdx = i;
                }
                braceCount++;
            }
            else if (content[i] == '}')
            {
                braceCount--;
                if (braceCount == 0 && inMethod)
                {
                    endIdx = i + 1;
                    break;
                }
            }
        }

        return content.Substring(startIdx, endIdx - startIdx);
    }

    private static int CalculateCyclomaticComplexity(string methodContent)
    {
        var complexity = 1; // Base complexity

        // Count decision points
        var decisionKeywords = new[] { "if", "else", "while", "for", "foreach", "switch", "case", "&&", "||", "?:" };
        foreach (var keyword in decisionKeywords)
        {
            complexity += System.Text.RegularExpressions.Regex.Matches(methodContent, $@"\b{keyword}\b").Count;
        }

        return complexity;
    }

    #endregion

    #endregion

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

    private static MCPsharp.Services.Roslyn.CircularDependency ConvertToCircularDependency(ConsolidatedCircularDependency consolidated)
    {
        return new MCPsharp.Services.Roslyn.CircularDependency
        {
            Methods = new List<MCPsharp.Models.Roslyn.MethodSignature>(), // Empty list for conversion
            CycleLength = consolidated.Length,
            Steps = new List<MCPsharp.Models.Roslyn.CallChainStep>(), // Skip complex conversion for now
            Confidence = MCPsharp.Models.Roslyn.ConfidenceLevel.Medium,
            FilesInvolved = consolidated.Locations.Select(l => l.FilePath).Distinct().ToList()
        };
    }

    #endregion
}
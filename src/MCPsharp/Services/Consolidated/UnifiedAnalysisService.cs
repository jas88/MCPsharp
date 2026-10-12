using Microsoft.Extensions.Logging;
using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Roslyn;

namespace MCPsharp.Services.Consolidated;

/// <summary>
/// Unified analysis service consolidating symbol, type, file, project, architecture, dependency, and quality analysis
/// Phase 2: Consolidate 70 analysis tools into 15 unified analysis tools
/// </summary>
public partial class UnifiedAnalysisService
{
    private readonly RoslynWorkspace? _workspace;
    private readonly SymbolQueryService? _symbolQuery;
    private readonly ClassStructureService? _classStructure;
    private readonly ReferenceFinderService? _referenceFinder;
    private readonly IArchitectureValidatorService? _architectureValidator;
    private readonly IDuplicateCodeDetectorService? _duplicateDetector;
    private readonly ILogger<UnifiedAnalysisService> _logger;

    public UnifiedAnalysisService(
        RoslynWorkspace? workspace = null,
        SymbolQueryService? symbolQuery = null,
        ClassStructureService? classStructure = null,
        ReferenceFinderService? referenceFinder = null,
        IArchitectureValidatorService? architectureValidator = null,
        IDuplicateCodeDetectorService? duplicateDetector = null,
        ILogger<UnifiedAnalysisService>? logger = null)
    {
        _workspace = workspace;
        _symbolQuery = symbolQuery;
        _classStructure = classStructure;
        _referenceFinder = referenceFinder;
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
                response.Definition = await GetSymbolDefinitionAsync(request.SymbolName, request.Context);
            }

            // References
            if (request.Options?.Include.HasFlag(IncludeOptions.Dependencies) == true || request.Scope == AnalysisScope.References || request.Scope == AnalysisScope.All)
            {
                response.References = await GetSymbolReferencesAsync(request.SymbolName, request.Context);
            }

            // Callers
            if (request.Options?.Include.HasFlag(IncludeOptions.Dependencies) == true && request.Scope == AnalysisScope.All)
            {
                response.Callers = await GetSymbolCallersAsync(request.SymbolName, request.Context);
            }

            // Usage patterns
            if (request.Options?.Detail >= DetailLevel.Detailed)
            {
                response.UsagePatterns = await AnalyzeSymbolUsageAsync(request.SymbolName, request.Context);
            }

            // Related symbols
            if (request.Options?.Include.HasFlag(IncludeOptions.Suggestions) == true)
            {
                response.RelatedSymbols = await FindRelatedSymbolsAsync(request.SymbolName, request.Context);
            }

            // Metrics
            if (request.Options?.Include.HasFlag(IncludeOptions.Metrics) == true)
            {
                response.Metrics = await CalculateSymbolMetricsAsync(request.SymbolName, request.Context);
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
            LogErrorAnalyzingSymbol(_logger, ex, request.SymbolName);

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
                response.Structure = await GetTypeStructureAsync(request.TypeName, request.Context);
            }

            // Inheritance hierarchy
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                response.Inheritance = await GetTypeInheritanceAsync(request.TypeName, request.Context);
            }

            // Usage analysis
            if (request.Scope == AnalysisScope.References || request.Scope == AnalysisScope.All)
            {
                response.Usages = await GetTypeUsagesAsync(request.TypeName);
            }

            // Implementations
            if (request.Options?.Include.HasFlag(IncludeOptions.Dependencies) == true)
            {
                response.Implementations = await GetTypeImplementationsAsync(request.TypeName, request.Context);
            }

            // Members analysis
            if (request.Options?.Detail >= DetailLevel.Detailed)
            {
                response.Members = await AnalyzeTypeMembersAsync(request.TypeName, request.Context);
            }

            // Type metrics
            if (request.Options?.Include.HasFlag(IncludeOptions.Metrics) == true)
            {
                response.Metrics = await CalculateTypeMetricsAsync(request.TypeName, request.Context);
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
            LogErrorAnalyzingType(_logger, ex, request.TypeName);

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
            if (request.Options?.Include.HasFlag(IncludeOptions.Suggestions) == true)
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
            LogErrorAnalyzingFile(_logger, ex, request.FilePath);

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
            var response = new ProjectAnalysisResponse
            {
                ProjectPath = request.ProjectPath,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Metadata = new ResponseMetadata { Success = true, ProcessingTime = stopwatch.Elapsed }
            };

            // Basic project structure
            response.Structure = GetProjectStructure(request.ProjectPath);

            // File inventory
            if (request.Options?.Detail >= DetailLevel.Standard)
            {
                response.Files = await GetProjectFileInventoryAsync(request.ProjectPath, ct);
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
            if (request.Options?.Include.HasFlag(IncludeOptions.Metrics) == true)
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
            LogErrorAnalyzingProject(_logger, ex, request.ProjectPath);

            return new ProjectAnalysisResponse
            {
                ProjectPath = request.ProjectPath,
                Error = ex.Message,
                Metadata = new ResponseMetadata { ProcessingTime = stopwatch.Elapsed, Success = false }
            };
        }
    }

    private const string ProjectPathRequiredError = "A project path is required for this analysis";

    /// <summary>
    /// C# files to analyse for a target that is either a single file or a directory searched recursively.
    /// </summary>
    private static List<string> EnumerateCSharpFiles(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            return string.Equals(Path.GetExtension(targetPath), ".cs", StringComparison.OrdinalIgnoreCase)
                ? new List<string> { targetPath }
                : new List<string>();
        }

        return Directory.Exists(targetPath)
            ? Directory.GetFiles(targetPath, "*.cs", SearchOption.AllDirectories).ToList()
            : throw new DirectoryNotFoundException($"Analysis target not found: {targetPath}");
    }

    /// <summary>
    /// Directory of a project given either as its directory or as a project/solution file.
    /// </summary>
    private static string? ResolveProjectDirectory(string projectPath) =>
        Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(projectPath);

    /// <summary>
    /// Project file for a project given either as a .csproj file or as a directory containing one.
    /// </summary>
    private static string? ResolveProjectFile(string projectPath)
    {
        if (File.Exists(projectPath))
        {
            return string.Equals(Path.GetExtension(projectPath), ".csproj", StringComparison.OrdinalIgnoreCase) ? projectPath : null;
        }

        return Directory.Exists(projectPath)
            ? Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

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
}

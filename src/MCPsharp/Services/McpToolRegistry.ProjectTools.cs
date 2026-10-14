using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services.Phase3;
using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCPsharp.Services;

/// <summary>
/// Project open/info tools and project-scoped service initialisation
/// </summary>
public partial class McpToolRegistry
{
    private async Task EnsureWorkspaceInitializedAsync()
    {
        if (_workspace != null && _projectContext.GetProjectContext() != null)
        {
            var context = _projectContext.GetProjectContext();
            if (context == null)
                throw new InvalidOperationException("Project context is not available");
            if (string.IsNullOrEmpty(context.RootPath))
                throw new InvalidOperationException("Project root path is not available");

            if (!_workspace.IsInitialized)
            {
                await _workspace.InitializeAsync(context.RootPath);
                InitializeProjectServices(context.RootPath);
            }
            else if (_symbolQuery == null)
            {
                // Workspace loaded outside the registry (e.g. auto-loaded at startup)
                InitializeProjectServices(context.RootPath);
            }
        }
    }

    /// <summary>
    /// Create the project-scoped services for the project open at <paramref name="rootPath"/>:
    /// file operations, the Roslyn-backed services when a workspace is available, and the
    /// consolidated services re-rooted on the project.
    /// </summary>
    private void InitializeProjectServices(string rootPath)
    {
        _fileOperations = _workspace != null
            ? new FileOperationsService(rootPath, _workspace)
            : new FileOperationsService(rootPath);

        if (_workspace != null)
        {
            var loggerFactory = _loggerFactory ?? NullLoggerFactory.Instance;
            _symbolQuery = new SymbolQueryService(_workspace);
            _classStructure = new ClassStructureService(_workspace);
            _semanticEdit = new SemanticEditService(_workspace, _classStructure);
            _referenceFinder = new ReferenceFinderService(_workspace);

            // Hybrid search service with graceful degradation
            _hybridSearch = new HybridSearchService(_workspace, _symbolQuery, loggerFactory.CreateLogger<HybridSearchService>());

            // Advanced reverse search services
            _callerAnalysis = new CallerAnalysisService(_workspace, _symbolQuery);
            _callChain = new CallChainService(_workspace, _symbolQuery, _callerAnalysis);
            _typeUsage = new TypeUsageService(_workspace, _symbolQuery);
            _advancedReferenceFinder = new AdvancedReferenceFinderService(_workspace, _symbolQuery, _callerAnalysis, _callChain, _typeUsage);
            _renameSymbol = new RenameSymbolService(_workspace, _advancedReferenceFinder, _symbolQuery,
                loggerFactory.CreateLogger<RenameSymbolService>());

            // Phase 3 services
            _architectureValidator = new ArchitectureValidatorService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage);
            _duplicateCodeDetector = new DuplicateCodeDetectorService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage,
                loggerFactory.CreateLogger<DuplicateCodeDetectorService>());
            if (!_largeFileOptimizerSupplied)
            {
                _largeFileOptimizer = new LargeFileOptimizerService(_workspace, _symbolQuery, _callerAnalysis, _typeUsage,
                    loggerFactory.CreateLogger<LargeFileOptimizerService>());
            }
        }

        RebindConsolidatedServices();
    }

    private async Task<ToolCallResult> ExecuteProjectOpen(JsonDocument arguments)
    {
        var path = arguments.RootElement.GetProperty("path").GetString();
        if (string.IsNullOrEmpty(path))
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "Path is required"
            };
        }

        try
        {
            _projectContext.OpenProject(path);

            if (_workspace != null)
            {
                await _workspace.InitializeAsync(path);
            }

            InitializeProjectServices(path);

            var context = _projectContext.GetProjectContext();
            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    Path = context?.RootPath ?? path,
                    Name = System.IO.Path.GetFileName(path)
                }
            };
        }
        catch (Exception ex)
        {
            LogToolOperationFailed(_logger, ex, nameof(ExecuteProjectOpen));
            return new ToolCallResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    private ToolCallResult ExecuteProjectInfo()
    {
        var info = _projectContext.GetProjectInfo();
        if (info == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "No project is currently open"
            };
        }

        return new ToolCallResult
        {
            Success = true,
            Result = info
        };
    }
}

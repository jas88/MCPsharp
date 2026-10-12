using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Dispatch table mapping tool names to their handlers
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Handlers for every tool the registry can execute, keyed by tool name.
    /// </summary>
    private Dictionary<string, Func<JsonDocument, CancellationToken, Task<ToolCallResult>>> CreateToolHandlers() => new(StringComparer.Ordinal)
    {
        ["project_open"] = (args, _) => ExecuteProjectOpen(args),
        ["project_info"] = (_, _) => Task.FromResult(ExecuteProjectInfo()),
        ["file_list"] = (args, _) => Task.FromResult(ExecuteFileList(args)),
        ["file_read"] = (args, ct) => ExecuteFileRead(args, ct),
        ["file_write"] = (args, ct) => ExecuteFileWrite(args, ct),
        ["file_edit"] = (args, ct) => ExecuteFileEdit(args, ct),
        ["search_text"] = (args, ct) => ExecuteSearchText(args, ct),
        ["find_symbol"] = (args, _) => ExecuteFindSymbol(args),
        ["get_symbol_info"] = (args, _) => ExecuteGetSymbolInfo(args),
        ["get_class_structure"] = (args, _) => ExecuteGetClassStructure(args),
        ["add_class_property"] = (args, _) => ExecuteAddClassProperty(args),
        ["add_class_method"] = (args, _) => ExecuteAddClassMethod(args),
        ["find_references"] = (args, _) => ExecuteFindReferences(args),
        ["find_implementations"] = (args, _) => ExecuteFindImplementations(args),
        ["parse_project"] = (args, _) => ExecuteParseProject(args),
        ["rename_symbol"] = (args, ct) => ExecuteRenameSymbol(args, ct),
        // Advanced reverse search tools
        ["find_callers"] = (args, _) => ExecuteFindCallers(args),
        ["find_call_chains"] = (args, _) => ExecuteFindCallChains(args),
        ["find_type_usages"] = (args, _) => ExecuteFindTypeUsages(args),
        ["analyze_call_patterns"] = (args, _) => ExecuteAnalyzeCallPatterns(args),
        ["analyze_inheritance"] = (args, _) => ExecuteAnalyzeInheritance(args),
        ["find_circular_dependencies"] = (args, _) => ExecuteFindCircularDependencies(args),
        ["find_unused_methods"] = (args, _) => ExecuteFindUnusedMethods(args),
        ["analyze_call_graph"] = (args, _) => ExecuteAnalyzeCallGraph(args),
        ["find_recursive_calls"] = (args, _) => ExecuteFindRecursiveCalls(args),
        ["analyze_type_dependencies"] = (args, _) => ExecuteAnalyzeTypeDependencies(args),
        // AI-powered tools
        ["ask_codebase"] = (args, _) => HandleAskCodebaseAsync(args),
        ["ai_suggest_fix"] = (args, _) => HandleAISuggestFixAsync(args),
        ["ai_refactor"] = (args, _) => HandleAIRefactorAsync(args),
        ["ai_implement_feature"] = (args, _) => HandleAIImplementFeatureAsync(args),
        // Phase 2 tools
        ["get_workflows"] = (args, _) => ExecuteGetWorkflows(args),
        ["parse_workflow"] = (args, _) => ExecuteParseWorkflow(args),
        ["validate_workflow_consistency"] = (args, _) => ExecuteValidateWorkflowConsistency(args),
        ["get_config_schema"] = (args, _) => ExecuteGetConfigSchema(args),
        ["merge_configs"] = (args, _) => ExecuteMergeConfigs(args),
        ["analyze_impact"] = (args, _) => ExecuteAnalyzeImpact(args),
        ["trace_feature"] = (args, _) => ExecuteTraceFeature(args),
        // Bulk edit tools
        ["bulk_replace"] = (args, ct) => WithProjectPaths(args, BulkReplaceFilesArgument, a => ExecuteBulkReplace(a, ct)),
        ["conditional_edit"] = (args, ct) => WithProjectPaths(args, BulkFilesArgument, a => ExecuteConditionalEdit(a, ct)),
        ["batch_refactor"] = (args, ct) => WithProjectPaths(args, BulkFilesArgument, a => ExecuteBatchRefactor(a, ct)),
        ["multi_file_edit"] = (args, ct) => WithProjectPaths(args, MultiFileOperationsArgument, a => ExecuteMultiFileEdit(a, ct)),
        ["preview_bulk_changes"] = (args, ct) => WithProjectPaths(args, BulkFilesAndOperationsArguments, a => ExecutePreviewBulkChanges(a, ct)),
        ["rollback_bulk_edit"] = (args, ct) => ExecuteRollbackBulkEdit(args, ct),
        ["validate_bulk_edit"] = (args, _) => WithProjectPaths(args, BulkFilesArgument, a => ExecuteValidateBulkEdit(a)),
        ["get_available_rollbacks"] = (_, ct) => ExecuteGetAvailableRollbacks(ct),
        ["estimate_bulk_impact"] = (args, ct) => WithProjectPaths(args, BulkFilesArgument, a => ExecuteEstimateBulkImpact(a)),
        ["get_bulk_file_statistics"] = (args, _) => WithProjectPaths(args, BulkFilesArgument, a => ExecuteGetBulkFileStatistics(a)),
        // Streaming processor tools
        ["stream_process_file"] = (args, ct) => WithProjectPaths(args, StreamFileArguments, a => ExecuteStreamProcessFile(a, ct)),
        ["bulk_transform"] = (args, ct) => WithProjectPaths(args, BulkTransformArguments, a => ExecuteBulkTransform(a, ct)),
        ["get_stream_progress"] = (args, _) => ExecuteGetStreamProgress(args),
        ["cancel_stream_operation"] = (args, _) => ExecuteCancelStreamOperation(args),
        ["resume_stream_operation"] = (args, ct) => ExecuteResumeStreamOperation(args, ct),
        ["list_stream_operations"] = (args, _) => ExecuteListStreamOperations(args),
        ["cleanup_stream_operations"] = (args, _) => ExecuteCleanupStreamOperations(args),
        ["get_available_processors"] = (_, _) => ExecuteGetAvailableProcessors(),
        ["estimate_stream_processing"] = (args, _) => WithProjectPaths(args, StreamEstimateArguments, a => ExecuteEstimateStreamProcessing(a)),
        // Phase 3 architecture validation tools
        ["validate_architecture"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteValidateArchitecture(a, ct)),
        ["detect_layer_violations"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteDetectLayerViolations(a, ct)),
        ["get_architecture_report"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteGetArchitectureReport(a, ct)),
        ["define_custom_architecture"] = (args, ct) => ExecuteDefineCustomArchitecture(args, ct),
        ["analyze_circular_dependencies"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteAnalyzeCircularDependencies(a, ct)),
        ["generate_architecture_diagram"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteGenerateArchitectureDiagram(a, ct)),
        ["get_architecture_recommendations"] = (args, ct) => ExecuteGetArchitectureRecommendations(args, ct),
        ["check_type_compliance"] = (args, ct) => ExecuteCheckTypeCompliance(args, ct),
        ["get_predefined_architectures"] = (_, ct) => ExecuteGetPredefinedArchitectures(ct),
        // Duplicate Code Detection Tools
        ["detect_duplicates"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteDetectDuplicates(a, ct)),
        ["find_exact_duplicates"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteFindExactDuplicates(a, ct)),
        ["find_near_duplicates"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteFindNearDuplicates(a, ct)),
        ["analyze_duplication_metrics"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteAnalyzeDuplicationMetrics(a, ct)),
        ["get_refactoring_suggestions"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteGetRefactoringSuggestions(a, ct)),
        ["get_duplicate_hotspots"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteGetDuplicateHotspots(a, ct)),
        ["compare_code_blocks"] = (args, ct) => ExecuteCompareCodeBlocks(args, ct),
        ["validate_refactoring"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteValidateRefactoring(a, ct)),
        // Roslyn Analyzer Tools
        ["load_roslyn_analyzers"] = (args, ct) => ExecuteLoadRoslynAnalyzers(args, ct),
        ["run_roslyn_analyzers"] = (args, ct) => ExecuteRunRoslynAnalyzers(args, ct),
        ["list_roslyn_analyzers"] = (_, _) => Task.FromResult(ExecuteListRoslynAnalyzers()),
        // Automated Code Fix Tools
        ["code_quality_analyze"] = (args, ct) => ExecuteCodeQualityAnalyze(args, ct),
        ["code_quality_fix"] = (args, ct) => ExecuteCodeQualityFix(args, ct),
        ["code_quality_profiles"] = (_, _) => ExecuteCodeQualityProfiles(),
        // Roslyn Code Fix and Configuration Tools
        ["apply_roslyn_fixes"] = (args, ct) => WithProjectPaths(args, RoslynFixArguments, a => ExecuteApplyRoslynFixes(a, ct)),
        ["configure_analyzer"] = (args, ct) => ExecuteConfigureAnalyzer(args, ct),
        ["get_analyzer_config"] = (_, _) => ExecuteGetAnalyzerConfig(),
        ["reset_analyzer_config"] = (_, ct) => ExecuteResetAnalyzerConfig(ct),
        ["generate_analysis_report"] = (args, ct) => WithProjectPaths(args, AnalysisReportArguments, a => ExecuteGenerateAnalysisReport(a, ct)),
        // SQL Migration Analyzer Tools (Phase 3)
        ["analyze_migrations"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteAnalyzeMigrations(a, ct)),
        ["detect_breaking_changes"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteDetectBreakingChanges(a, ct)),
        ["get_migration_history"] = (args, ct) => WithProjectPaths(args, MigrationHistoryArguments, a => ExecuteGetMigrationHistory(a, ct)),
        ["get_migration_dependencies"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteGetMigrationDependencies(a, ct)),
        ["generate_migration_report"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteGenerateMigrationReport(a, ct)),
        ["validate_migrations"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteValidateMigrations(a, ct)),
        // Large File Optimization Tools (Phase 3)
        ["analyze_large_files"] = (args, ct) => WithProjectPaths(args, ProjectPathArgument, a => ExecuteAnalyzeLargeFiles(a, ct)),
        ["optimize_large_class"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteOptimizeLargeClass(a, ct)),
        ["optimize_large_method"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteOptimizeLargeMethod(a, ct)),
        ["get_complexity_metrics"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteGetComplexityMetrics(a, ct)),
        ["generate_optimization_plan"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteGenerateOptimizationPlan(a, ct)),
        ["detect_god_classes"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteDetectGodClasses(a, ct)),
        ["detect_god_methods"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteDetectGodMethods(a, ct)),
        ["analyze_code_smells"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteAnalyzeCodeSmells(a, ct)),
        // Refactoring tools
        ["extract_method"] = (args, ct) => ExecuteExtractMethod(args, ct),
        ["get_optimization_recommendations"] = (args, ct) => WithProjectPaths(args, FilePathArguments, a => ExecuteGetOptimizationRecommendations(a, ct)),
        // Universal File Operations (consolidated)
        ["get_file_info"] = (args, ct) => ExecuteGetFileInfo(args, ct),
        ["get_file_content"] = (args, ct) => ExecuteGetFileContent(args, ct),
        ["execute_file_operation"] = (args, ct) => ExecuteFileOperation(args, ct),
        ["execute_batch"] = (args, ct) => ExecuteBatch(args, ct),
        // Unified Analysis (consolidated)
        ["analyze_symbol"] = (args, ct) => ExecuteAnalyzeSymbol(args, ct),
        ["analyze_type"] = (args, ct) => ExecuteAnalyzeType(args, ct),
        ["analyze_file"] = (args, ct) => ExecuteAnalyzeFile(args, ct),
        ["analyze_project"] = (args, ct) => ExecuteAnalyzeProject(args, ct),
        ["analyze_architecture"] = (args, ct) => ExecuteAnalyzeArchitecture(args, ct),
        ["analyze_dependencies"] = (args, ct) => ExecuteAnalyzeDependenciesUnified(args, ct),
        ["analyze_quality"] = (args, ct) => ExecuteAnalyzeQuality(args, ct),
        // Bulk Operations Hub (consolidated)
        ["execute_bulk_operation"] = (args, ct) => ExecuteBulkOperation(args, ct),
        ["preview_bulk_operation"] = (args, ct) => ExecutePreviewBulkOperation(args, ct),
        ["get_bulk_progress"] = (args, ct) => ExecuteGetBulkProgress(args, ct),
        ["manage_bulk_operation"] = (args, ct) => ExecuteManageBulkOperation(args, ct),
        // Stream Processing Controller (consolidated)
        ["process_stream"] = (args, ct) => ExecuteProcessStream(args, ct),
        ["monitor_stream"] = (args, ct) => ExecuteMonitorStream(args, ct),
        ["manage_stream"] = (args, ct) => ExecuteManageStream(args, ct),
    };
}

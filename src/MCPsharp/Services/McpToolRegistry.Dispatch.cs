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
        ["project_open"] = (args, _) => ExecuteProjectOpenAsync(args),
        ["project_info"] = (_, _) => Task.FromResult(ExecuteProjectInfo()),
        ["file_list"] = (args, _) => Task.FromResult(ExecuteFileList(args)),
        ["file_read"] = (args, ct) => ExecuteFileReadAsync(args, ct),
        ["file_write"] = (args, ct) => ExecuteFileWriteAsync(args, ct),
        ["file_edit"] = (args, ct) => ExecuteFileEditAsync(args, ct),
        ["search_text"] = (args, ct) => ExecuteSearchTextAsync(args, ct),
        ["find_symbol"] = (args, _) => ExecuteFindSymbolAsync(args),
        ["get_symbol_info"] = (args, _) => ExecuteGetSymbolInfoAsync(args),
        ["get_class_structure"] = (args, _) => ExecuteGetClassStructureAsync(args),
        ["add_class_property"] = (args, _) => ExecuteAddClassPropertyAsync(args),
        ["add_class_method"] = (args, _) => ExecuteAddClassMethodAsync(args),
        ["find_references"] = (args, _) => ExecuteFindReferencesAsync(args),
        ["find_implementations"] = (args, _) => ExecuteFindImplementationsAsync(args),
        ["parse_project"] = (args, _) => ExecuteParseProjectAsync(args),
        ["rename_symbol"] = (args, ct) => ExecuteRenameSymbolAsync(args, ct),
        // Advanced reverse search tools
        ["find_callers"] = (args, _) => ExecuteFindCallersAsync(args),
        ["find_call_chains"] = (args, _) => ExecuteFindCallChainsAsync(args),
        ["find_type_usages"] = (args, _) => ExecuteFindTypeUsagesAsync(args),
        ["analyze_call_patterns"] = (args, _) => ExecuteAnalyzeCallPatternsAsync(args),
        ["analyze_inheritance"] = (args, _) => ExecuteAnalyzeInheritanceAsync(args),
        ["find_circular_dependencies"] = (args, _) => ExecuteFindCircularDependenciesAsync(args),
        ["find_unused_methods"] = (args, _) => ExecuteFindUnusedMethodsAsync(args),
        ["analyze_call_graph"] = (args, _) => ExecuteAnalyzeCallGraphAsync(args),
        ["find_recursive_calls"] = (args, _) => ExecuteFindRecursiveCallsAsync(args),
        ["analyze_type_dependencies"] = (args, _) => ExecuteAnalyzeTypeDependenciesAsync(args),
        // AI-powered tools
        ["ask_codebase"] = (args, _) => HandleAskCodebaseAsync(args),
        ["ai_suggest_fix"] = (args, _) => HandleAISuggestFixAsync(args),
        ["ai_refactor"] = (args, _) => HandleAIRefactorAsync(args),
        ["ai_implement_feature"] = (args, _) => HandleAIImplementFeatureAsync(args),
        // Phase 2 tools
        ["get_workflows"] = (args, _) => ExecuteGetWorkflowsAsync(args),
        ["parse_workflow"] = (args, _) => ExecuteParseWorkflowAsync(args),
        ["validate_workflow_consistency"] = (args, _) => ExecuteValidateWorkflowConsistency(args),
        ["get_config_schema"] = (args, _) => ExecuteGetConfigSchemaAsync(args),
        ["merge_configs"] = (args, _) => ExecuteMergeConfigsAsync(args),
        ["analyze_impact"] = (args, _) => ExecuteAnalyzeImpactAsync(args),
        ["trace_feature"] = (args, _) => ExecuteTraceFeatureAsync(args),
        // Bulk edit tools
        ["bulk_replace"] = (args, ct) => WithProjectPathsAsync(args, BulkReplaceFilesArgument, a => ExecuteBulkReplaceAsync(a, ct)),
        ["conditional_edit"] = (args, ct) => WithProjectPathsAsync(args, BulkFilesArgument, a => ExecuteConditionalEditAsync(a, ct)),
        ["batch_refactor"] = (args, ct) => WithProjectPathsAsync(args, BulkFilesArgument, a => ExecuteBatchRefactorAsync(a, ct)),
        ["multi_file_edit"] = (args, ct) => WithProjectPathsAsync(args, MultiFileOperationsArgument, a => ExecuteMultiFileEditAsync(a, ct)),
        ["preview_bulk_changes"] = (args, ct) => WithProjectPathsAsync(args, BulkFilesAndOperationsArguments, a => ExecutePreviewBulkChangesAsync(a, ct)),
        ["rollback_bulk_edit"] = (args, ct) => ExecuteRollbackBulkEditAsync(args, ct),
        ["validate_bulk_edit"] = (args, _) => WithProjectPathsAsync(args, BulkFilesArgument, a => ExecuteValidateBulkEditAsync(a)),
        ["get_available_rollbacks"] = (_, ct) => ExecuteGetAvailableRollbacksAsync(ct),
        ["estimate_bulk_impact"] = (args, ct) => WithProjectPathsAsync(args, BulkFilesArgument, a => ExecuteEstimateBulkImpactAsync(a)),
        ["get_bulk_file_statistics"] = (args, _) => WithProjectPathsAsync(args, BulkFilesArgument, a => ExecuteGetBulkFileStatisticsAsync(a)),
        // Streaming processor tools
        ["stream_process_file"] = (args, ct) => WithProjectPathsAsync(args, StreamFileArguments, a => ExecuteStreamProcessFileAsync(a, ct)),
        ["bulk_transform"] = (args, ct) => WithProjectPathsAsync(args, BulkTransformArguments, a => ExecuteBulkTransformAsync(a, ct)),
        ["get_stream_progress"] = (args, _) => ExecuteGetStreamProgressAsync(args),
        ["cancel_stream_operation"] = (args, _) => ExecuteCancelStreamOperationAsync(args),
        ["resume_stream_operation"] = (args, ct) => ExecuteResumeStreamOperationAsync(args, ct),
        ["list_stream_operations"] = (args, _) => ExecuteListStreamOperationsAsync(args),
        ["cleanup_stream_operations"] = (args, _) => ExecuteCleanupStreamOperationsAsync(args),
        ["get_available_processors"] = (_, _) => ExecuteGetAvailableProcessorsAsync(),
        ["estimate_stream_processing"] = (args, _) => WithProjectPathsAsync(args, StreamEstimateArguments, a => ExecuteEstimateStreamProcessingAsync(a)),
        // Phase 3 architecture validation tools
        ["validate_architecture"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteValidateArchitectureAsync(a, ct)),
        ["detect_layer_violations"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteDetectLayerViolationsAsync(a, ct)),
        ["get_architecture_report"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteGetArchitectureReportAsync(a, ct)),
        ["define_custom_architecture"] = (args, ct) => ExecuteDefineCustomArchitectureAsync(args, ct),
        ["analyze_circular_dependencies"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteAnalyzeCircularDependenciesAsync(a, ct)),
        ["generate_architecture_diagram"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteGenerateArchitectureDiagramAsync(a, ct)),
        ["get_architecture_recommendations"] = (args, ct) => ExecuteGetArchitectureRecommendationsAsync(args, ct),
        ["check_type_compliance"] = (args, ct) => ExecuteCheckTypeComplianceAsync(args, ct),
        ["get_predefined_architectures"] = (_, ct) => ExecuteGetPredefinedArchitecturesAsync(ct),
        // Duplicate Code Detection Tools
        ["detect_duplicates"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteDetectDuplicatesAsync(a, ct)),
        ["find_exact_duplicates"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteFindExactDuplicatesAsync(a, ct)),
        ["find_near_duplicates"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteFindNearDuplicatesAsync(a, ct)),
        ["analyze_duplication_metrics"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteAnalyzeDuplicationMetricsAsync(a, ct)),
        ["get_refactoring_suggestions"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteGetRefactoringSuggestionsAsync(a, ct)),
        ["get_duplicate_hotspots"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteGetDuplicateHotspotsAsync(a, ct)),
        ["compare_code_blocks"] = (args, ct) => ExecuteCompareCodeBlocksAsync(args, ct),
        ["validate_refactoring"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteValidateRefactoringAsync(a, ct)),
        // Roslyn Analyzer Tools
        ["load_roslyn_analyzers"] = (args, ct) => ExecuteLoadRoslynAnalyzersAsync(args, ct),
        ["run_roslyn_analyzers"] = (args, ct) => ExecuteRunRoslynAnalyzersAsync(args, ct),
        ["list_roslyn_analyzers"] = (_, _) => Task.FromResult(ExecuteListRoslynAnalyzers()),
        // Automated Code Fix Tools
        ["code_quality_analyze"] = (args, ct) => ExecuteCodeQualityAnalyzeAsync(args, ct),
        ["code_quality_fix"] = (args, ct) => ExecuteCodeQualityFixAsync(args, ct),
        ["code_quality_profiles"] = (_, _) => ExecuteCodeQualityProfiles(),
        // Roslyn Code Fix and Configuration Tools
        ["apply_roslyn_fixes"] = (args, ct) => WithProjectPathsAsync(args, RoslynFixArguments, a => ExecuteApplyRoslynFixesAsync(a, ct)),
        ["configure_analyzer"] = (args, ct) => ExecuteConfigureAnalyzerAsync(args, ct),
        ["get_analyzer_config"] = (_, _) => ExecuteGetAnalyzerConfig(),
        ["reset_analyzer_config"] = (_, ct) => ExecuteResetAnalyzerConfigAsync(ct),
        ["generate_analysis_report"] = (args, ct) => WithProjectPathsAsync(args, AnalysisReportArguments, a => ExecuteGenerateAnalysisReportAsync(a, ct)),
        // SQL Migration Analyzer Tools (Phase 3)
        ["analyze_migrations"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteAnalyzeMigrationsAsync(a, ct)),
        ["detect_breaking_changes"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteDetectBreakingChangesAsync(a, ct)),
        ["get_migration_history"] = (args, ct) => WithProjectPathsAsync(args, MigrationHistoryArguments, a => ExecuteGetMigrationHistoryAsync(a, ct)),
        ["get_migration_dependencies"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteGetMigrationDependenciesAsync(a, ct)),
        ["generate_migration_report"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteGenerateMigrationReportAsync(a, ct)),
        ["validate_migrations"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteValidateMigrationsAsync(a, ct)),
        // Large File Optimization Tools (Phase 3)
        ["analyze_large_files"] = (args, ct) => WithProjectPathsAsync(args, ProjectPathArgument, a => ExecuteAnalyzeLargeFilesAsync(a, ct)),
        ["optimize_large_class"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteOptimizeLargeClassAsync(a, ct)),
        ["optimize_large_method"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteOptimizeLargeMethodAsync(a, ct)),
        ["get_complexity_metrics"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteGetComplexityMetricsAsync(a, ct)),
        ["generate_optimization_plan"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteGenerateOptimizationPlanAsync(a, ct)),
        ["detect_god_classes"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteDetectGodClassesAsync(a, ct)),
        ["detect_god_methods"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteDetectGodMethodsAsync(a, ct)),
        ["analyze_code_smells"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteAnalyzeCodeSmellsAsync(a, ct)),
        // Refactoring tools
        ["extract_method"] = (args, ct) => ExecuteExtractMethodAsync(args, ct),
        ["get_optimization_recommendations"] = (args, ct) => WithProjectPathsAsync(args, FilePathArguments, a => ExecuteGetOptimizationRecommendationsAsync(a, ct)),
        // Universal File Operations (consolidated)
        ["get_file_info"] = (args, ct) => ExecuteGetFileInfoAsync(args, ct),
        ["get_file_content"] = (args, ct) => ExecuteGetFileContentAsync(args, ct),
        ["execute_file_operation"] = (args, ct) => ExecuteFileOperationAsync(args, ct),
        ["execute_batch"] = (args, ct) => ExecuteBatchAsync(args, ct),
        // Unified Analysis (consolidated)
        ["analyze_symbol"] = (args, ct) => ExecuteAnalyzeSymbolAsync(args, ct),
        ["analyze_type"] = (args, ct) => ExecuteAnalyzeTypeAsync(args, ct),
        ["analyze_file"] = (args, ct) => ExecuteAnalyzeFileAsync(args, ct),
        ["analyze_project"] = (args, ct) => ExecuteAnalyzeProjectAsync(args, ct),
        ["analyze_architecture"] = (args, ct) => ExecuteAnalyzeArchitectureAsync(args, ct),
        ["analyze_dependencies"] = (args, ct) => ExecuteAnalyzeDependenciesUnifiedAsync(args, ct),
        ["analyze_quality"] = (args, ct) => ExecuteAnalyzeQualityAsync(args, ct),
        // Bulk Operations Hub (consolidated)
        ["execute_bulk_operation"] = (args, ct) => ExecuteBulkOperationAsync(args, ct),
        ["preview_bulk_operation"] = (args, ct) => ExecutePreviewBulkOperationAsync(args, ct),
        ["get_bulk_progress"] = (args, ct) => ExecuteGetBulkProgressAsync(args, ct),
        ["manage_bulk_operation"] = (args, ct) => ExecuteManageBulkOperationAsync(args, ct),
        // Stream Processing Controller (consolidated)
        ["process_stream"] = (args, ct) => ExecuteProcessStreamAsync(args, ct),
        ["monitor_stream"] = (args, ct) => ExecuteMonitorStreamAsync(args, ct),
        ["manage_stream"] = (args, ct) => ExecuteManageStreamAsync(args, ct),
    };
}

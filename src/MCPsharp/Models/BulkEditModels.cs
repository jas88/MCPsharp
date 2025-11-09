namespace MCPsharp.Models;

// This file maintains backward compatibility by re-exporting all BulkEdit models
// The actual implementations have been moved to the BulkEdit subdirectory

// Request models
using MCPsharp.Models.BulkEdit;
using BulkEditRequest = MCPsharp.Models.BulkEdit.BulkEditRequest;

// Operation models
using BulkEditOperationType = MCPsharp.Models.BulkEdit.BulkEditOperationType;
using BulkEditCondition = MCPsharp.Models.BulkEdit.BulkEditCondition;
using BulkConditionType = MCPsharp.Models.BulkEdit.BulkConditionType;
using BulkRefactorPattern = MCPsharp.Models.BulkEdit.BulkRefactorPattern;
using BulkRefactorType = MCPsharp.Models.BulkEdit.BulkRefactorType;
using MultiFileEditOperation = MCPsharp.Models.BulkEdit.MultiFileEditOperation;

// Options models
using BulkEditOptions = MCPsharp.Models.BulkEdit.BulkEditOptions;
using ConditionalEditOptions = MCPsharp.Models.BulkEdit.ConditionalEditOptions;
using RefactorOptions = MCPsharp.Models.BulkEdit.RefactorOptions;
using MultiFileEditOptions = MCPsharp.Models.BulkEdit.MultiFileEditOptions;

// Result models
using BulkEditResult = MCPsharp.Models.BulkEdit.BulkEditResult;
using BulkEditError = MCPsharp.Models.BulkEdit.BulkEditError;
using FileBulkEditResult = MCPsharp.Models.BulkEdit.FileBulkEditResult;
using BulkEditSummary = MCPsharp.Models.BulkEdit.BulkEditSummary;

// Preview models
using BulkEditPreview = MCPsharp.Models.BulkEdit.BulkEditPreview;
using FileChangePreview = MCPsharp.Models.BulkEdit.FileChangePreview;
using ImpactAssessment = MCPsharp.Models.BulkEdit.ImpactAssessment;
using PreviewResult = MCPsharp.Models.BulkEdit.PreviewResult;
using PreviewSummary = MCPsharp.Models.BulkEdit.PreviewSummary;
using FilePreviewResult = MCPsharp.Models.BulkEdit.FilePreviewResult;
using ImpactEstimate = MCPsharp.Models.BulkEdit.ImpactEstimate;

// Rollback models
using RollbackSession = MCPsharp.Models.BulkEdit.RollbackSession;
using RollbackFileInfo = MCPsharp.Models.BulkEdit.RollbackFileInfo;
using RollbackResult = MCPsharp.Models.BulkEdit.RollbackResult;
using FileRollbackResult = MCPsharp.Models.BulkEdit.FileRollbackResult;
using RollbackOperationType = MCPsharp.Models.BulkEdit.RollbackOperationType;
using RollbackError = MCPsharp.Models.BulkEdit.RollbackError;
using RollbackInfo = MCPsharp.Models.BulkEdit.RollbackInfo;

// Progress models
using BulkEditProgress = MCPsharp.Models.BulkEdit.BulkEditProgress;
using ChangeType = MCPsharp.Models.BulkEdit.ChangeType;
using FileEdit = MCPsharp.Models.BulkEdit.FileEdit;
using MultiFileOperation = MCPsharp.Models.BulkEdit.MultiFileOperation;

// Risk and complexity models
using RiskItem = MCPsharp.Models.BulkEdit.RiskItem;
using RiskType = MCPsharp.Models.BulkEdit.RiskType;
using RiskSeverity = MCPsharp.Models.BulkEdit.RiskSeverity;
using ChangeRiskLevel = MCPsharp.Models.BulkEdit.ChangeRiskLevel;
using ComplexityEstimate = MCPsharp.Models.BulkEdit.ComplexityEstimate;
using ComplexityLevel = MCPsharp.Models.BulkEdit.ComplexityLevel;
using RiskLevel = MCPsharp.Models.BulkEdit.RiskLevel;

/// <summary>
/// This namespace provides re-exports of all BulkEdit models for backward compatibility.
/// The actual model implementations are located in the MCPsharp.Models.BulkEdit namespace.
/// All types are aliased to their new locations to ensure existing code continues to work.
/// </summary>
internal static class BulkEditModelsCompat
{
    // This class exists only to satisfy the XML documentation requirement.
    // All actual types are imported via using declarations above.
}

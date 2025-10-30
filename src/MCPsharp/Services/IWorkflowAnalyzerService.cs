using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Service for analyzing GitHub Actions workflows
/// Interface to be implemented by Phase 2 agents
/// </summary>
public interface IWorkflowAnalyzerService
{
    Task<List<WorkflowInfo>> GetAllWorkflowsAsync(string projectRoot);
    Task<WorkflowInfo> ParseWorkflowAsync(string workflowPath);
    Task<List<WorkflowValidationIssue>> ValidateWorkflowConsistencyAsync(string workflowPath, string projectPath);
}

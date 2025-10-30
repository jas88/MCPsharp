namespace MCPsharp.Models;

/// <summary>
/// Represents a GitHub Actions workflow file.
/// </summary>
public class WorkflowInfo
{
    /// <summary>
    /// Workflow name from the 'name' field
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Absolute path to the workflow YAML file
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Trigger events (push, pull_request, schedule, workflow_dispatch, etc.)
    /// </summary>
    public IReadOnlyList<string>? Triggers { get; init; }

    /// <summary>
    /// Environment variables defined at workflow level
    /// </summary>
    public Dictionary<string, string>? Environment { get; init; }

    /// <summary>
    /// Jobs defined in the workflow
    /// </summary>
    public IReadOnlyList<WorkflowJob>? Jobs { get; init; }

    /// <summary>
    /// Secrets referenced in the workflow (extracted from ${{ secrets.* }})
    /// </summary>
    public IReadOnlyList<string>? Secrets { get; init; }
}

/// <summary>
/// Represents a job within a GitHub Actions workflow.
/// </summary>
public class WorkflowJob
{
    /// <summary>
    /// Job identifier/name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Runner specification (ubuntu-latest, windows-latest, etc.)
    /// </summary>
    public required string RunsOn { get; init; }

    /// <summary>
    /// Steps in this job
    /// </summary>
    public IReadOnlyList<WorkflowStep>? Steps { get; init; }

    /// <summary>
    /// Environment variables defined at job level
    /// </summary>
    public Dictionary<string, string>? Environment { get; init; }
}

/// <summary>
/// Represents a step within a workflow job.
/// </summary>
public class WorkflowStep
{
    /// <summary>
    /// Step name (optional in workflows)
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Action reference (e.g., "actions/checkout@v4")
    /// </summary>
    public string? Uses { get; init; }

    /// <summary>
    /// Shell command to execute
    /// </summary>
    public string? Run { get; init; }

    /// <summary>
    /// Parameters passed to the action (with: section)
    /// </summary>
    public Dictionary<string, string>? With { get; init; }
}

/// <summary>
/// Represents a validation issue found when comparing workflow to project settings.
/// </summary>
public class WorkflowValidationIssue
{
    /// <summary>
    /// Type of issue (VersionMismatch, MissingDependency, UnsupportedFramework, etc.)
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Severity level (Error, Warning, Info)
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>
    /// Human-readable description of the issue
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Path to the workflow file where issue was found
    /// </summary>
    public string? WorkflowFile { get; init; }

    /// <summary>
    /// Path to the project file related to the issue
    /// </summary>
    public string? ProjectFile { get; init; }
}

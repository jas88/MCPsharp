using MCPsharp.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCPsharp.Services.Phase2;

/// <summary>
/// Analyzes GitHub Actions workflows
/// </summary>
public class WorkflowAnalyzerService : IWorkflowAnalyzerService
{
    public async Task<List<WorkflowInfo>> GetAllWorkflowsAsync(string projectRoot)
    {
        var workflows = new List<WorkflowInfo>();
        var workflowDir = Path.Combine(projectRoot, ".github", "workflows");

        if (!Directory.Exists(workflowDir))
            return workflows;

        var workflowFiles = Directory.GetFiles(workflowDir, "*.yml")
            .Concat(Directory.GetFiles(workflowDir, "*.yaml"));

        foreach (var workflowFile in workflowFiles)
        {
            try
            {
                var workflow = await ParseWorkflowAsync(workflowFile);
                workflows.Add(workflow);
            }
            catch (Exception ex) when (ex is YamlException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // Skip workflows that cannot be read or parsed
            }
        }

        return workflows;
    }

    public async Task<WorkflowInfo> ParseWorkflowAsync(string workflowPath)
    {
        var content = await File.ReadAllTextAsync(workflowPath);

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var workflow = deserializer.Deserialize<WorkflowYaml?>(content)
            ?? throw new InvalidDataException($"Workflow file '{workflowPath}' is empty");

        var jobs = new List<WorkflowJob>();
        if (workflow.Jobs != null)
        {
            foreach (var job in workflow.Jobs)
            {
                var steps = new List<WorkflowStep>();
                if (job.Value.Steps != null)
                {
                    foreach (var step in job.Value.Steps)
                    {
                        steps.Add(new WorkflowStep
                        {
                            Name = step.Name ?? step.Uses ?? step.Run ?? "Unnamed step",
                            Uses = step.Uses,
                            Run = step.Run,
                            With = step.With
                        });
                    }
                }

                jobs.Add(new WorkflowJob
                {
                    Name = job.Key,
                    RunsOn = job.Value.RunsOn ?? "ubuntu-latest",
                    Steps = steps,
                    Environment = job.Value.Env
                });
            }
        }

        var triggers = new List<string>();
        if (workflow.On is string onString)
        {
            triggers.Add(onString);
        }
        else if (workflow.On is Dictionary<object, object> onDict)
        {
            triggers.AddRange(onDict.Keys.Select(k => k.ToString() ?? ""));
        }

        return new WorkflowInfo
        {
            Name = workflow.Name ?? Path.GetFileNameWithoutExtension(workflowPath),
            FilePath = workflowPath,
            Triggers = triggers,
            Environment = workflow.Env,
            Jobs = jobs
        };
    }

    public async Task<List<WorkflowValidationIssue>> ValidateWorkflowConsistencyAsync(string workflowPath, string projectPath)
    {
        var issues = new List<WorkflowValidationIssue>();

        var workflow = await ParseWorkflowAsync(workflowPath);

        // Validate dotnet version consistency
        foreach (var job in workflow.Jobs ?? [])
        {
            foreach (var step in job.Steps ?? [])
            {
                if (step.Uses?.StartsWith("actions/setup-dotnet", StringComparison.Ordinal) == true)
                {
                    var dotnetVersion = step.With?.GetValueOrDefault("dotnet-version");
                    if (dotnetVersion != null)
                    {
                        // Check if this matches project file target frameworks
                        var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
                        if (csprojFiles.Length == 0)
                        {
                            issues.Add(new WorkflowValidationIssue
                            {
                                Type = "MissingProject",
                                Severity = "Warning",
                                Message = $"Workflow specifies .NET {dotnetVersion} but no .csproj files found",
                                WorkflowFile = workflowPath
                            });
                        }
                        else
                        {
                            // Check for version mismatch
                            foreach (var csprojFile in csprojFiles)
                            {
                                try
                                {
                                    var csprojContent = await File.ReadAllTextAsync(csprojFile);
                                    if (csprojContent.Contains("net8.0", StringComparison.Ordinal) && dotnetVersion.Contains("6.0", StringComparison.Ordinal))
                                    {
                                        issues.Add(new WorkflowValidationIssue
                                        {
                                            Type = "VersionMismatch",
                                            Severity = "Warning",
                                            Message = $"Workflow .NET version mismatch: specifies {dotnetVersion} but project targets .NET 8.0",
                                            WorkflowFile = workflowPath,
                                            ProjectFile = csprojFile
                                        });
                                        break; // Found version mismatch, no need to check more files
                                    }
                                }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                                {
                                    // Skip files we can't read
                                }
                            }
                        }
                    }
                }
            }
        }

        return issues;
    }

    // YAML structure classes
    internal sealed class WorkflowYaml
    {
        public string? Name { get; init; }
        public object? On { get; init; }
        public Dictionary<string, string>? Env { get; init; }
        public Dictionary<string, JobYaml>? Jobs { get; init; }
    }

    internal sealed class JobYaml
    {
        public string? RunsOn { get; init; }
        public Dictionary<string, string>? Env { get; init; }
        public List<StepYaml>? Steps { get; init; }
    }

    internal sealed class StepYaml
    {
        public string? Name { get; init; }
        public string? Uses { get; init; }
        public string? Run { get; init; }
        public Dictionary<string, string>? With { get; init; }
    }
}

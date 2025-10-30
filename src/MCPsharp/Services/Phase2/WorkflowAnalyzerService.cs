using MCPsharp.Models;
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
            catch
            {
                // Skip invalid workflows
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

        var workflow = deserializer.Deserialize<WorkflowYaml>(content);

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
                if (step.Uses?.StartsWith("actions/setup-dotnet") == true)
                {
                    var dotnetVersion = step.With?.GetValueOrDefault("dotnet-version");
                    if (dotnetVersion != null)
                    {
                        // Check if this matches project file target frameworks
                        var csprojFiles = Directory.GetFiles(projectPath, "*.csproj", SearchOption.AllDirectories);
                        if (!csprojFiles.Any())
                        {
                            issues.Add(new WorkflowValidationIssue
                            {
                                Type = "MissingProject",
                                Severity = "Warning",
                                Message = $"Workflow specifies .NET {dotnetVersion} but no .csproj files found",
                                WorkflowFile = workflowPath
                            });
                        }
                    }
                }
            }
        }

        return issues;
    }

    // YAML structure classes
    private class WorkflowYaml
    {
        public string? Name { get; set; }
        public object? On { get; set; }
        public Dictionary<string, string>? Env { get; set; }
        public Dictionary<string, JobYaml>? Jobs { get; set; }
    }

    private class JobYaml
    {
        public string? RunsOn { get; set; }
        public Dictionary<string, string>? Env { get; set; }
        public List<StepYaml>? Steps { get; set; }
    }

    private class StepYaml
    {
        public string? Name { get; set; }
        public string? Uses { get; set; }
        public string? Run { get; set; }
        public Dictionary<string, string>? With { get; set; }
    }
}

using System.Text.RegularExpressions;
using MCPsharp.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCPsharp.Services;

/// <summary>
/// Service for analyzing GitHub Actions workflow files and validating consistency with project configuration.
/// </summary>
public partial class WorkflowAnalyzerService
{
    private readonly IDeserializer _yamlDeserializer;

    [GeneratedRegex(@"\$\{\{\s*secrets\.(\w+)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex SecretReferenceRegex();

    public WorkflowAnalyzerService()
    {
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(HyphenatedNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Finds all workflow files in the .github/workflows directory.
    /// </summary>
    /// <param name="projectRoot">Root directory of the project</param>
    /// <returns>List of absolute paths to workflow files</returns>
    public Task<IReadOnlyList<string>> FindWorkflowsAsync(string projectRoot)
    {
        var workflowsDir = Path.Combine(projectRoot, ".github", "workflows");

        if (!Directory.Exists(workflowsDir))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var workflows = Directory.GetFiles(workflowsDir, "*.yml")
            .Concat(Directory.GetFiles(workflowsDir, "*.yaml"))
            .Select(Path.GetFullPath)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(workflows);
    }

    /// <summary>
    /// Parses a GitHub Actions workflow file.
    /// </summary>
    /// <param name="workflowPath">Absolute path to the workflow file</param>
    /// <returns>Parsed workflow information</returns>
    public async Task<WorkflowInfo> ParseWorkflowAsync(string workflowPath)
    {
        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(workflowPath);
        var workflowYaml = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);

        var name = GetWorkflowName(workflowYaml, workflowPath);
        var triggers = ExtractTriggers(workflowYaml);
        var environment = ExtractEnvironment(workflowYaml);
        var jobs = ExtractJobs(workflowYaml);
        var secrets = ExtractSecrets(yamlContent);

        return new WorkflowInfo
        {
            Name = name,
            FilePath = workflowPath,
            Triggers = triggers,
            Environment = environment,
            Jobs = jobs,
            Secrets = secrets
        };
    }

    /// <summary>
    /// Gets all workflows in the project.
    /// </summary>
    /// <param name="projectRoot">Root directory of the project</param>
    /// <returns>List of parsed workflow information</returns>
    public async Task<IReadOnlyList<WorkflowInfo>> GetAllWorkflowsAsync(string projectRoot)
    {
        var workflowPaths = await FindWorkflowsAsync(projectRoot);
        var workflows = new List<WorkflowInfo>();

        foreach (var workflowPath in workflowPaths)
        {
            try
            {
                var workflow = await ParseWorkflowAsync(workflowPath);
                workflows.Add(workflow);
            }
            catch (Exception)
            {
                // Skip invalid workflow files
                continue;
            }
        }

        return workflows;
    }

    /// <summary>
    /// Validates that workflow settings are consistent with project settings.
    /// </summary>
    /// <param name="workflow">Workflow to validate</param>
    /// <param name="project">Project information</param>
    /// <returns>List of validation issues found</returns>
    public Task<IReadOnlyList<WorkflowValidationIssue>> ValidateWorkflowConsistencyAsync(
        WorkflowInfo workflow,
        CsprojInfo project)
    {
        var issues = new List<WorkflowValidationIssue>();

        // Extract .NET versions from workflow
        var workflowDotnetVersions = ExtractDotnetVersionsFromWorkflow(workflow);

        // Check if workflow .NET versions match project target frameworks
        foreach (var workflowVersion in workflowDotnetVersions)
        {
            var isCompatible = project.TargetFrameworks.Any(tf =>
                IsVersionCompatible(tf, workflowVersion));

            if (!isCompatible)
            {
                issues.Add(new WorkflowValidationIssue
                {
                    Type = "VersionMismatch",
                    Severity = "Warning",
                    Message = $"Workflow uses .NET version '{workflowVersion}' which may not match project target frameworks: {string.Join(", ", project.TargetFrameworks)}",
                    WorkflowFile = workflow.FilePath,
                    ProjectFile = project.FilePath
                });
            }
        }

        // Check if workflow has dotnet build/test steps for a .NET project
        var hasDotnetSteps = workflow.Jobs?.Any(j =>
            j.Steps?.Any(s =>
                s.Run?.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) == true ||
                s.Run?.Contains("dotnet test", StringComparison.OrdinalIgnoreCase) == true) == true) == true;

        if (!hasDotnetSteps && project.OutputType != "Library")
        {
            issues.Add(new WorkflowValidationIssue
            {
                Type = "MissingBuildStep",
                Severity = "Warning",
                Message = "Workflow does not contain dotnet build or test steps for .NET project",
                WorkflowFile = workflow.FilePath,
                ProjectFile = project.FilePath
            });
        }

        return Task.FromResult<IReadOnlyList<WorkflowValidationIssue>>(issues);
    }

    private static string GetWorkflowName(Dictionary<string, object> yaml, string filePath)
    {
        if (yaml.TryGetValue("name", out var nameObj) && nameObj is string name)
        {
            return name;
        }

        return Path.GetFileNameWithoutExtension(filePath);
    }

    private static IReadOnlyList<string> ExtractTriggers(Dictionary<string, object> yaml)
    {
        if (!yaml.TryGetValue("on", out var onObj))
        {
            return Array.Empty<string>();
        }

        var triggers = new List<string>();

        switch (onObj)
        {
            case string singleTrigger:
                triggers.Add(singleTrigger);
                break;
            case IList<object> triggerList:
                triggers.AddRange(triggerList.OfType<string>());
                break;
            case Dictionary<object, object> triggerDict:
                triggers.AddRange(triggerDict.Keys.OfType<string>());
                break;
        }

        return triggers;
    }

    private static Dictionary<string, string>? ExtractEnvironment(Dictionary<string, object> yaml)
    {
        if (!yaml.TryGetValue("env", out var envObj) || envObj is not Dictionary<object, object> envDict)
        {
            return null;
        }

        return envDict.ToDictionary(
            kvp => kvp.Key.ToString() ?? string.Empty,
            kvp => kvp.Value?.ToString() ?? string.Empty);
    }

    private static IReadOnlyList<WorkflowJob> ExtractJobs(Dictionary<string, object> yaml)
    {
        if (!yaml.TryGetValue("jobs", out var jobsObj) || jobsObj is not Dictionary<object, object> jobsDict)
        {
            return Array.Empty<WorkflowJob>();
        }

        var jobs = new List<WorkflowJob>();

        foreach (var jobEntry in jobsDict)
        {
            var jobName = jobEntry.Key.ToString() ?? "unknown";
            if (jobEntry.Value is not Dictionary<object, object> jobData)
            {
                continue;
            }

            var runsOn = ExtractRunsOn(jobData);
            var steps = ExtractSteps(jobData);
            var environment = ExtractJobEnvironment(jobData);

            jobs.Add(new WorkflowJob
            {
                Name = jobName,
                RunsOn = runsOn,
                Steps = steps,
                Environment = environment
            });
        }

        return jobs;
    }

    private static string ExtractRunsOn(Dictionary<object, object> jobData)
    {
        if (!jobData.TryGetValue("runs-on", out var runsOnObj))
        {
            return "unknown";
        }

        return runsOnObj switch
        {
            string s => s,
            IList<object> list => string.Join(", ", list),
            _ => runsOnObj.ToString() ?? "unknown"
        };
    }

    private static IReadOnlyList<WorkflowStep> ExtractSteps(Dictionary<object, object> jobData)
    {
        if (!jobData.TryGetValue("steps", out var stepsObj) || stepsObj is not IList<object> stepsList)
        {
            return Array.Empty<WorkflowStep>();
        }

        var steps = new List<WorkflowStep>();

        foreach (var stepObj in stepsList)
        {
            if (stepObj is not Dictionary<object, object> stepData)
            {
                continue;
            }

            var name = stepData.TryGetValue("name", out var nameObj)
                ? nameObj?.ToString() ?? "unnamed"
                : "unnamed";

            var uses = stepData.TryGetValue("uses", out var usesObj)
                ? usesObj?.ToString()
                : null;

            var run = stepData.TryGetValue("run", out var runObj)
                ? runObj?.ToString()
                : null;

            var with = stepData.TryGetValue("with", out var withObj) && withObj is Dictionary<object, object> withDict
                ? withDict.ToDictionary(
                    kvp => kvp.Key.ToString() ?? string.Empty,
                    kvp => kvp.Value?.ToString() ?? string.Empty)
                : null;

            steps.Add(new WorkflowStep
            {
                Name = name,
                Uses = uses,
                Run = run,
                With = with
            });
        }

        return steps;
    }

    private static Dictionary<string, string>? ExtractJobEnvironment(Dictionary<object, object> jobData)
    {
        if (!jobData.TryGetValue("env", out var envObj) || envObj is not Dictionary<object, object> envDict)
        {
            return null;
        }

        return envDict.ToDictionary(
            kvp => kvp.Key.ToString() ?? string.Empty,
            kvp => kvp.Value?.ToString() ?? string.Empty);
    }

    private static IReadOnlyList<string> ExtractSecrets(string yamlContent)
    {
        var secrets = new HashSet<string>();
        var matches = SecretReferenceRegex().Matches(yamlContent);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                secrets.Add(match.Groups[1].Value);
            }
        }

        return secrets.ToList();
    }

    private static List<string> ExtractDotnetVersionsFromWorkflow(WorkflowInfo workflow)
    {
        var versions = new HashSet<string>();

        // Check environment variables
        if (workflow.Environment?.TryGetValue("DOTNET_VERSION", out var envVersion) == true)
        {
            versions.Add(NormalizeDotnetVersion(envVersion));
        }

        // Check steps with setup-dotnet action
        if (workflow.Jobs != null)
        {
            foreach (var job in workflow.Jobs)
            {
                if (job.Steps == null) continue;

                foreach (var step in job.Steps)
                {
                    if (step.Uses?.Contains("setup-dotnet", StringComparison.OrdinalIgnoreCase) == true &&
                        step.With?.TryGetValue("dotnet-version", out var stepVersion) == true)
                    {
                        versions.Add(NormalizeDotnetVersion(stepVersion));
                    }
                }

                // Check job-level environment
                if (job.Environment?.TryGetValue("DOTNET_VERSION", out var jobEnvVersion) == true)
                {
                    versions.Add(NormalizeDotnetVersion(jobEnvVersion));
                }
            }
        }

        return versions.ToList();
    }

    private static string NormalizeDotnetVersion(string version)
    {
        // Remove .x suffix and normalize to major.minor format
        version = version.Replace(".x", "").Replace("*", "").Trim();

        // Extract major.minor (e.g., "9.0" from "9.0.1")
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    private static bool IsVersionCompatible(string targetFramework, string workflowVersion)
    {
        // Extract version from target framework (e.g., "net9.0" -> "9.0")
        var tfVersion = targetFramework.Replace("net", "").Replace("coreapp", "").Replace("standard", "");

        // Normalize both versions
        tfVersion = NormalizeDotnetVersion(tfVersion);
        workflowVersion = NormalizeDotnetVersion(workflowVersion);

        return tfVersion.Equals(workflowVersion, StringComparison.OrdinalIgnoreCase);
    }
}

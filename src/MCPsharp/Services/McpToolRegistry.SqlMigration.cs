using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// SQL migration analysis tool execution methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    private async Task<ToolCallResult> ExecuteAnalyzeMigrations(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        try
        {
            var result = await _sqlMigrationAnalyzer.AnalyzeMigrationsAsync(projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalMigrations"] = result.Summary.TotalMigrations,
                    ["appliedMigrations"] = result.Summary.AppliedMigrations,
                    ["pendingMigrations"] = result.Summary.PendingMigrations,
                    ["breakingChanges"] = result.Summary.BreakingChanges,
                    ["highRiskOperations"] = result.Summary.HighRiskOperations,
                    ["provider"] = result.Summary.Provider.ToString(),
                    ["analysisTime"] = result.Summary.TotalAnalysisTime.TotalSeconds,
                    ["migrations"] = result.Migrations.Take(10).Select(m => new
                    {
                        name = m.Name,
                        filePath = m.FilePath,
                        createdAt = m.CreatedAt,
                        isApplied = m.IsApplied,
                        operationCount = m.Operations.Count,
                        hasBreakingChanges = m.Operations.Any(o => o.IsBreakingChange)
                    }).ToList(),
                    ["topBreakingChanges"] = result.BreakingChanges.Take(5).Select(bc => new
                    {
                        type = bc.Type,
                        severity = bc.Severity.ToString(),
                        description = bc.Description,
                        tableName = bc.TableName,
                        recommendation = bc.Recommendation
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing migrations: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteDetectBreakingChanges(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var fromMigration = GetStringArgument(arguments, "fromMigration");
        var toMigration = GetStringArgument(arguments, "toMigration");
        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        if (string.IsNullOrEmpty(fromMigration) || string.IsNullOrEmpty(toMigration))
        {
            return new ToolCallResult { Success = false, Error = "fromMigration and toMigration parameters are required" };
        }

        try
        {
            var breakingChanges = await _sqlMigrationAnalyzer.DetectBreakingChangesAsync(fromMigration, toMigration, projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalBreakingChanges"] = breakingChanges.Count,
                    ["criticalChanges"] = breakingChanges.Count(bc => bc.Severity == MCPsharp.Models.SqlMigration.Severity.Critical),
                    ["highSeverityChanges"] = breakingChanges.Count(bc => bc.Severity == MCPsharp.Models.SqlMigration.Severity.High),
                    ["breakingChanges"] = breakingChanges.Select(bc => new
                    {
                        type = bc.Type,
                        severity = bc.Severity.ToString(),
                        description = bc.Description,
                        tableName = bc.TableName,
                        columnName = bc.ColumnName,
                        fromMigration = bc.FromMigration,
                        toMigration = bc.ToMigration,
                        recommendation = bc.Recommendation
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error detecting breaking changes: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetMigrationHistory(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var dbContextPath = GetStringArgument(arguments, "dbContextPath");
        if (string.IsNullOrEmpty(dbContextPath))
        {
            return new ToolCallResult { Success = false, Error = "dbContextPath parameter is required" };
        }

        try
        {
            var history = await _sqlMigrationAnalyzer.GetMigrationHistoryAsync(dbContextPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalEntries"] = history.Count,
                    ["history"] = history.Select(h => new
                    {
                        migrationName = h.MigrationName,
                        appliedAt = h.AppliedAt,
                        checksum = h.Checksum,
                        executionTime = h.ExecutionTime,
                        isSuccessful = h.IsSuccessful
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting migration history: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGetMigrationDependencies(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var migrationName = GetStringArgument(arguments, "migrationName");
        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        if (string.IsNullOrEmpty(migrationName))
        {
            return new ToolCallResult { Success = false, Error = "migrationName parameter is required" };
        }

        try
        {
            var dependency = await _sqlMigrationAnalyzer.GetMigrationDependenciesAsync(migrationName, projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["migrationName"] = dependency.MigrationName,
                    ["dependencyType"] = dependency.Type.ToString(),
                    ["dependsOn"] = dependency.DependsOn,
                    ["requiredBy"] = dependency.RequiredBy,
                    ["totalDependencies"] = dependency.DependsOn.Count,
                    ["totalDependents"] = dependency.RequiredBy.Count
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error getting migration dependencies: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteGenerateMigrationReport(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();
        var includeHistory = GetBoolArgument(arguments, "includeHistory") ?? true;

        try
        {
            var report = await _sqlMigrationAnalyzer.GenerateMigrationReportAsync(projectPath, includeHistory, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["metadata"] = new
                    {
                        projectPath = report.Metadata.ProjectPath,
                        generatedAt = report.Metadata.GeneratedAt,
                        analyzerVersion = report.Metadata.AnalyzerVersion,
                        provider = report.Metadata.Provider.ToString(),
                        totalMigrations = report.Metadata.TotalMigrations,
                        analysisTime = report.Metadata.AnalysisTime.TotalSeconds
                    },
                    ["totalMigrations"] = report.Migrations.Count,
                    ["totalBreakingChanges"] = report.BreakingChanges.Count,
                    ["overallRisk"] = report.RiskAssessment.OverallRisk.ToString(),
                    ["highRiskMigrations"] = report.RiskAssessment.MigrationRisks.Count(mr => (int)mr.RiskLevel >= (int)MCPsharp.Models.SqlMigration.RiskLevel.High),
                    ["topRecommendations"] = report.Recommendations.BestPractices.Take(5).ToList(),
                    ["warnings"] = report.Recommendations.Warnings.Take(5).ToList(),
                    ["statistics"] = report.Statistics
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error generating migration report: {ex.Message}" };
        }
    }

    private async Task<ToolCallResult> ExecuteValidateMigrations(JsonDocument arguments, CancellationToken ct)
    {
        if (_sqlMigrationAnalyzer == null)
        {
            return new ToolCallResult { Success = false, Error = "SQL migration analyzer service not available" };
        }

        var projectPath = GetStringArgument(arguments, "projectPath") ?? _projectContext.GetProjectContext()?.RootPath ?? Directory.GetCurrentDirectory();

        try
        {
            var issues = await _sqlMigrationAnalyzer.ValidateMigrationsAsync(projectPath, ct);

            return new ToolCallResult
            {
                Success = true,
                Result = new Dictionary<string, object>
                {
                    ["totalIssues"] = issues.Count,
                    ["criticalIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.Critical),
                    ["highSeverityIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.High),
                    ["mediumSeverityIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.Medium),
                    ["lowSeverityIssues"] = issues.Count(i => i.Severity == MCPsharp.Models.SqlMigration.Severity.Low),
                    ["issues"] = issues.Take(20).Select(i => new
                    {
                        type = i.Type,
                        severity = i.Severity.ToString(),
                        message = i.Message,
                        migrationFile = i.MigrationFile,
                        recommendation = i.Recommendation
                    }).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error validating migrations: {ex.Message}" };
        }
    }
}

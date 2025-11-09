using System.Collections.Immutable;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Registry;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace MCPsharp.Services;

/// <summary>
/// Automated code fix tools for MCPsharp
/// Provides MCP tool endpoints for analyzing and fixing code quality issues
/// </summary>
public partial class McpToolRegistry
{
    private readonly BuiltInCodeFixRegistry? _codeFixRegistry;

    /// <summary>
    /// Analyze code for quality issues using built-in diagnostic analyzers
    /// </summary>
    private async Task<ToolCallResult> ExecuteCodeQualityAnalyze(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            if (_codeFixRegistry == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Code fix registry not initialized. This feature requires BuiltInCodeFixRegistry."
                };
            }

            if (_workspace == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Roslyn workspace not initialized. Cannot analyze code without loaded workspace."
                };
            }

            // Parse arguments
            var root = arguments.RootElement;
            var targetPath = root.TryGetProperty("target_path", out var pathProp) ? pathProp.GetString() : null;
            var diagnosticIds = root.TryGetProperty("diagnostic_ids", out var idsProp)
                ? idsProp.EnumerateArray().Select(x => x.GetString()!).ToImmutableArray()
                : ImmutableArray<string>.Empty;
            var profileStr = root.TryGetProperty("profile", out var profileProp) ? profileProp.GetString() : "Balanced";

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "target_path is required"
                };
            }

            // Parse profile
            if (!Enum.TryParse<FixProfile>(profileStr, true, out var profile))
            {
                profile = FixProfile.Balanced;
            }

            // Get providers to use
            var providers = diagnosticIds.IsEmpty
                ? _codeFixRegistry.GetProvidersByProfile(profile)
                : _codeFixRegistry.GetAllProviders().Where(p => diagnosticIds.Any(id => p.FixableDiagnosticIds.Contains(id))).ToImmutableArray();

            if (providers.IsEmpty)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        issues_found = 0,
                        issues_by_provider = new Dictionary<string, int>(),
                        issues = Array.Empty<object>()
                    }
                };
            }

            // Determine if target is file or directory
            var fullPath = Path.GetFullPath(targetPath);
            var isDirectory = Directory.Exists(fullPath);
            var isFile = File.Exists(fullPath);

            if (!isDirectory && !isFile)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Target path does not exist: {targetPath}"
                };
            }

            // Get documents to analyze
            var documentsToAnalyze = new List<Document>();
            var solution = _workspace.Solution;

            if (isFile)
            {
                var doc = solution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => d.FilePath?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true);

                if (doc != null)
                {
                    documentsToAnalyze.Add(doc);
                }
            }
            else
            {
                // Directory - find all documents under it
                documentsToAnalyze.AddRange(
                    solution.Projects
                        .SelectMany(p => p.Documents)
                        .Where(d => d.FilePath?.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase) == true));
            }

            if (documentsToAnalyze.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        issues_found = 0,
                        message = "No documents found in workspace for the specified path",
                        issues_by_provider = new Dictionary<string, int>(),
                        issues = Array.Empty<object>()
                    }
                };
            }

            // Collect diagnostics from all providers
            var allIssues = new List<object>();
            var issuesByProvider = new Dictionary<string, int>();

            foreach (var provider in providers)
            {
                var analyzer = provider.GetAnalyzer();
                var providerIssues = 0;

                foreach (var doc in documentsToAnalyze)
                {
                    var semanticModel = await doc.GetSemanticModelAsync(ct);
                    if (semanticModel == null) continue;

                    var compilation = semanticModel.Compilation;
                    var analyzers = ImmutableArray.Create(analyzer);

                    var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, options: null);
                    var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct);

                    // Filter to diagnostics from this document
                    var docDiagnostics = diagnostics.Where(d =>
                        d.Location.IsInSource &&
                        d.Location.SourceTree?.FilePath?.Equals(doc.FilePath, StringComparison.OrdinalIgnoreCase) == true);

                    foreach (var diagnostic in docDiagnostics)
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        allIssues.Add(new
                        {
                            diagnostic_id = diagnostic.Id,
                            severity = diagnostic.Severity.ToString(),
                            message = diagnostic.GetMessage(),
                            file_path = diagnostic.Location.SourceTree?.FilePath ?? doc.FilePath ?? "",
                            line = lineSpan.StartLinePosition.Line + 1,
                            column = lineSpan.StartLinePosition.Character + 1,
                            provider = provider.Name
                        });
                        providerIssues++;
                    }
                }

                if (providerIssues > 0)
                {
                    issuesByProvider[provider.Name] = providerIssues;
                }
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    issues_found = allIssues.Count,
                    issues_by_provider = issuesByProvider,
                    issues = allIssues,
                    documents_analyzed = documentsToAnalyze.Count,
                    providers_used = providers.Length
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"{ex.Message}\n{ex.StackTrace}"
            };
        }
    }

    /// <summary>
    /// Apply automated code quality fixes to improve code
    /// </summary>
    private async Task<ToolCallResult> ExecuteCodeQualityFix(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            if (_codeFixRegistry == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Code fix registry not initialized. This feature requires BuiltInCodeFixRegistry."
                };
            }

            if (_workspace == null)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "Roslyn workspace not initialized. Cannot fix code without loaded workspace."
                };
            }

            // Parse arguments
            var root = arguments.RootElement;
            var targetPath = root.TryGetProperty("target_path", out var pathProp) ? pathProp.GetString() : null;
            var diagnosticIds = root.TryGetProperty("diagnostic_ids", out var idsProp)
                ? idsProp.EnumerateArray().Select(x => x.GetString()!).ToImmutableArray()
                : ImmutableArray<string>.Empty;
            var profileStr = root.TryGetProperty("profile", out var profileProp) ? profileProp.GetString() : "Balanced";
            var preview = root.TryGetProperty("preview", out var previewProp) ? previewProp.GetBoolean() : true;
            var createBackup = root.TryGetProperty("create_backup", out var backupProp) ? backupProp.GetBoolean() : true;

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = "target_path is required"
                };
            }

            // Parse profile
            if (!Enum.TryParse<FixProfile>(profileStr, true, out var profile))
            {
                profile = FixProfile.Balanced;
            }

            // Build configuration
            var config = profile switch
            {
                FixProfile.Conservative => FixConfiguration.ConservativeDefault,
                FixProfile.Balanced => FixConfiguration.BalancedDefault,
                FixProfile.Aggressive => FixConfiguration.AggressiveDefault,
                _ => FixConfiguration.BalancedDefault
            } with
            {
                CreateBackup = createBackup,
                RequireUserApproval = preview
            };

            // Get providers to use
            var providers = diagnosticIds.IsEmpty
                ? _codeFixRegistry.GetProvidersByProfile(profile)
                : _codeFixRegistry.GetAllProviders().Where(p => diagnosticIds.Any(id => p.FixableDiagnosticIds.Contains(id))).ToImmutableArray();

            if (providers.IsEmpty)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        preview,
                        fixes_applied = 0,
                        fixes_available = 0,
                        changes = Array.Empty<object>()
                    }
                };
            }

            // Determine if target is file or directory
            var fullPath = Path.GetFullPath(targetPath);
            var isDirectory = Directory.Exists(fullPath);
            var isFile = File.Exists(fullPath);

            if (!isDirectory && !isFile)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Target path does not exist: {targetPath}"
                };
            }

            // Get documents to fix
            var documentsToFix = new List<Document>();
            var solution = _workspace.Solution;

            if (isFile)
            {
                var doc = solution.Projects
                    .SelectMany(p => p.Documents)
                    .FirstOrDefault(d => d.FilePath?.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true);

                if (doc != null)
                {
                    documentsToFix.Add(doc);
                }
            }
            else
            {
                // Directory - find all documents under it
                documentsToFix.AddRange(
                    solution.Projects
                        .SelectMany(p => p.Documents)
                        .Where(d => d.FilePath?.StartsWith(fullPath, StringComparison.OrdinalIgnoreCase) == true));
            }

            if (documentsToFix.Count == 0)
            {
                return new ToolCallResult
                {
                    Success = true,
                    Result = new
                    {
                        preview,
                        fixes_applied = 0,
                        fixes_available = 0,
                        message = "No documents found in workspace for the specified path",
                        changes = Array.Empty<object>()
                    }
                };
            }

            // Apply fixes for each provider
            var allChanges = new List<object>();
            var totalFixesApplied = 0;
            var totalFixesAvailable = 0;

            foreach (var provider in providers)
            {
                var result = await provider.ApplyBatchFixesAsync(
                    documentsToFix.ToImmutableArray(),
                    config,
                    null,
                    ct);

                totalFixesAvailable += result.TotalDiagnostics;

                if (!preview)
                {
                    totalFixesApplied += result.TotalFixed;
                }

                // Collect change previews
                foreach (var docResult in result.DocumentResults)
                {
                    if (docResult.Modified && docResult.DiagnosticsFixed > 0)
                    {
                        allChanges.Add(new
                        {
                            file_path = docResult.FilePath,
                            provider = provider.Name,
                            diagnostics_fixed = docResult.DiagnosticsFixed,
                            preview_note = preview ? "Preview only - changes not applied" : "Changes applied"
                        });
                    }
                }
            }

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    preview,
                    fixes_applied = totalFixesApplied,
                    fixes_available = totalFixesAvailable,
                    changes = allChanges,
                    documents_processed = documentsToFix.Count,
                    providers_used = providers.Length,
                    backup_created = createBackup && !preview
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = $"{ex.Message}\n{ex.StackTrace}"
            };
        }
    }

    /// <summary>
    /// List available fix profiles and their capabilities
    /// </summary>
    private Task<ToolCallResult> ExecuteCodeQualityProfiles(JsonDocument arguments, CancellationToken ct)
    {
        try
        {
            if (_codeFixRegistry == null)
            {
                return Task.FromResult(new ToolCallResult
                {
                    Success = false,
                    Error = "Code fix registry not initialized. This feature requires BuiltInCodeFixRegistry."
                });
            }

            var statistics = _codeFixRegistry.GetStatistics();
            var allProviders = _codeFixRegistry.GetAllProviders();

            var profiles = new List<object>
            {
                new
                {
                    name = "Conservative",
                    description = "Safe, low-risk fixes only",
                    auto_apply = true,
                    providers = allProviders
                        .Where(p => p.Profile == FixProfile.Conservative)
                        .Select(p => p.Name)
                        .ToArray(),
                    example_fixes = allProviders
                        .Where(p => p.Profile == FixProfile.Conservative)
                        .Select(p => p.Description)
                        .ToArray()
                },
                new
                {
                    name = "Balanced",
                    description = "Moderate fixes with preview",
                    auto_apply = false,
                    providers = allProviders
                        .Where(p => p.Profile == FixProfile.Balanced)
                        .Select(p => p.Name)
                        .ToArray(),
                    example_fixes = allProviders
                        .Where(p => p.Profile == FixProfile.Balanced)
                        .Select(p => p.Description)
                        .ToArray()
                },
                new
                {
                    name = "Aggressive",
                    description = "All fixes including structural changes",
                    auto_apply = false,
                    providers = allProviders
                        .Where(p => p.Profile == FixProfile.Aggressive)
                        .Select(p => p.Name)
                        .ToArray(),
                    example_fixes = allProviders
                        .Where(p => p.Profile == FixProfile.Aggressive)
                        .Select(p => p.Description)
                        .ToArray()
                }
            };

            return Task.FromResult(new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    profiles = profiles,
                    total_providers = statistics.TotalProviders,
                    fully_automated_count = statistics.FullyAutomatedCount,
                    total_fixable_diagnostics = statistics.TotalFixableDiagnostics,
                    providers_by_profile = statistics.ProvidersByProfile
                }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolCallResult
            {
                Success = false,
                Error = $"{ex.Message}\n{ex.StackTrace}"
            });
        }
    }
}

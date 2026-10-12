using System.Collections.Immutable;
using System.Text.Json;
using MCPsharp.Models;
using MCPsharp.Services.Analyzers.BuiltIn.CodeFixes.Base;
using Microsoft.CodeAnalysis;

namespace MCPsharp.Services;

/// <summary>
/// Automated code fix application tool (code_quality_fix)
/// </summary>
public partial class McpToolRegistry
{
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
            var preview = !root.TryGetProperty("preview", out var previewProp) || previewProp.GetBoolean();
            var createBackup = !root.TryGetProperty("create_backup", out var backupProp) || backupProp.GetBoolean();

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
            // One document per file, even for multi-targeted projects
            var allDocuments = _workspace.GetAllDocuments();

            if (isFile)
            {
                var doc = allDocuments
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
                    allDocuments
                        .Where(d => d.FilePath != null && PathContainment.IsWithin(fullPath, d.FilePath)));
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
            LogToolOperationFailed(_logger, ex, nameof(ExecuteCodeQualityFix));
            return new ToolCallResult
            {
                Success = false,
                Error = $"{ex.Message}\n{ex.StackTrace}"
            };
        }
    }
}

using System.Text.Json;
using MCPsharp.Models;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services;

/// <summary>
/// Partial class for configuration-related MCP tools
/// </summary>
public partial class McpToolRegistry
{
    /// <summary>
    /// Get the schema of a configuration file (JSON/YAML)
    /// </summary>
    /// <param name="arguments">Tool arguments containing configPath parameter</param>
    /// <returns>Tool execution result with configuration schema information</returns>
    private async Task<ToolCallResult> ExecuteGetConfigSchema(JsonDocument arguments)
    {
        if (_configAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. ConfigAnalyzerService is not available."
            };
        }

        try
        {
            var configPath = arguments.RootElement.GetProperty("configPath").GetString();

            if (string.IsNullOrEmpty(configPath))
            {
                return new ToolCallResult { Success = false, Error = "ConfigPath is required" };
            }

            // Validate file exists
            if (!File.Exists(configPath))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Configuration file not found: {configPath}"
                };
            }

            // Determine file type from extension
            var fileExtension = Path.GetExtension(configPath).ToLowerInvariant();
            var supportedExtensions = new[] { ".json", ".yaml", ".yml", ".xml", ".config" };

            if (!supportedExtensions.Contains(fileExtension))
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Unsupported configuration file type: {fileExtension}. Supported types: {string.Join(", ", supportedExtensions)}"
                };
            }

            var schema = await _configAnalyzer.GetConfigSchemaAsync(configPath);

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    configPath = configPath,
                    fileType = fileExtension,
                    schema = schema,
                    analyzedAt = DateTime.UtcNow
                }
            };
        }
        catch (FileNotFoundException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Configuration file not found: {ex.Message}" };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Access denied to configuration file: {ex.Message}" };
        }
        catch (JsonException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Invalid JSON/YAML format in configuration file: {ex.Message}" };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Feature not yet implemented: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error analyzing configuration schema: {ex.Message}" };
        }
    }

    /// <summary>
    /// Merge multiple configuration files
    /// </summary>
    /// <param name="arguments">Tool arguments containing configPaths array</param>
    /// <returns>Tool execution result with merged configuration</returns>
    private async Task<ToolCallResult> ExecuteMergeConfigs(JsonDocument arguments)
    {
        if (_configAnalyzer == null)
        {
            return new ToolCallResult
            {
                Success = false,
                Error = "This tool requires Phase 2 features. ConfigAnalyzerService is not available."
            };
        }

        try
        {
            // Parse config paths array
            if (!arguments.RootElement.TryGetProperty("configPaths", out var configPathsElement))
            {
                return new ToolCallResult { Success = false, Error = "ConfigPaths array is required" };
            }

            var configPaths = new List<string>();
            foreach (var pathElement in configPathsElement.EnumerateArray())
            {
                var path = pathElement.GetString();
                if (!string.IsNullOrEmpty(path))
                {
                    configPaths.Add(path);
                }
            }

            if (configPaths.Count == 0)
            {
                return new ToolCallResult { Success = false, Error = "At least one config path is required" };
            }

            // Validate all files exist and are accessible
            var missingFiles = new List<string>();
            var inaccessibleFiles = new List<string>();
            var supportedExtensions = new[] { ".json", ".yaml", ".yml", ".xml", ".config" };

            foreach (var configPath in configPaths)
            {
                if (!File.Exists(configPath))
                {
                    missingFiles.Add(configPath);
                    continue;
                }

                try
                {
                    // Test file access
                    using var fs = File.OpenRead(configPath);
                }
                catch (UnauthorizedAccessException)
                {
                    inaccessibleFiles.Add(configPath);
                }

                var fileExtension = Path.GetExtension(configPath).ToLowerInvariant();
                if (!supportedExtensions.Contains(fileExtension))
                {
                    return new ToolCallResult
                    {
                        Success = false,
                        Error = $"Unsupported configuration file type: {fileExtension} in file {configPath}. Supported types: {string.Join(", ", supportedExtensions)}"
                    };
                }
            }

            if (missingFiles.Count > 0)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Configuration files not found: {string.Join(", ", missingFiles)}"
                };
            }

            if (inaccessibleFiles.Count > 0)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Access denied to configuration files: {string.Join(", ", inaccessibleFiles)}"
                };
            }

            // Validate that all files are of compatible types
            var fileTypes = configPaths.Select(p => Path.GetExtension(p).ToLowerInvariant()).Distinct().ToList();
            if (fileTypes.Count > 1)
            {
                return new ToolCallResult
                {
                    Success = false,
                    Error = $"Cannot merge configuration files of different types. Found: {string.Join(", ", fileTypes)}"
                };
            }

            var mergedConfig = await _configAnalyzer.MergeConfigsAsync(configPaths.ToArray());

            return new ToolCallResult
            {
                Success = true,
                Result = new
                {
                    mergedConfig = mergedConfig,
                    sourceFiles = configPaths,
                    fileType = fileTypes.First(),
                    mergedAt = DateTime.UtcNow,
                    filesCount = configPaths.Count
                }
            };
        }
        catch (FileNotFoundException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Configuration file not found: {ex.Message}" };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Access denied to configuration file: {ex.Message}" };
        }
        catch (JsonException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Invalid JSON/YAML format in configuration file: {ex.Message}" };
        }
        catch (InvalidOperationException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Configuration merge conflict: {ex.Message}" };
        }
        catch (NotImplementedException ex)
        {
            return new ToolCallResult { Success = false, Error = $"Feature not yet implemented: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ToolCallResult { Success = false, Error = $"Error merging configuration files: {ex.Message}" };
        }
    }
}
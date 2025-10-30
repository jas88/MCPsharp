using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCPsharp.Models;

namespace MCPsharp.Services;

/// <summary>
/// Implementation of IConfigAnalyzerService for analyzing configuration files
/// </summary>
public class ConfigAnalyzerService : IConfigAnalyzerService
{
    public Task<ConfigSchema> GetConfigSchemaAsync(string configPath)
    {
        throw new NotImplementedException("ConfigAnalyzerService.GetConfigSchemaAsync not yet fully implemented");
    }

    public Task<MergedConfig> MergeConfigsAsync(string[] configPaths)
    {
        throw new NotImplementedException("ConfigAnalyzerService.MergeConfigsAsync not yet fully implemented");
    }

    /// <summary>
    /// Get all configuration keys from config files in the project root
    /// </summary>
    public async Task<IReadOnlyList<ConfigurationKey>> GetConfigurationKeysAsync(
        string projectRoot,
        CancellationToken ct = default)
    {
        var keys = new List<ConfigurationKey>();

        // Find all JSON config files
        var configFiles = Directory.EnumerateFiles(projectRoot, "*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") &&
                       !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .ToList();

        foreach (var configFile in configFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(configFile, ct);
                var jsonKeys = ExtractJsonKeys(content, configFile);
                keys.AddRange(jsonKeys);
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        return keys;
    }

    private List<ConfigurationKey> ExtractJsonKeys(string json, string filePath)
    {
        var keys = new List<ConfigurationKey>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            ExtractKeysRecursive(doc.RootElement, "", filePath, keys);
        }
        catch
        {
            // Skip invalid JSON
        }

        return keys;
    }

    private void ExtractKeysRecursive(
        JsonElement element,
        string prefix,
        string filePath,
        List<ConfigurationKey> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix)
                    ? property.Name
                    : $"{prefix}:{property.Name}";

                keys.Add(new ConfigurationKey
                {
                    Key = key,
                    FilePath = filePath,
                    Value = property.Value.ToString()
                });

                ExtractKeysRecursive(property.Value, key, filePath, keys);
            }
        }
    }
}

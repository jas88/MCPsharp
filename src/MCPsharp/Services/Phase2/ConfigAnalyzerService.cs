using System.Text.Json;
using MCPsharp.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCPsharp.Services.Phase2;

/// <summary>
/// Analyzes JSON/YAML configuration files
/// </summary>
public class ConfigAnalyzerService : IConfigAnalyzerService
{
    public async Task<ConfigSchema> GetConfigSchemaAsync(string configPath)
    {
        var content = await File.ReadAllTextAsync(configPath);
        var extension = Path.GetExtension(configPath).ToLowerInvariant();

        Dictionary<string, object> settings;

        if (extension == ".json")
        {
            settings = ParseJsonConfig(content);
        }
        else if (extension is ".yml" or ".yaml")
        {
            settings = ParseYamlConfig(content);
        }
        else
        {
            throw new NotSupportedException($"Unsupported config format: {extension}");
        }

        var properties = new Dictionary<string, ConfigProperty>();
        FlattenToProperties(settings, "", properties);

        return new ConfigSchema
        {
            FilePath = configPath,
            Properties = properties
        };
    }

    public async Task<MergedConfig> MergeConfigsAsync(string[] configPaths)
    {
        var mergedSettings = new Dictionary<string, object>();
        var conflicts = new List<ConfigConflict>();

        foreach (var configPath in configPaths)
        {
            var schema = await GetConfigSchemaAsync(configPath);

            foreach (var prop in schema.Properties)
            {
                if (mergedSettings.ContainsKey(prop.Key))
                {
                    // Conflict detected
                    var existingValue = mergedSettings[prop.Key];
                    var newValue = prop.Value.DefaultValue ?? "";

                    if (!Equals(existingValue, newValue))
                    {
                        conflicts.Add(new ConfigConflict
                        {
                            Key = prop.Key,
                            File1 = configPaths[0],
                            Value1 = existingValue,
                            File2 = configPath,
                            Value2 = newValue
                        });
                    }
                }
                else
                {
                    mergedSettings[prop.Key] = prop.Value.DefaultValue ?? "";
                }
            }
        }

        return new MergedConfig
        {
            SourceFiles = configPaths,
            MergedSettings = mergedSettings,
            Conflicts = conflicts.Any() ? conflicts : null
        };
    }

    private Dictionary<string, object> ParseJsonConfig(string content)
    {
        var settings = new Dictionary<string, object>();

        try
        {
            var jsonDoc = JsonDocument.Parse(content);
            FlattenJson(jsonDoc.RootElement, "", settings);
        }
        catch
        {
            // If parsing fails, return empty
        }

        return settings;
    }

    private void FlattenJson(JsonElement element, string prefix, Dictionary<string, object> settings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}:{property.Name}";
                    FlattenJson(property.Value, key, settings);
                }
                break;

            case JsonValueKind.Array:
                settings[prefix] = $"[Array with {element.GetArrayLength()} items]";
                break;

            case JsonValueKind.String:
                settings[prefix] = element.GetString() ?? "";
                break;

            case JsonValueKind.Number:
                settings[prefix] = element.GetDouble();
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                settings[prefix] = element.GetBoolean();
                break;
        }
    }

    private Dictionary<string, object> ParseYamlConfig(string content)
    {
        var settings = new Dictionary<string, object>();

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var yamlObject = deserializer.Deserialize<Dictionary<string, object>>(content);
            if (yamlObject != null)
            {
                FlattenYaml(yamlObject, "", settings);
            }
        }
        catch
        {
            // If parsing fails, return empty
        }

        return settings;
    }

    private void FlattenYaml(object? obj, string prefix, Dictionary<string, object> settings)
    {
        if (obj is Dictionary<object, object> dict)
        {
            foreach (var kvp in dict)
            {
                var key = kvp.Key.ToString() ?? "";
                var fullKey = string.IsNullOrEmpty(prefix) ? key : $"{prefix}:{key}";

                if (kvp.Value is Dictionary<object, object>)
                {
                    FlattenYaml(kvp.Value, fullKey, settings);
                }
                else
                {
                    settings[fullKey] = kvp.Value ?? "";
                }
            }
        }
        else if (obj is List<object> list)
        {
            settings[prefix] = $"[Array with {list.Count} items]";
        }
        else if (obj != null)
        {
            settings[prefix] = obj;
        }
    }

    private void FlattenToProperties(Dictionary<string, object> settings, string prefix, Dictionary<string, ConfigProperty> properties)
    {
        foreach (var kvp in settings)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";

            properties[key] = new ConfigProperty
            {
                Path = key,
                Type = kvp.Value?.GetType().Name ?? "null",
                DefaultValue = kvp.Value,
                IsSensitive = IsSensitiveKey(key)
            };
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var sensitiveKeywords = new[] { "password", "secret", "token", "key", "credential", "apikey" };
        return sensitiveKeywords.Any(keyword => key.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

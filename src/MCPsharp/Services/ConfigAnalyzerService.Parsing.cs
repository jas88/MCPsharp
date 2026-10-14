using System.Text.Json;
using MCPsharp.Models;

namespace MCPsharp.Services;

public partial class ConfigAnalyzerService
{
    private static List<ConfigurationKey> ExtractJsonKeys(string json, string filePath)
    {
        var keys = new List<ConfigurationKey>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            ExtractKeysRecursive(doc.RootElement, "", filePath, keys);
        }
        catch (JsonException)
        {
            // Skip invalid JSON
        }

        return keys;
    }

    private static void ExtractKeysRecursive(
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

    /// <summary>
    /// Parses a JSON configuration file into a dictionary.
    /// </summary>
    private Task<Dictionary<string, object>> ParseJsonConfigAsync(string content, string filePath)
    {
        try
        {
            LogParsingJSONConfigurationFile(_logger, filePath);

            using var document = JsonDocument.Parse(content);
            var result = new Dictionary<string, object>();

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                ConvertJsonElementToDictionary(document.RootElement, result, "");
            }

            LogSuccessfullyParsedJSONConfigurationWith(_logger, result.Count);

            return Task.FromResult(result);
        }
        catch (JsonException ex)
        {
            LogFailedToParseJSONConfiguration(_logger, ex, filePath);
            throw new InvalidOperationException($"Invalid JSON in configuration file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Parses a YAML configuration file into a dictionary.
    /// </summary>
    private Task<Dictionary<string, object>> ParseYamlConfigAsync(string content, string filePath)
    {
        try
        {
            LogParsingYAMLConfigurationFile(_logger, filePath);

            var yamlObject = _yamlDeserializer.Deserialize<Dictionary<string, object>>(content);
            var result = yamlObject ?? new Dictionary<string, object>();

            LogSuccessfullyParsedYAMLConfigurationWith(_logger, result.Count);

            return Task.FromResult(result);
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            LogFailedToParseYAMLConfiguration(_logger, ex, filePath);
            throw new InvalidOperationException($"Invalid YAML in configuration file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Converts a JsonElement to a dictionary recursively.
    /// </summary>
    private void ConvertJsonElementToDictionary(JsonElement element, Dictionary<string, object> target, string prefix)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}:{property.Name}";

                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Object:
                        var nestedDict = new Dictionary<string, object>();
                        ConvertJsonElementToDictionary(property.Value, nestedDict, key);
                        target[key] = nestedDict;
                        break;

                    case JsonValueKind.Array:
                        var array = new List<object>();
                        foreach (var arrayItem in property.Value.EnumerateArray())
                        {
                            array.Add(ConvertJsonElementToObject(arrayItem));
                        }
                        target[key] = array;
                        break;

                    case JsonValueKind.String:
                        target[key] = property.Value.GetString() ?? string.Empty;
                        break;

                    case JsonValueKind.Number:
                        if (property.Value.TryGetInt32(out var intValue))
                            target[key] = intValue;
                        else if (property.Value.TryGetInt64(out var longValue))
                            target[key] = longValue;
                        else if (property.Value.TryGetDouble(out var doubleValue))
                            target[key] = doubleValue;
                        else
                            target[key] = property.Value.GetDecimal();
                        break;

                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        target[key] = property.Value.GetBoolean();
                        break;

                    case JsonValueKind.Null:
                        target[key] = null!;
                        break;

                    default:
                        target[key] = property.Value.ToString();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Converts a JsonElement to an object.
    /// </summary>
    private object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => ConvertJsonNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToList(),
            JsonValueKind.Object => ConvertJsonElementToObjectDictionary(element),
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Converts a JsonElement object to a Dictionary.
    /// </summary>
    private Dictionary<string, object> ConvertJsonElementToObjectDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        ConvertJsonElementToDictionary(element, dict, "");
        return dict;
    }

    /// <summary>
    /// Extracts configuration properties with metadata from a configuration dictionary.
    /// </summary>
    private static Dictionary<string, ConfigProperty> ExtractConfigProperties(Dictionary<string, object> configData)
    {
        var properties = new Dictionary<string, ConfigProperty>();
        var flatConfig = FlattenConfig(configData);

        foreach (var kvp in flatConfig)
        {
            properties[kvp.Key] = new ConfigProperty
            {
                Path = kvp.Key,
                Type = GetPropertyType(kvp.Value),
                DefaultValue = kvp.Value,
                IsSensitive = IsSensitiveKey(kvp.Key)
            };
        }

        return properties;
    }

    /// <summary>
    /// Flattens a nested configuration dictionary into dot-notation paths.
    /// </summary>
    private static Dictionary<string, object> FlattenConfig(Dictionary<string, object> config, string prefix = "")
    {
        var result = new Dictionary<string, object>();

        foreach (var kvp in config)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}:{kvp.Key}";

            if (kvp.Value is Dictionary<string, object> nestedDict)
            {
                var nestedResult = FlattenConfig(nestedDict, key);
                foreach (var nestedKvp in nestedResult)
                {
                    result[nestedKvp.Key] = nestedKvp.Value;
                }
            }
            else
            {
                result[key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Determines the type of a configuration property value.
    /// </summary>
    private static string GetPropertyType(object value)
    {
        return value switch
        {
            null => "null",
            string => "string",
            int or long or short or byte => "integer",
            float or double or decimal => "number",
            bool => "boolean",
            Dictionary<string, object> => "object",
            List<object> => "array",
            _ => value.GetType().Name.ToLowerInvariant()
        };
    }

    /// <summary>
    /// Determines if a configuration key is likely to contain sensitive information.
    /// </summary>
    private static bool IsSensitiveKey(string key)
    {
        var keyParts = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return keyParts.Any(part =>
            SensitiveKeyPatterns.Any(pattern =>
                part.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Compares two configuration values for equality.
    /// </summary>
    private static bool AreValuesEqual(object value1, object value2)
    {
        if (value1 == null && value2 == null)
            return true;
        if (value1 == null || value2 == null)
            return false;

        // Convert both to string for comparison to handle type differences
        return string.Equals(value1.ToString(), value2.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static object ConvertJsonNumber(JsonElement element)
    {
        if (element.TryGetInt32(out var intValue))
            return intValue;
        if (element.TryGetInt64(out var longValue))
            return longValue;
        if (element.TryGetDouble(out var doubleValue))
            return doubleValue;
        return element.GetDecimal();
    }

    /// <summary>
    /// Checks if a configuration file contains a specific key (helper for conflict detection).
    /// </summary>
    private static bool ContainsConfigKey(string filePath, string key)
    {
        try
        {
            // This is a simplified check - in a real implementation, you might want to cache the parsed configs
            var content = File.ReadAllText(filePath);
            var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

            return fileExtension switch
            {
                ".json" => content.Contains($"\"{key.Replace(":", "\":\"")}", StringComparison.OrdinalIgnoreCase),
                ".yml" or ".yaml" => content.Contains(key.Replace(":", ":"), StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

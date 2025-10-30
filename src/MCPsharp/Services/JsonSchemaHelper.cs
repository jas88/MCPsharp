using System.Text.Json;

namespace MCPsharp.Services;

/// <summary>
/// Helper for building JSON Schema documents for MCP tool parameters
/// </summary>
public static class JsonSchemaHelper
{
    /// <summary>
    /// Create a JSON Schema document for object properties
    /// </summary>
    /// <param name="properties">Array of property definitions (name, type, description, required)</param>
    /// <returns>JSON Schema document following Draft 2020-12</returns>
    public static JsonDocument CreateSchema(params PropertyDefinition[] properties)
    {
        var schema = new Dictionary<string, object>
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["type"] = "object",
            ["properties"] = properties.ToDictionary(
                p => p.Name,
                p => CreatePropertySchema(p)
            )
        };

        var required = properties.Where(p => p.Required).Select(p => p.Name).ToArray();
        if (required.Length > 0)
        {
            schema["required"] = required;
        }

        return JsonDocument.Parse(JsonSerializer.Serialize(schema));
    }

    private static Dictionary<string, object> CreatePropertySchema(PropertyDefinition property)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = property.Type,
            ["description"] = property.Description
        };

        if (property.Default != null)
        {
            schema["default"] = property.Default;
        }

        if (property.Enum != null && property.Enum.Length > 0)
        {
            schema["enum"] = property.Enum;
        }

        if (property.Items != null)
        {
            schema["items"] = property.Items;
        }

        return schema;
    }
}

/// <summary>
/// Definition of a JSON Schema property
/// </summary>
public class PropertyDefinition
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Description { get; init; }
    public bool Required { get; init; }
    public object? Default { get; init; }
    public string[]? Enum { get; init; }
    public object? Items { get; init; }
}

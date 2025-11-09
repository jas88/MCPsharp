using System.Text;
using System.Text.Json;

namespace MCPsharp.Services;

/// <summary>
/// Helper methods for MCP tool registry
/// </summary>
public partial class McpToolRegistry
{
    private string GetProjectPathFromArguments(JsonDocument arguments)
    {
        var context = _projectContext.GetProjectContext();
        if (arguments.RootElement.TryGetProperty("projectPath", out var projectPathElement) &&
            !string.IsNullOrEmpty(projectPathElement.GetString()))
        {
            return projectPathElement.GetString()!;
        }
        return context?.RootPath ?? Environment.CurrentDirectory;
    }

    #region Additional Helper Methods for Duplicate Code Detection

    private JsonElement? GetPropertyFromArguments(JsonDocument arguments, string propertyName)
    {
        if (arguments.RootElement.TryGetProperty(propertyName, out var propertyElement))
        {
            return propertyElement;
        }
        return null;
    }

    private double GetDoubleFromArguments(JsonDocument arguments, string propertyName, double defaultValue)
    {
        if (arguments.RootElement.TryGetProperty(propertyName, out var propertyElement) &&
            propertyElement.ValueKind == JsonValueKind.Number &&
            propertyElement.TryGetDouble(out var value))
        {
            return value;
        }
        return defaultValue;
    }

    private string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private string? GetStringArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            return propertyElement.ValueKind == JsonValueKind.String ? propertyElement.GetString() : propertyElement.ToString();
        }
        return null;
    }

    private bool? GetBoolArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            if (propertyElement.ValueKind == JsonValueKind.True || propertyElement.ValueKind == JsonValueKind.False)
            {
                return propertyElement.GetBoolean();
            }
            if (propertyElement.ValueKind == JsonValueKind.String &&
                bool.TryParse(propertyElement.GetString(), out var boolValue))
            {
                return boolValue;
            }
        }
        return null;
    }

    private int? GetIntArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetInt32(out var intValue))
            {
                return intValue;
            }
            if (propertyElement.ValueKind == JsonValueKind.String &&
                int.TryParse(propertyElement.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
    }

    private double? GetDoubleArgument(JsonDocument arguments, string parameterName)
    {
        if (arguments.RootElement.TryGetProperty(parameterName, out var propertyElement))
        {
            if (propertyElement.ValueKind == JsonValueKind.Number && propertyElement.TryGetDouble(out var doubleValue))
            {
                return doubleValue;
            }
            if (propertyElement.ValueKind == JsonValueKind.String &&
                double.TryParse(propertyElement.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        return null;
    }

    #endregion
}

/// <summary>
/// Extension methods for JsonElement
/// </summary>
internal static class JsonElementExtensions
{
    public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var propertyElement))
        {
            return propertyElement;
        }
        return null;
    }
}

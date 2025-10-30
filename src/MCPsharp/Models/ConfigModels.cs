namespace MCPsharp.Models;

/// <summary>
/// Represents information about a parsed configuration file.
/// </summary>
public class ConfigFileInfo
{
    /// <summary>
    /// Gets the full path to the configuration file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the type of configuration file (e.g., "json", "yaml").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the parsed configuration settings as a dictionary.
    /// </summary>
    public required Dictionary<string, object> Settings { get; init; }

    /// <summary>
    /// Gets the list of keys that contain sensitive information (passwords, tokens, etc.).
    /// </summary>
    public IReadOnlyList<string>? SecretKeys { get; init; }
}

/// <summary>
/// Represents the schema of a configuration file with flattened paths.
/// </summary>
public class ConfigSchema
{
    /// <summary>
    /// Gets the full path to the configuration file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the flattened configuration properties (e.g., "Database:ConnectionString").
    /// </summary>
    public required Dictionary<string, ConfigProperty> Properties { get; init; }
}

/// <summary>
/// Represents a single configuration property with metadata.
/// </summary>
public class ConfigProperty
{
    /// <summary>
    /// Gets the dot-notation path to this property (e.g., "Database:ConnectionString").
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the type of the property value (e.g., "string", "int", "object").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the default value of the property, if any.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether this property contains sensitive information.
    /// </summary>
    public bool IsSensitive { get; init; }
}

/// <summary>
/// Represents the result of merging multiple configuration files.
/// </summary>
public class MergedConfig
{
    /// <summary>
    /// Gets the list of source configuration files that were merged.
    /// </summary>
    public required IReadOnlyList<string> SourceFiles { get; init; }

    /// <summary>
    /// Gets the merged configuration settings.
    /// </summary>
    public required Dictionary<string, object> MergedSettings { get; init; }

    /// <summary>
    /// Gets the list of conflicts encountered during merging, if any.
    /// </summary>
    public IReadOnlyList<ConfigConflict>? Conflicts { get; init; }
}

/// <summary>
/// Represents a conflict between two configuration values during merging.
/// </summary>
public class ConfigConflict
{
    /// <summary>
    /// Gets the configuration key that has conflicting values.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the path to the first configuration file.
    /// </summary>
    public required string File1 { get; init; }

    /// <summary>
    /// Gets the value from the first configuration file.
    /// </summary>
    public required object Value1 { get; init; }

    /// <summary>
    /// Gets the path to the second configuration file.
    /// </summary>
    public required string File2 { get; init; }

    /// <summary>
    /// Gets the value from the second configuration file.
    /// </summary>
    public required object Value2 { get; init; }
}

/// <summary>
/// Represents a configuration key extracted from a configuration file
/// </summary>
public class ConfigurationKey
{
    /// <summary>
    /// Gets the configuration key path (e.g., "Authentication:Provider")
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the file path containing this configuration key
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the value of the configuration key
    /// </summary>
    public required string Value { get; init; }
}

/// <summary>
/// Represents the result of configuration validation analysis.
/// </summary>
public class ConfigValidationResult
{
    /// <summary>
    /// Gets the root directory of the project that was validated.
    /// </summary>
    public required string ProjectRoot { get; init; }

    /// <summary>
    /// Gets the list of configuration files that were analyzed.
    /// </summary>
    public required IReadOnlyList<string> ConfigurationFiles { get; init; }

    /// <summary>
    /// Gets the list of validation issues found during analysis.
    /// </summary>
    public required IList<ConfigValidationIssue> Issues { get; init; }

    /// <summary>
    /// Gets a value indicating whether the validation passed without any issues.
    /// </summary>
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);

    /// <summary>
    /// Gets the count of issues by severity level.
    /// </summary>
    public Dictionary<ValidationSeverity, int> IssueCounts => Issues
        .GroupBy(i => i.Severity)
        .ToDictionary(g => g.Key, g => g.Count());
}

/// <summary>
/// Represents a validation issue found in configuration analysis.
/// </summary>
public class ConfigValidationIssue
{
    /// <summary>
    /// Gets the severity level of this validation issue.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the descriptive message for this validation issue.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the file path where this issue was found (optional).
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// Gets additional details about this issue (optional).
    /// </summary>
    public IReadOnlyList<string>? Details { get; init; }
}


namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Represents a method signature with parameter and return type information
/// </summary>
public class MethodSignature
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public List<ParameterInfo> Parameters { get; init; } = new();
    public required string ContainingType { get; init; }
    public required string Accessibility { get; init; }
    public bool IsStatic { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsOverride { get; init; }
    public bool IsExtension { get; init; }
    public bool IsAsync { get; init; }
    public string? FullyQualifiedName { get; init; }

    /// <summary>
    /// Display string for the method signature
    /// </summary>
    public string DisplayName => ToString();

    public override string ToString()
    {
        var paramStr = string.Join(", ", Parameters.Select(p => $"{p.Type}{(p.IsOptional ? "?" : "")} {p.Name}{(p.HasDefaultValue ? $" = {p.DefaultValue}" : "")}"));
        var accessibility = string.IsNullOrEmpty(Accessibility) ? "" : $"{Accessibility} ";
        var modifiers = new List<string>();

        if (IsStatic) modifiers.Add("static");
        if (IsVirtual) modifiers.Add("virtual");
        if (IsAbstract) modifiers.Add("abstract");
        if (IsOverride) modifiers.Add("override");
        if (IsExtension) modifiers.Add("this");
        if (IsAsync) modifiers.Add("async");

        var modifierStr = modifiers.Count > 0 ? $"{string.Join(" ", modifiers)} " : "";

        return $"{modifierStr}{accessibility}{ReturnType} {Name}({paramStr})";
    }

    /// <summary>
    /// Check if this signature matches another (for overload resolution)
    /// </summary>
    public bool Matches(MethodSignature other, bool exactMatch = false)
    {
        if (Name != other.Name || ReturnType != other.ReturnType)
            return false;

        if (Parameters.Count != other.Parameters.Count)
            return false;

        for (int i = 0; i < Parameters.Count; i++)
        {
            var thisParam = Parameters[i];
            var otherParam = other.Parameters[i];

            if (exactMatch)
            {
                if (thisParam.Type != otherParam.Type ||
                    thisParam.IsOptional != otherParam.IsOptional ||
                    thisParam.HasDefaultValue != otherParam.HasDefaultValue)
                    return false;
            }
            else
            {
                // For non-exact matching, allow compatible types (e.g., int vs long)
                if (!AreTypesCompatible(thisParam.Type, otherParam.Type))
                    return false;
            }
        }

        return true;
    }

    private static bool AreTypesCompatible(string type1, string type2)
    {
        // Exact match is always compatible
        if (type1 == type2) return true;

        // object is compatible with all types
        if (type1 == "object" || type2 == "object") return true;

        // Built-in type conversions (implicit numeric conversions)
        var numericConversions = new Dictionary<string, HashSet<string>>
        {
            ["int"] = new HashSet<string> { "long", "double", "float", "decimal" },
            ["long"] = new HashSet<string> { "double", "float", "decimal" },
            ["float"] = new HashSet<string> { "double" },
            ["double"] = new HashSet<string>(), // Double is the largest numeric type
            ["decimal"] = new HashSet<string>() // Decimal is separate from floating point
        };

        if (numericConversions.TryGetValue(type1, out var compatibleTypes) && compatibleTypes.Contains(type2))
            return true;

        // Handle generic types - they must have the same generic root
        if (type1.Contains("<") && type2.Contains("<"))
        {
            var generic1 = type1.Substring(0, type1.IndexOf('<'));
            var generic2 = type2.Substring(0, type2.IndexOf('<'));
            return generic1 == generic2;
        }

        return false;
    }
}

/// <summary>
/// Parameter information for method signatures
/// </summary>
public class ParameterInfo
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public bool IsOptional { get; init; }
    public bool IsOut { get; init; }
    public bool IsRef { get; init; }
    public bool IsParams { get; init; }
    public string? DefaultValue { get; init; }
    public int Position { get; init; }
    public bool HasDefaultValue => !string.IsNullOrEmpty(DefaultValue);
}
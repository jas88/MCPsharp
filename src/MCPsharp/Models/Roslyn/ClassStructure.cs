namespace MCPsharp.Models.Roslyn;

/// <summary>
/// Complete structure of a class
/// </summary>
public class ClassStructure
{
    public required string Name { get; init; }
    public required string Namespace { get; init; }
    public required string Kind { get; init; }
    public required string Accessibility { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public bool IsStatic { get; init; }
    public List<string> BaseTypes { get; init; } = new();
    public List<string> Interfaces { get; init; } = new();
    public List<PropertyStructure> Properties { get; init; } = new();
    public List<MethodStructure> Methods { get; init; } = new();
    public List<FieldStructure> Fields { get; init; } = new();
    public LocationInfo? Location { get; init; }
}

/// <summary>
/// Property information
/// </summary>
public class PropertyStructure
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Accessibility { get; init; }
    public bool HasGetter { get; init; }
    public bool HasSetter { get; init; }
    public int Line { get; init; }
}

/// <summary>
/// Method information
/// </summary>
public class MethodStructure
{
    public required string Name { get; init; }
    public required string ReturnType { get; init; }
    public required string Accessibility { get; init; }
    public bool IsOverride { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsStatic { get; init; }
    public List<ParameterStructure> Parameters { get; init; } = new();
    public int Line { get; init; }
}

/// <summary>
/// Field information
/// </summary>
public class FieldStructure
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Accessibility { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsConst { get; init; }
    public bool IsStatic { get; init; }
    public int Line { get; init; }
}

/// <summary>
/// Parameter information
/// </summary>
public class ParameterStructure
{
    public required string Name { get; init; }
    public required string Type { get; init; }
}

/// <summary>
/// Location information
/// </summary>
public class LocationInfo
{
    public int StartLine { get; init; }
    public int EndLine { get; init; }
}

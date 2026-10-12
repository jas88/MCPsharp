using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Request to rename a symbol
/// </summary>
public class RenameRequest
{
    public required string OldName { get; init; }
    public required string NewName { get; init; }
    public SymbolKind SymbolKind { get; init; } = SymbolKind.Any;
    public string? ContainingType { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
    public bool RenameInComments { get; init; } = true;
    public bool RenameInStrings { get; init; }
    public bool RenameOverloads { get; init; }
    public bool PreviewOnly { get; init; }
    public bool ForcePublicApiChange { get; init; }
    public bool AllowKeywordNames { get; init; }
    public ConflictHandling HandleConflicts { get; init; } = ConflictHandling.Abort;
}

/// <summary>
/// Result of rename validation
/// </summary>
public class RenameValidation
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public ISymbol? Symbol { get; init; }
}

/// <summary>
/// Result of a rename operation
/// </summary>
public class RenameResult
{
    public bool Success { get; init; }
    public int RenamedCount { get; init; }
    public List<string> FilesModified { get; init; } = new();
    public List<RenameConflict> Conflicts { get; init; } = new();
    public List<FileChange>? Preview { get; init; }
    public List<string> Errors { get; init; } = new();

    public static RenameResult Failed(IEnumerable<string> errors)
    {
        return new RenameResult
        {
            Success = false,
            Errors = errors.ToList()
        };
    }

    public static RenameResult Blocked(IEnumerable<RenameConflict> conflicts)
    {
        return new RenameResult
        {
            Success = false,
            Conflicts = conflicts.ToList()
        };
    }
}

/// <summary>
/// Represents a file change in preview mode
/// </summary>
public class FileChange
{
    public required string FilePath { get; init; }
    public required List<TextChange> Changes { get; init; }
}

/// <summary>
/// Represents a text change
/// </summary>
public class TextChange
{
    public required TextSpan Span { get; init; }
    public required string NewText { get; init; }
}

/// <summary>
/// Types of symbols that can be renamed
/// </summary>
public enum SymbolKind
{
    Any,
    Class,
    Interface,
    Method,
    Property,
    Field,
    Parameter,
    Namespace,
    Local,
    TypeParameter
}

/// <summary>
/// Conflict handling strategy
/// </summary>
public enum ConflictHandling
{
    Abort,
    Prompt,
    AutoResolve
}

/// <summary>
/// Represents a rename conflict
/// </summary>
public class RenameConflict
{
    public required ConflictType Type { get; init; }
    public required ConflictSeverity Severity { get; init; }
    public required string Description { get; init; }
    public Location? Location { get; init; }
    public ConflictResolution Resolution { get; init; } = ConflictResolution.RequiresManualResolution;
}

/// <summary>
/// Types of conflicts
/// </summary>
public enum ConflictType
{
    NameCollision,
    HidesInheritedMember,
    BreakingOverride,
    InterfaceImplementation,
    NamespaceConflict,
    PublicApiChange,
    ImplicitReference,
    CrossLanguageReference
}

/// <summary>
/// Severity of conflicts
/// </summary>
public enum ConflictSeverity
{
    Error,
    Warning,
    Info
}

/// <summary>
/// Resolution strategy for conflicts
/// </summary>
public enum ConflictResolution
{
    RequiresManualResolution,
    AutomaticallyResolved,
    UserPromptRequired
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Roslyn;
using System.Collections.Concurrent;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for safe symbol renaming across the entire solution
/// </summary>
public class RenameSymbolService
{
    private readonly RoslynWorkspace _workspace;
    private readonly AdvancedReferenceFinderService _referenceFinder;
    private readonly SymbolQueryService _symbolQuery;
    private readonly ILogger<RenameSymbolService> _logger;

    public RenameSymbolService(
        RoslynWorkspace workspace,
        AdvancedReferenceFinderService referenceFinder,
        SymbolQueryService symbolQuery,
        ILogger<RenameSymbolService> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _referenceFinder = referenceFinder ?? throw new ArgumentNullException(nameof(referenceFinder));
        _symbolQuery = symbolQuery ?? throw new ArgumentNullException(nameof(symbolQuery));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Rename a symbol across all files in the solution
    /// </summary>
    public async Task<RenameResult> RenameSymbolAsync(
        RenameRequest request,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Starting rename of '{OldName}' to '{NewName}'",
                request.OldName, request.NewName);

            // Phase 1: Validate the rename request
            var validation = await ValidateRenameAsync(request, ct);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Rename validation failed: {Errors}",
                    string.Join(", ", validation.Errors));
                return RenameResult.Failed(validation.Errors);
            }

            // Phase 2: Find the symbol to rename
            var symbol = validation.Symbol;
            if (symbol == null)
            {
                return RenameResult.Failed(new[] { "Symbol not found" });
            }

            // Phase 3: Check for conflicts
            var conflicts = await DetectConflictsAsync(symbol, request.NewName, ct);
            if (conflicts.Any(c => c.Severity == ConflictSeverity.Error))
            {
                _logger.LogWarning("Blocking conflicts detected for rename");
                return RenameResult.Blocked(conflicts);
            }

            // Phase 4: Handle non-blocking conflicts based on strategy
            if (conflicts.Any() && request.HandleConflicts == ConflictHandling.Abort)
            {
                return RenameResult.Blocked(conflicts);
            }

            // Phase 5: Perform the rename using Roslyn's Renamer
            var renameResult = await PerformRenameAsync(symbol, request, ct);

            _logger.LogInformation("Rename completed successfully: {Count} references updated",
                renameResult.RenamedCount);

            return renameResult;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rename operation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rename operation failed");
            return RenameResult.Failed(new[] { $"Rename failed: {ex.Message}" });
        }
    }

    private async Task<RenameValidation> ValidateRenameAsync(
        RenameRequest request,
        CancellationToken ct)
    {
        var errors = new List<string>();

        // Validate new name is a valid identifier
        if (!IsValidIdentifier(request.NewName))
        {
            errors.Add($"'{request.NewName}' is not a valid C# identifier");
        }

        // Check if new name is a keyword
        if (IsKeyword(request.NewName) && !request.AllowKeywordNames)
        {
            errors.Add($"'{request.NewName}' is a C# keyword. Set AllowKeywordNames=true to use @{request.NewName}");
        }

        // Find the symbol
        var symbol = await FindSymbolAsync(request, ct);
        if (symbol == null)
        {
            errors.Add($"Symbol '{request.OldName}' not found");
            return new RenameValidation { IsValid = false, Errors = errors };
        }

        // Check if symbol can be renamed
        if (!CanRenameSymbol(symbol))
        {
            errors.Add($"Symbol '{request.OldName}' cannot be renamed (external or generated code)");
        }

        // Check public API warning
        if (IsPublicApi(symbol) && !request.ForcePublicApiChange)
        {
            errors.Add("Symbol is part of public API. Set ForcePublicApiChange=true to proceed");
        }

        return new RenameValidation
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Symbol = symbol
        };
    }

    private async Task<ISymbol?> FindSymbolAsync(RenameRequest request, CancellationToken ct)
    {
        var compilation = _workspace.GetCompilation();
        if (compilation == null)
        {
            return null;
        }

        // If we have location information, use it for precise lookup
        if (!string.IsNullOrEmpty(request.FilePath) && request.Line.HasValue && request.Column.HasValue)
        {
            var document = _workspace.GetDocument(request.FilePath);
            if (document != null)
            {
                var syntaxTree = await document.GetSyntaxTreeAsync(ct);
                if (syntaxTree != null)
                {
                    var position = syntaxTree.GetText().Lines[request.Line.Value - 1].Start + request.Column.Value;
                    var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position, ct);
                    if (symbol != null)
                    {
                        return symbol;
                    }
                }
            }
        }

        // Otherwise, search by name and kind
        var candidates = _symbolQuery.FindSymbolsByName(request.OldName);

        // Filter by symbol kind if specified
        if (request.SymbolKind != SymbolKind.Any)
        {
            candidates = FilterByKind(candidates, request.SymbolKind).ToList();
        }

        // Filter by containing type if specified
        if (!string.IsNullOrEmpty(request.ContainingType))
        {
            candidates = candidates.Where(s => s.ContainingType?.Name == request.ContainingType).ToList();
        }

        return candidates.FirstOrDefault();
    }

    private async Task<List<RenameConflict>> DetectConflictsAsync(
        ISymbol symbol,
        string newName,
        CancellationToken ct)
    {
        var conflicts = new List<RenameConflict>();

        // Check for name collision in the same scope
        if (symbol.ContainingSymbol is INamedTypeSymbol containingType)
        {
            var existingMembers = containingType.GetMembers(newName);
            foreach (var existing in existingMembers)
            {
                if (!SymbolEqualityComparer.Default.Equals(existing, symbol))
                {
                    conflicts.Add(new RenameConflict
                    {
                        Type = ConflictType.NameCollision,
                        Severity = ConflictSeverity.Error,
                        Description = $"Name '{newName}' already exists in {containingType.Name}",
                        Location = existing.Locations.FirstOrDefault()
                    });
                }
            }
        }
        else if (symbol.ContainingSymbol is INamespaceSymbol containingNamespace)
        {
            var compilation = _workspace.GetCompilation();
            if (compilation != null)
            {
                var existingSymbols = compilation.GetSymbolsWithName(n => n == newName, SymbolFilter.TypeAndMember)
                    .Where(s => SymbolEqualityComparer.Default.Equals(s.ContainingNamespace, containingNamespace));
                foreach (var existing in existingSymbols)
                {
                    if (!SymbolEqualityComparer.Default.Equals(existing, symbol))
                    {
                        conflicts.Add(new RenameConflict
                        {
                            Type = ConflictType.NameCollision,
                            Severity = ConflictSeverity.Error,
                            Description = $"Name '{newName}' already exists in namespace {containingNamespace.ToDisplayString()}",
                            Location = existing.Locations.FirstOrDefault()
                        });
                    }
                }
            }
        }

        // Check for hiding inherited members
        if (symbol.ContainingType is INamedTypeSymbol containingTypeForInheritance)
        {
            var baseType = containingTypeForInheritance.BaseType;
            while (baseType != null)
            {
                var baseMember = baseType.GetMembers(newName).FirstOrDefault();
                if (baseMember != null && !baseMember.IsSealed)
                {
                    conflicts.Add(new RenameConflict
                    {
                        Type = ConflictType.HidesInheritedMember,
                        Severity = ConflictSeverity.Warning,
                        Description = $"New name '{newName}' hides inherited member from {baseType.Name}",
                        Location = baseMember.Locations.FirstOrDefault()
                    });
                }
                baseType = baseType.BaseType;
            }
        }

        // Check interface implementation conflicts
        if (symbol is IMethodSymbol methodSymbol && methodSymbol.ContainingType != null)
        {
            foreach (var iface in methodSymbol.ContainingType.AllInterfaces)
            {
                var interfaceMethod = iface.GetMembers(newName).OfType<IMethodSymbol>().FirstOrDefault();
                if (interfaceMethod != null)
                {
                    // Check if signatures would match after rename
                    if (SignaturesMatch(methodSymbol, interfaceMethod))
                    {
                        conflicts.Add(new RenameConflict
                        {
                            Type = ConflictType.InterfaceImplementation,
                            Severity = ConflictSeverity.Info,
                            Description = $"After rename, method will implement {iface.Name}.{newName}",
                            Location = interfaceMethod.Locations.FirstOrDefault()
                        });
                    }
                }
            }
        }

        return conflicts;
    }

    private async Task<RenameResult> PerformRenameAsync(
        ISymbol symbol,
        RenameRequest request,
        CancellationToken ct)
    {
        var solution = _workspace.Solution;
        var optionSet = solution.Options;

        // Configure rename options
        if (request.RenameInComments)
        {
            optionSet = optionSet.WithChangedOption(RenameOptions.RenameInComments, true);
        }

        if (request.RenameInStrings)
        {
            optionSet = optionSet.WithChangedOption(RenameOptions.RenameInStrings, true);
        }

        if (request.RenameOverloads && symbol is IMethodSymbol)
        {
            optionSet = optionSet.WithChangedOption(RenameOptions.RenameOverloads, true);
        }

        // Track modified files
        var modifiedFiles = new HashSet<string>();
        var renamedCount = 0;

        try
        {
            // Use Roslyn's Renamer API for the actual rename
            var newSolution = await Renamer.RenameSymbolAsync(
                solution,
                symbol,
                request.NewName,
                optionSet,
                ct);

            // Calculate changes
            var changes = newSolution.GetChanges(solution);
            var fileChanges = new List<FileChange>();

            foreach (var projectChange in changes.GetProjectChanges())
            {
                foreach (var docId in projectChange.GetChangedDocuments())
                {
                    var oldDoc = solution.GetDocument(docId);
                    var newDoc = newSolution.GetDocument(docId);

                    if (oldDoc != null && newDoc != null)
                    {
                        var oldText = await oldDoc.GetTextAsync(ct);
                        var newText = await newDoc.GetTextAsync(ct);
                        var textChanges = newText.GetTextChanges(oldText);

                        if (textChanges.Any())
                        {
                            modifiedFiles.Add(oldDoc.FilePath ?? docId.ToString());
                            renamedCount += textChanges.Count;

                            if (request.PreviewOnly)
                            {
                                fileChanges.Add(new FileChange
                                {
                                    FilePath = oldDoc.FilePath ?? "",
                                    Changes = textChanges.Select(tc => new TextChange
                                    {
                                        Span = tc.Span,
                                        NewText = tc.NewText!
                                    }).ToList()
                                });
                            }
                        }
                    }
                }
            }

            // Apply changes if not preview mode
            if (!request.PreviewOnly)
            {
                if (!_workspace.Solution.Workspace.TryApplyChanges(newSolution))
                {
                    return RenameResult.Failed(new[] { "Failed to apply rename changes to workspace" });
                }
            }

            return new RenameResult
            {
                Success = true,
                RenamedCount = renamedCount,
                FilesModified = modifiedFiles.ToList(),
                Preview = request.PreviewOnly ? fileChanges : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform rename");
            throw;
        }
    }

    private bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        // Check first character
        if (!char.IsLetter(name[0]) && name[0] != '_' && name[0] != '@')
            return false;

        // Check remaining characters
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_')
                return false;
        }

        return true;
    }

    private bool IsKeyword(string name)
    {
        var keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while"
        };

        return keywords.Contains(name);
    }

    private bool CanRenameSymbol(ISymbol symbol)
    {
        // Cannot rename symbols from metadata
        if (!symbol.Locations.Any(l => l.IsInSource))
            return false;

        // Cannot rename compiler-generated symbols
        if (symbol.IsImplicitlyDeclared)
            return false;

        // Cannot rename symbols in generated code
        var attributes = symbol.GetAttributes();
        if (attributes.Any(a => a.AttributeClass?.Name == "GeneratedCodeAttribute"))
            return false;

        return true;
    }

    private bool IsPublicApi(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility == Accessibility.Public ||
               symbol.DeclaredAccessibility == Accessibility.Protected ||
               symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal;
    }

    private IEnumerable<ISymbol> FilterByKind(IEnumerable<ISymbol> symbols, SymbolKind kind)
    {
        return kind switch
        {
            SymbolKind.Class => symbols.OfType<INamedTypeSymbol>().Where(s => s.TypeKind == TypeKind.Class),
            SymbolKind.Interface => symbols.OfType<INamedTypeSymbol>().Where(s => s.TypeKind == TypeKind.Interface),
            SymbolKind.Method => symbols.OfType<IMethodSymbol>(),
            SymbolKind.Property => symbols.OfType<IPropertySymbol>(),
            SymbolKind.Field => symbols.OfType<IFieldSymbol>(),
            SymbolKind.Parameter => symbols.OfType<IParameterSymbol>(),
            SymbolKind.Namespace => symbols.OfType<INamespaceSymbol>(),
            _ => symbols
        };
    }

    private bool SignaturesMatch(IMethodSymbol method1, IMethodSymbol method2)
    {
        if (method1.Parameters.Length != method2.Parameters.Length)
            return false;

        for (int i = 0; i < method1.Parameters.Length; i++)
        {
            if (!SymbolEqualityComparer.Default.Equals(
                method1.Parameters[i].Type,
                method2.Parameters[i].Type))
            {
                return false;
            }
        }

        return SymbolEqualityComparer.Default.Equals(method1.ReturnType, method2.ReturnType);
    }
}

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
    public bool RenameInStrings { get; init; } = false;
    public bool RenameOverloads { get; init; } = false;
    public bool PreviewOnly { get; init; } = false;
    public bool ForcePublicApiChange { get; init; } = false;
    public bool AllowKeywordNames { get; init; } = false;
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
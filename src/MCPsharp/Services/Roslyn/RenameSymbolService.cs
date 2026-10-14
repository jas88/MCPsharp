using MCPsharp.Services.Roslyn.SymbolSearch;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Service for safe symbol renaming across the entire solution
/// </summary>
public partial class RenameSymbolService
{
    private static readonly string[] SymbolNotFoundErrors = { "Symbol not found" };
    private static readonly string[] ApplyChangesFailedErrors = { "Failed to apply rename changes to workspace" };

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
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

    private readonly RoslynWorkspace _workspace;
    private readonly SymbolFinderService _symbolFinder;
    private readonly ILogger<RenameSymbolService> _logger;

    public RenameSymbolService(
        RoslynWorkspace workspace,
        AdvancedReferenceFinderService referenceFinder,
        SymbolQueryService symbolQuery,
        ILogger<RenameSymbolService> logger)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        // referenceFinder and symbolQuery are retained in the constructor contract (callers and DI
        // supply them) but symbol lookup is now delegated to SymbolFinderService.
        ArgumentNullException.ThrowIfNull(referenceFinder);
        ArgumentNullException.ThrowIfNull(symbolQuery);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize the new symbol finder service with strategy pattern
        var symbolFinderLogger = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger<SymbolFinderService>();
        _symbolFinder = new SymbolFinderService(_workspace, symbolFinderLogger);
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
            LogStartingRename(_logger, request.OldName, request.NewName);

            // Phase 1: Validate the rename request
            var validation = await ValidateRenameAsync(request, ct);
            if (!validation.IsValid)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    LogRenameValidationFailed(_logger, string.Join(", ", validation.Errors));
                }
                return RenameResult.Failed(validation.Errors);
            }

            // Phase 2: Find the symbol to rename
            var symbol = validation.Symbol;
            if (symbol == null)
            {
                return RenameResult.Failed(SymbolNotFoundErrors);
            }

            // Phase 3: Check for conflicts
            var conflicts = DetectConflicts(symbol, request.NewName, ct);
            if (conflicts.Any(c => c.Severity == ConflictSeverity.Error))
            {
                LogBlockingConflicts(_logger);
                return RenameResult.Blocked(conflicts);
            }

            // Phase 4: Handle non-blocking conflicts based on strategy
            if (conflicts.Count > 0 && request.HandleConflicts == ConflictHandling.Abort)
            {
                return RenameResult.Blocked(conflicts);
            }

            // Phase 5: Perform the rename using Roslyn's Renamer
            var renameResult = await PerformRenameAsync(symbol, request, ct);

            LogRenameCompleted(_logger, renameResult.RenamedCount);

            return renameResult;
        }
        catch (OperationCanceledException)
        {
            LogRenameCancelled(_logger);
            throw;
        }
        catch (Exception ex)
        {
            LogRenameFailed(_logger, ex);
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

    private static bool IsSymbolAccessibleForRename(ISymbol symbol)
    {
        if (symbol == null)
        {
            return false;
        }

        return CanRenameSymbol(symbol);
    }

    private static bool IsValidIdentifier(string name)
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

    private static bool IsKeyword(string name) => CSharpKeywords.Contains(name);

    private static bool CanRenameSymbol(ISymbol symbol)
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

    private static bool IsPublicApi(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility == Accessibility.Public ||
               symbol.DeclaredAccessibility == Accessibility.Protected ||
               symbol.DeclaredAccessibility == Accessibility.ProtectedOrInternal;
    }
}

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn.SymbolSearch;

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

    /// <summary>
    /// Find symbol using the new unified symbol finder service.
    /// This replaces the old ad-hoc search logic with a clean strategy pattern.
    /// </summary>
    private async Task<ISymbol?> FindSymbolAsync(RenameRequest request, CancellationToken ct)
    {
        // Convert RenameRequest to SymbolSearchRequest
        var searchRequest = new SymbolSearchRequest
        {
            Name = request.OldName,
            SymbolKind = request.SymbolKind,
            ContainingType = request.ContainingType,
            FilePath = request.FilePath,
            Line = request.Line,
            Column = request.Column
        };

        // Use the new unified finder service
        var result = await _symbolFinder.FindSymbolAsync(searchRequest, ct);

        var foundSymbol = result.Symbol;
        if (foundSymbol != null)
        {
            // Additional validation specific to rename operations
            if (!ValidateSymbolMatch(foundSymbol, request))
            {
                LogSymbolFailedValidation(_logger, foundSymbol.Name);
                return null;
            }

            LogFoundSymbol(_logger, foundSymbol.Name, foundSymbol.Kind, result.UsedStrategy);

            if (result.IsAmbiguous)
            {
                LogAmbiguousCandidates(_logger, result.Candidates.Count, request.OldName);
            }

            return foundSymbol;
        }

        LogSymbolNotFound(_logger, request.OldName, request.SymbolKind);
        return null;
    }

    /// <summary>
    /// Count occurrences of a substring in a string
    /// </summary>
    private static int CountOccurrences(string text, string substring)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(substring))
            return 0;

        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }

    private async Task<ISymbol?> ResolveSymbolInCurrentCompilationAsync(
        ISymbol symbol,
        Solution solution,
        CancellationToken ct)
    {
        if (symbol == null || solution == null)
        {
            return null;
        }

        try
        {
            // Get the symbol's primary source location
            var sourceLocation = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (sourceLocation == null)
            {
                LogNoSourceLocation(_logger);
                return null;
            }

            // Find the document containing this symbol
            var filePath = sourceLocation.GetLineSpan().Path;
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null)
            {
                LogDocumentNotFound(_logger, filePath);
                return null;
            }

            // Get the current semantic model and syntax tree
            var semanticModel = await document.GetSemanticModelAsync(ct);
            var syntaxRoot = await document.GetSyntaxRootAsync(ct);

            if (semanticModel == null || syntaxRoot == null)
            {
                LogNoSemanticModelOrRoot(_logger);
                return null;
            }

            // Find the syntax node at the symbol's location
            // We need to find the declaration node, not just any node at the location
            var node = syntaxRoot.FindNode(sourceLocation.SourceSpan);
            if (node == null)
            {
                LogSyntaxNodeNotFound(_logger);
                return null;
            }

            // For declarations, we need to find the parent declaration node
            // (e.g., ClassDeclarationSyntax, MethodDeclarationSyntax, etc.)
            var declarationNode = node.AncestorsAndSelf().FirstOrDefault(n =>
                semanticModel.GetDeclaredSymbol(n, ct) != null);

            if (declarationNode != null)
            {
                var declaredSymbol = semanticModel.GetDeclaredSymbol(declarationNode, ct);
                if (declaredSymbol != null)
                {
                    LogReResolvedViaDeclaration(_logger, declaredSymbol.Name, declaredSymbol.Kind);
                    return declaredSymbol;
                }
            }

            // If not a declaration, try to get the symbol info (for references like variables, parameters)
            var resolved = semanticModel.GetSymbolInfo(node, ct).Symbol;
            if (resolved != null)
            {
                LogReResolvedViaSymbolInfo(_logger, resolved.Name, resolved.Kind);
                return resolved;
            }

            LogCouldNotReResolve(_logger);
            return null;
        }
        catch (Exception ex)
        {
            LogReResolveError(_logger, ex);
            return null;
        }
    }

    private List<RenameConflict> DetectConflicts(
        ISymbol symbol,
        string newName,
        CancellationToken ct)
    {
        var conflicts = new List<RenameConflict>();

        try
        {
            if (symbol == null || string.IsNullOrWhiteSpace(newName))
            {
                LogInvalidConflictParameters(_logger, symbol != null, newName);
                return conflicts;
            }

            LogDetectingConflicts(_logger, symbol.Name, symbol.Kind, newName);

            // Check for name collision in the same scope
            var containingSymbol = symbol.ContainingSymbol;
            LogContainingSymbol(_logger, containingSymbol?.Name ?? "null", containingSymbol?.GetType().Name ?? "null");

            // Check for name collision in the same type or namespace
            if (containingSymbol is INamedTypeSymbol containingType)
            {
                LogCheckingTypeMemberCollisions(_logger, containingType.Name);
                CheckTypeMemberCollisions(containingType, newName, symbol, conflicts);
            }
            else if (containingSymbol is INamespaceSymbol containingNamespace)
            {
                LogCheckingNamespaceCollisions(_logger, containingNamespace);
                CheckNamespaceCollisions(containingNamespace, newName, symbol, conflicts, ct);
            }
            else
            {
                LogContainingSymbolNeitherTypeNorNamespace(_logger);
            }

            // Check for hiding inherited members
            LogCheckingInheritanceHiding(_logger);
            CheckInheritanceHiding(symbol, newName, conflicts);

            // Check interface implementation conflicts
            LogCheckingInterfaceConflicts(_logger);
            CheckInterfaceImplementationConflicts(symbol, newName, conflicts);

            LogConflictDetectionComplete(_logger, conflicts.Count, symbol.Name);
        }
        catch (Exception ex)
        {
            LogConflictDetectionError(_logger, ex, symbol?.Name);
        }

        return conflicts;
    }

    private void CheckTypeMemberCollisions(
        INamedTypeSymbol containingType,
        string newName,
        ISymbol originalSymbol,
        List<RenameConflict> conflicts)
    {
        try
        {
            LogCheckTypeMemberCollisionsCalled(_logger, containingType?.Name, newName, originalSymbol?.Name);

            if (containingType == null || originalSymbol == null || conflicts == null)
            {
                LogTypeMemberCollisionsEarlyReturn(_logger, containingType != null, originalSymbol != null, conflicts != null);
                return;
            }

            // Get all members with the target name
            var existingMembers = containingType.GetMembers(newName);
            LogFoundMembersWithName(_logger, existingMembers.Length, newName, containingType.Name);

            if (existingMembers.IsEmpty)
            {
                LogNoExistingMembers(_logger, newName, containingType.Name);
                return;
            }

            foreach (var existing in existingMembers)
            {
                if (existing == null)
                {
                    LogSkippingNullMember(_logger);
                    continue;
                }

                var sourceLocations = existing.Locations.Where(l => l?.IsInSource == true).ToList();
                LogCheckingMember(_logger, existing.Name, existing.Kind, sourceLocations.Count > 0);

                // Skip if it's the same symbol
                if (SymbolEqualityComparer.Default.Equals(existing, originalSymbol))
                {
                    LogSkippingSameSymbol(_logger);
                    continue;
                }

                // Check if the existing symbol is in source code
                if (sourceLocations.Count == 0)
                {
                    LogExistingSymbolNotInSource(_logger, existing.Name);
                    continue;
                }

                LogNameCollision(_logger, newName, containingType.Name);

                conflicts.Add(new RenameConflict
                {
                    Type = ConflictType.NameCollision,
                    Severity = ConflictSeverity.Error,
                    Description = $"Name '{newName}' already exists in {containingType.Name}",
                    Location = sourceLocations[0]
                });
            }

            LogCheckTypeMemberCollisionsFinished(_logger, conflicts.Count);
        }
        catch (Exception ex)
        {
            LogTypeMemberCollisionsError(_logger, ex, containingType?.Name);
        }
    }

    private void CheckNamespaceCollisions(
        INamespaceSymbol containingNamespace,
        string newName,
        ISymbol originalSymbol,
        List<RenameConflict> conflicts,
        CancellationToken ct)
    {
        try
        {
            if (containingNamespace == null || originalSymbol == null || conflicts == null)
            {
                return;
            }

            var compilation = _workspace.GetCompilation();
            if (compilation == null)
            {
                LogNoCompilationForNamespaceCheck(_logger);
                return;
            }

            var namespaceName = containingNamespace.ToDisplayString();

            // Find all symbols with the target name in the same namespace
            var existingSymbols = compilation.GetSymbolsWithName(
                n => string.Equals(n, newName, StringComparison.Ordinal),
                SymbolFilter.TypeAndMember, ct)
                .Where(s => s != null &&
                           s.ContainingNamespace != null &&
                           SymbolEqualityComparer.Default.Equals(s.ContainingNamespace, containingNamespace) &&
                           !SymbolEqualityComparer.Default.Equals(s, originalSymbol) &&
                           s.Locations.Any(l => l?.IsInSource == true))
                .ToList();

            if (existingSymbols.Count == 0)
            {
                LogNoExistingNamespaceSymbols(_logger, newName, namespaceName);
                return;
            }

            foreach (var existing in existingSymbols)
            {
                var sourceLocation = existing.Locations.FirstOrDefault(l => l?.IsInSource == true);

                LogNamespaceCollision(_logger, newName, namespaceName);

                conflicts.Add(new RenameConflict
                {
                    Type = ConflictType.NameCollision,
                    Severity = ConflictSeverity.Error,
                    Description = $"Name '{newName}' already exists in namespace {namespaceName}",
                    Location = sourceLocation
                });
            }
        }
        catch (Exception ex)
        {
            LogNamespaceCollisionsError(_logger, ex, containingNamespace?.ToDisplayString());
        }
    }

    private void CheckInheritanceHiding(
        ISymbol symbol,
        string newName,
        List<RenameConflict> conflicts)
    {
        try
        {
            if (symbol == null || conflicts == null)
            {
                return;
            }

            if (symbol.ContainingType is not INamedTypeSymbol containingType)
            {
                LogNotInType(_logger, symbol.Name);
                return;
            }

            LogCheckingInheritanceHidingFor(_logger, symbol.Name, newName);

            var baseType = containingType.BaseType;
            while (baseType != null)
            {
                try
                {
                    AddInheritanceHidingConflicts(baseType, symbol, newName, conflicts);
                }
                catch (Exception ex)
                {
                    LogBaseTypeCheckError(_logger, ex, baseType.Name);
                }

                baseType = baseType.BaseType;
            }
        }
        catch (Exception ex)
        {
            LogInheritanceHidingError(_logger, ex, symbol?.Name ?? "null");
        }
    }

    private void AddInheritanceHidingConflicts(
        INamedTypeSymbol baseType,
        ISymbol symbol,
        string newName,
        List<RenameConflict> conflicts)
    {
        // Only virtual/abstract/override members that are not sealed can be hidden;
        // skip the symbol itself (shouldn't happen but defensive).
        var hideableMembers = baseType.GetMembers(newName)
            .Where(baseMember => baseMember != null &&
                                 !SymbolEqualityComparer.Default.Equals(baseMember, symbol) &&
                                 !baseMember.IsSealed &&
                                 (baseMember.IsVirtual || baseMember.IsAbstract || baseMember.IsOverride));

        foreach (var baseMember in hideableMembers)
        {
            var sourceLocation = baseMember.Locations.FirstOrDefault(l => l?.IsInSource == true);

            LogInheritanceHidingFound(_logger, newName, baseType.Name);

            conflicts.Add(new RenameConflict
            {
                Type = ConflictType.HidesInheritedMember,
                Severity = ConflictSeverity.Warning,
                Description = $"New name '{newName}' hides inherited member from {baseType.Name}",
                Location = sourceLocation
            });
        }
    }

    private void CheckInterfaceImplementationConflicts(
        ISymbol symbol,
        string newName,
        List<RenameConflict> conflicts)
    {
        try
        {
            if (symbol is not IMethodSymbol methodSymbol || methodSymbol.ContainingType == null)
            {
                return;
            }

            foreach (var iface in methodSymbol.ContainingType.AllInterfaces)
            {
                if (iface == null) continue;

                var matchingMethods = iface.GetMembers(newName)
                    .OfType<IMethodSymbol>()
                    .Where(interfaceMethod => interfaceMethod != null && SignaturesMatch(methodSymbol, interfaceMethod));

                foreach (var interfaceMethod in matchingMethods)
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
        catch (Exception ex)
        {
            LogInterfaceConflictsError(_logger, ex, symbol.Name);
        }
    }

    // NOTE: FindLocalOrParameterSymbolsAsync removed - now handled by ScopeLocalStrategy
    // in the SymbolFinderService. This eliminates duplicate code and centralizes
    // symbol search logic.

    private async Task<RenameResult> PerformRenameAsync(
        ISymbol symbol,
        RenameRequest request,
        CancellationToken ct)
    {
        LogPerformRename(_logger, symbol.Name, symbol.Kind, request.NewName);

        var solution = _workspace.Solution;

        // CRITICAL: The symbol must be from the current solution's compilation,
        // not from a cached compilation. Roslyn symbols are tied to a specific compilation.
        // If the symbol is from an old compilation, Renamer won't find all references.
        // Re-resolve the symbol to ensure it's from the current compilation
        var resolvedSymbol = await ResolveSymbolInCurrentCompilationAsync(symbol, solution, ct);
        if (resolvedSymbol != null)
        {
            LogUsingReResolvedSymbol(_logger, resolvedSymbol.Name);
            symbol = resolvedSymbol;
        }
        else
        {
            LogUsingOriginalSymbol(_logger, symbol.Name);
        }

        await LogRenameDiagnosticsAsync(symbol, solution, ct);

        var optionSet = solution.Options;

        // Configure rename options
        // Note: Using OptionSet approach as older API, newer code should use SymbolRenameOptions
        #pragma warning disable CS0618
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
        #pragma warning restore CS0618

        // Track modified files
        var modifiedFiles = new HashSet<string>();
        var renamedCount = 0;

        // Use Roslyn's Renamer API for the actual rename.
        // Failures propagate to RenameSymbolAsync, which logs them and converts them to a failed result.
        #pragma warning disable CS0618
        var newSolution = await Renamer.RenameSymbolAsync(
            solution,
            symbol,
            request.NewName,
            optionSet,
            ct);
        #pragma warning restore CS0618

        // Calculate changes
        var changes = newSolution.GetChanges(solution);
        var projectChanges = changes.GetProjectChanges().ToList();
        LogRenameProjectChanges(_logger, projectChanges.Count);

        var fileChanges = new List<FileChange>();

        foreach (var projectChange in projectChanges)
        {
            var changedDocs = projectChange.GetChangedDocuments().ToList();
            LogProjectChangedDocuments(_logger, projectChange.NewProject.Name, changedDocs.Count);

            foreach (var docId in changedDocs)
            {
                var oldDoc = solution.GetDocument(docId);
                var newDoc = newSolution.GetDocument(docId);

                if (oldDoc == null || newDoc == null)
                {
                    continue;
                }

                var oldText = await oldDoc.GetTextAsync(ct);
                var newText = await newDoc.GetTextAsync(ct);
                var textChanges = newText.GetTextChanges(oldText).ToList();

                LogDocumentTextChanges(_logger, oldDoc.Name, textChanges.Count);

                if (textChanges.Count == 0)
                {
                    continue;
                }

                modifiedFiles.Add(oldDoc.FilePath ?? docId.ToString());
                renamedCount += CountSymbolRenames(oldText, textChanges, request);

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

        // Apply changes if not preview mode
        if (!request.PreviewOnly && !_workspace.Solution.Workspace.TryApplyChanges(newSolution))
        {
            return RenameResult.Failed(ApplyChangesFailedErrors);
        }

        return new RenameResult
        {
            Success = true,
            RenamedCount = renamedCount,
            FilesModified = modifiedFiles.ToList(),
            Preview = request.PreviewOnly ? fileChanges : null
        };
    }

    /// <summary>
    /// Count actual symbol renames, not just text change spans.
    /// Roslyn may consolidate multiple renames into fewer text changes.
    /// </summary>
    private int CountSymbolRenames(
        SourceText oldText,
        List<Microsoft.CodeAnalysis.Text.TextChange> textChanges,
        RenameRequest request)
    {
        int symbolRenames = 0;
        foreach (var change in textChanges)
        {
            var oldTextSpan = oldText.GetSubText(change.Span).ToString();
            LogTextChange(_logger, change.Span, oldTextSpan, change.NewText);

            // Count occurrences of old name being replaced
            // Handle case where Roslyn does bulk replacements
            if (change.NewText != null)
            {
                int oldNameCount = CountOccurrences(oldTextSpan, request.OldName);
                int newNameCount = CountOccurrences(change.NewText, request.NewName);
                symbolRenames += Math.Max(oldNameCount, newNameCount);
            }
        }
        return symbolRenames;
    }

    /// <summary>
    /// Diagnostic logging of the symbol, the solution and the references Roslyn can find.
    /// Skipped entirely unless Information logging is enabled, as finding references is expensive.
    /// </summary>
    private async Task LogRenameDiagnosticsAsync(ISymbol symbol, Solution solution, CancellationToken ct)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return;
        }

        LogRenamingSymbolDetails(_logger, symbol.Name, symbol.Kind, symbol.ContainingType?.Name ?? "none", symbol.Locations.Length);

        var projectCount = 0;
        var documentCount = 0;
        foreach (var project in solution.Projects)
        {
            projectCount++;
            documentCount += project.DocumentIds.Count;
        }
        LogSolutionState(_logger, projectCount, documentCount);

        try
        {
            var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(
                symbol, solution, ct);
            var refList = references.ToList();
            LogReferenceGroupCount(_logger, refList.Count);
            foreach (var refGroup in refList)
            {
                var locationCount = refGroup.Locations.Count();
                LogReferenceLocationCount(_logger, refGroup.Definition.Name, locationCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSymbolFinderError(_logger, ex);
        }
    }

    // NOTE: FilterCandidatesAsync removed - filtering is now handled by SymbolSelector
    // in the SymbolFinderService as part of the unified selection process.

    private bool ValidateSymbolMatch(ISymbol symbol, RenameRequest request)
    {
        if (symbol == null || request == null)
        {
            return false;
        }

        // Check name match (case-insensitive for robustness)
        if (string.IsNullOrWhiteSpace(symbol.Name) ||
            !string.Equals(symbol.Name, request.OldName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check symbol kind
        if (request.SymbolKind != SymbolKind.Any && !MatchesSymbolKind(symbol, request.SymbolKind))
        {
            return false;
        }

        // Check containing type
        if (!string.IsNullOrEmpty(request.ContainingType))
        {
            var containingTypeName = symbol.ContainingType?.Name;
            if (string.IsNullOrEmpty(containingTypeName) ||
                !string.Equals(containingTypeName, request.ContainingType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Ensure symbol is in source code (not metadata)
        if (!symbol.Locations.Any(l => l != null && l.IsInSource))
        {
            LogSymbolNotInSource(_logger, symbol.Name);
            return false;
        }

        // Additional validation for specific symbol types
        if (!IsSymbolAccessibleForRename(symbol))
        {
            LogSymbolNotAccessible(_logger, symbol.Name);
            return false;
        }

        // Additional validation for constructor handling
        if (request.SymbolKind == SymbolKind.Class && symbol is IMethodSymbol methodSymbol)
        {
            // If we're looking for a class but found a constructor, check if it's the constructor for that class
            if (methodSymbol.MethodKind == MethodKind.Constructor && methodSymbol.ContainingType != null)
            {
                return string.Equals(methodSymbol.ContainingType.Name, request.OldName, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        // Special handling for type parameters - check if the name matches the pattern
        if (request.SymbolKind == SymbolKind.TypeParameter && symbol is not ITypeParameterSymbol)
        {
            return false;
        }

        return true;
    }

    private static bool MatchesSymbolKind(ISymbol symbol, SymbolKind requestedKind)
    {
        if (symbol == null)
            return false;

        return requestedKind switch
        {
            SymbolKind.Class => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class } ||
                                // Handle constructors when looking for their class
                                (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } methodSymbol &&
                                 methodSymbol.ContainingType != null &&
                                 methodSymbol.ContainingType.TypeKind == TypeKind.Class),

            SymbolKind.Interface => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },

            SymbolKind.Method => symbol is IMethodSymbol methodSymbol &&
                                methodSymbol.MethodKind != MethodKind.Constructor &&
                                methodSymbol.MethodKind != MethodKind.PropertyGet &&
                                methodSymbol.MethodKind != MethodKind.PropertySet,

            SymbolKind.Property => symbol is IPropertySymbol ||
                                  // Handle property accessors when looking for the property
                                  (symbol is IMethodSymbol methodSymbol &&
                                   (methodSymbol.MethodKind == MethodKind.PropertyGet ||
                                    methodSymbol.MethodKind == MethodKind.PropertySet) &&
                                   methodSymbol.AssociatedSymbol is IPropertySymbol),

            SymbolKind.Field => symbol is IFieldSymbol,

            SymbolKind.Parameter => symbol is IParameterSymbol,

            SymbolKind.Namespace => symbol is INamespaceSymbol,

            SymbolKind.Local => symbol is ILocalSymbol,

            SymbolKind.TypeParameter => symbol is ITypeParameterSymbol,

            _ => true // Any
        };
    }

    // NOTE: SelectBestCandidate, NormalizeSymbol, and CalculateCandidateScore removed
    // These methods are now handled by SymbolSelector in the SymbolFinderService.
    // The logic has been preserved and improved with better separation of concerns.

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

    private static bool SignaturesMatch(IMethodSymbol method1, IMethodSymbol method2)
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
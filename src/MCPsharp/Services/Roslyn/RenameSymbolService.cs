using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using MCPsharp.Models.Roslyn;
using MCPsharp.Services.Roslyn.SymbolSearch;
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
    private readonly SymbolFinderService _symbolFinder;
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

        if (result.Symbol != null)
        {
            // Additional validation specific to rename operations
            if (!ValidateSymbolMatch(result.Symbol, request))
            {
                _logger.LogDebug("Symbol found but failed validation: '{SymbolName}'", result.Symbol.Name);
                return null;
            }

            _logger.LogDebug("Found symbol '{SymbolName}' of kind {SymbolKind} using {Strategy}",
                result.Symbol.Name, result.Symbol.Kind, result.UsedStrategy);

            if (result.IsAmbiguous)
            {
                _logger.LogWarning("Found {Count} ambiguous candidates for '{OldName}', selected best match",
                    result.Candidates.Count, request.OldName);
            }

            return result.Symbol;
        }

        _logger.LogWarning("Symbol '{OldName}' not found with kind {SymbolKind}",
            request.OldName, request.SymbolKind);
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
                _logger.LogDebug("Symbol has no source location, cannot re-resolve");
                return null;
            }

            // Find the document containing this symbol
            var filePath = sourceLocation.GetLineSpan().Path;
            var document = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);

            if (document == null)
            {
                _logger.LogDebug("Document not found for path: {FilePath}", filePath);
                return null;
            }

            // Get the current semantic model and syntax tree
            var semanticModel = await document.GetSemanticModelAsync(ct);
            var syntaxRoot = await document.GetSyntaxRootAsync(ct);

            if (semanticModel == null || syntaxRoot == null)
            {
                _logger.LogDebug("Could not get semantic model or syntax root");
                return null;
            }

            // Find the syntax node at the symbol's location
            // We need to find the declaration node, not just any node at the location
            var node = syntaxRoot.FindNode(sourceLocation.SourceSpan);
            if (node == null)
            {
                _logger.LogDebug("Could not find syntax node at location");
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
                    _logger.LogInformation("Re-resolved symbol '{SymbolName}' (kind: {Kind}) in current compilation via declaration node",
                        declaredSymbol.Name, declaredSymbol.Kind);
                    return declaredSymbol;
                }
            }

            // If not a declaration, try to get the symbol info (for references like variables, parameters)
            var symbolInfo = semanticModel.GetSymbolInfo(node, ct);
            if (symbolInfo.Symbol != null)
            {
                _logger.LogInformation("Re-resolved symbol '{SymbolName}' (kind: {Kind}) in current compilation via SymbolInfo",
                    symbolInfo.Symbol.Name, symbolInfo.Symbol.Kind);
                return symbolInfo.Symbol;
            }

            _logger.LogDebug("Could not re-resolve symbol in current compilation");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-resolving symbol in current compilation");
            return null;
        }
    }

    private async Task<List<RenameConflict>> DetectConflictsAsync(
        ISymbol symbol,
        string newName,
        CancellationToken ct)
    {
        var conflicts = new List<RenameConflict>();

        try
        {
            if (symbol == null || string.IsNullOrWhiteSpace(newName))
            {
                _logger.LogWarning("Invalid parameters for conflict detection: symbol={HasSymbol}, newName={NewName}",
                    symbol != null, newName);
                return conflicts;
            }

            _logger.LogInformation("Detecting conflicts for symbol {SymbolName} (Kind: {Kind}) -> {NewName}",
                symbol.Name, symbol.Kind, newName);

            // Check for name collision in the same scope
            var containingSymbol = symbol.ContainingSymbol;
            _logger.LogDebug("ContainingSymbol: {ContainingSymbol} (Type: {Type})",
                containingSymbol?.Name ?? "null",
                containingSymbol?.GetType().Name ?? "null");

            // Check for name collision in the same type or namespace
            if (containingSymbol is INamedTypeSymbol containingType && containingType != null)
            {
                _logger.LogDebug("Checking type member collisions in {TypeName}", containingType.Name);
                await CheckTypeMemberCollisionsAsync(containingType, newName, symbol, conflicts, ct);
            }
            else if (containingSymbol is INamespaceSymbol containingNamespace && containingNamespace != null)
            {
                _logger.LogDebug("Checking namespace collisions in {NamespaceName}", containingNamespace.ToDisplayString());
                await CheckNamespaceCollisionsAsync(containingNamespace, newName, symbol, conflicts, ct);
            }
            else
            {
                _logger.LogDebug("ContainingSymbol is neither a type nor a namespace");
            }

            // Check for hiding inherited members
            _logger.LogDebug("Checking inheritance hiding");
            await CheckInheritanceHidingAsync(symbol, newName, conflicts, ct);

            // Check interface implementation conflicts
            _logger.LogDebug("Checking interface implementation conflicts");
            await CheckInterfaceImplementationConflictsAsync(symbol, newName, conflicts, ct);

            _logger.LogInformation("Conflict detection complete: Found {ConflictCount} conflicts for symbol {SymbolName}",
                conflicts.Count, symbol.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting conflicts for symbol {SymbolName}", symbol?.Name);
        }

        return conflicts;
    }

    private async Task CheckTypeMemberCollisionsAsync(
        INamedTypeSymbol containingType,
        string newName,
        ISymbol originalSymbol,
        List<RenameConflict> conflicts,
        CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("CheckTypeMemberCollisionsAsync called: containingType={ContainingType}, newName={NewName}, originalSymbol={OriginalSymbol}",
                containingType?.Name, newName, originalSymbol?.Name);

            if (containingType == null || originalSymbol == null || conflicts == null)
            {
                _logger.LogDebug("Early return due to null check: containingType={HasContainingType}, originalSymbol={HasOriginalSymbol}, conflicts={HasConflicts}",
                    containingType != null, originalSymbol != null, conflicts != null);
                return;
            }

            // Get all members with the target name
            var existingMembers = containingType.GetMembers(newName);
            _logger.LogDebug("Found {MemberCount} members with name '{NewName}' in type {TypeName}",
                existingMembers.Length, newName, containingType.Name);

            if (!existingMembers.Any())
            {
                _logger.LogDebug("No existing members found with name '{NewName}' in type {TypeName}", newName, containingType.Name);
                return;
            }

            foreach (var existing in existingMembers)
            {
                if (existing == null)
                {
                    _logger.LogDebug("Skipping null member");
                    continue;
                }

                _logger.LogDebug("Checking member: Name={Name}, Kind={Kind}, IsInSource={IsInSource}",
                    existing.Name, existing.Kind, existing.Locations.Any(l => l.IsInSource));

                // Skip if it's the same symbol
                if (SymbolEqualityComparer.Default.Equals(existing, originalSymbol))
                {
                    _logger.LogDebug("Skipping because it's the same symbol as original");
                    continue;
                }

                // Check if the existing symbol is in source code
                var sourceLocations = existing.Locations.Where(l => l?.IsInSource == true).ToList();
                if (!sourceLocations.Any())
                {
                    _logger.LogDebug("Existing symbol '{ExistingName}' is not in source code", existing.Name);
                    continue;
                }

                _logger.LogInformation("Found name collision: '{NewName}' already exists in {TypeName}", newName, containingType.Name);

                conflicts.Add(new RenameConflict
                {
                    Type = ConflictType.NameCollision,
                    Severity = ConflictSeverity.Error,
                    Description = $"Name '{newName}' already exists in {containingType.Name}",
                    Location = sourceLocations.FirstOrDefault()
                });
            }

            _logger.LogDebug("CheckTypeMemberCollisionsAsync finished with {ConflictCount} conflicts", conflicts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking type member collisions in {TypeName}", containingType?.Name);
        }
    }

    private async Task CheckNamespaceCollisionsAsync(
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
                _logger.LogWarning("No compilation available for namespace collision checking");
                return;
            }

            // Find all symbols with the target name in the same namespace
            var existingSymbols = compilation.GetSymbolsWithName(
                n => string.Equals(n, newName, StringComparison.Ordinal),
                SymbolFilter.TypeAndMember)
                .Where(s => s != null &&
                           s.ContainingNamespace != null &&
                           SymbolEqualityComparer.Default.Equals(s.ContainingNamespace, containingNamespace) &&
                           !SymbolEqualityComparer.Default.Equals(s, originalSymbol) &&
                           s.Locations != null &&
                           s.Locations.Any(l => l?.IsInSource == true))
                .ToList();

            if (!existingSymbols.Any())
            {
                _logger.LogDebug("No existing symbols found with name '{NewName}' in namespace {NamespaceName}", newName, containingNamespace.ToDisplayString());
                return;
            }

            foreach (var existing in existingSymbols)
            {
                if (existing == null)
                    continue;

                var sourceLocation = existing.Locations.FirstOrDefault(l => l?.IsInSource == true);

                _logger.LogDebug("Found namespace collision: '{NewName}' already exists in namespace {NamespaceName}",
                    newName, containingNamespace.ToDisplayString());

                conflicts.Add(new RenameConflict
                {
                    Type = ConflictType.NameCollision,
                    Severity = ConflictSeverity.Error,
                    Description = $"Name '{newName}' already exists in namespace {containingNamespace.ToDisplayString()}",
                    Location = sourceLocation
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking namespace collisions in {NamespaceName}", containingNamespace?.ToDisplayString());
        }
    }

    private async Task CheckInheritanceHidingAsync(
        ISymbol symbol,
        string newName,
        List<RenameConflict> conflicts,
        CancellationToken ct)
    {
        try
        {
            if (symbol == null || conflicts == null)
            {
                return;
            }

            if (symbol.ContainingType is not INamedTypeSymbol containingType || containingType == null)
            {
                _logger.LogDebug("Symbol {SymbolName} is not in a type, skipping inheritance hiding check", symbol.Name);
                return;
            }

            _logger.LogDebug("Checking inheritance hiding for symbol {SymbolName} -> {NewName}", symbol.Name, newName);

            var baseType = containingType.BaseType;
            while (baseType != null)
            {
                if (baseType == null)
                    break;

                try
                {
                    var baseMembers = baseType.GetMembers(newName);
                    if (baseMembers != null)
                    {
                        foreach (var baseMember in baseMembers)
                        {
                            if (baseMember == null)
                                continue;

                            // Skip if it's the same symbol (shouldn't happen but defensive)
                            if (SymbolEqualityComparer.Default.Equals(baseMember, symbol))
                                continue;

                            // Skip if base member is sealed and would be hidden
                            if (baseMember.IsSealed)
                                continue;

                            // Check if base member is virtual/abstract/override that could be hidden
                            if (baseMember.IsVirtual || baseMember.IsAbstract || baseMember.IsOverride)
                            {
                                var sourceLocation = baseMember.Locations.FirstOrDefault(l => l?.IsInSource == true);

                                _logger.LogDebug("Found inheritance hiding: '{NewName}' hides inherited member from {BaseTypeName}",
                                    newName, baseType.Name);

                                conflicts.Add(new RenameConflict
                                {
                                    Type = ConflictType.HidesInheritedMember,
                                    Severity = ConflictSeverity.Warning,
                                    Description = $"New name '{newName}' hides inherited member from {baseType.Name}",
                                    Location = sourceLocation
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error checking base type {BaseTypeName} for inheritance hiding", baseType.Name);
                }

                baseType = baseType.BaseType;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking inheritance hiding for symbol {SymbolName}", symbol?.Name ?? "null");
        }
    }

    private async Task CheckInterfaceImplementationConflictsAsync(
        ISymbol symbol,
        string newName,
        List<RenameConflict> conflicts,
        CancellationToken ct)
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

                var interfaceMethods = iface.GetMembers(newName).OfType<IMethodSymbol>();
                foreach (var interfaceMethod in interfaceMethods)
                {
                    if (interfaceMethod != null && SignaturesMatch(methodSymbol, interfaceMethod))
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking interface implementation conflicts for symbol {SymbolName}", symbol.Name);
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
        _logger.LogDebug("PerformRenameAsync: Renaming symbol '{SymbolName}' of kind {Kind} to '{NewName}'",
            symbol.Name, symbol.Kind, request.NewName);

        var solution = _workspace.Solution;

        // CRITICAL: The symbol must be from the current solution's compilation,
        // not from a cached compilation. Roslyn symbols are tied to a specific compilation.
        // If the symbol is from an old compilation, Renamer won't find all references.
        // CRITICAL: Ensure symbol is from current solution's compilation
        // Re-resolve the symbol to ensure it's from the current compilation
        var resolvedSymbol = await ResolveSymbolInCurrentCompilationAsync(symbol, solution, ct);
        if (resolvedSymbol != null)
        {
            _logger.LogInformation("Using re-resolved symbol '{SymbolName}' from current compilation", resolvedSymbol.Name);
            symbol = resolvedSymbol;
        }
        else
        {
            _logger.LogWarning("Could not re-resolve symbol '{SymbolName}', using original symbol", symbol.Name);
        }

        // Log symbol details for debugging
        _logger.LogInformation("Renaming symbol: Name='{Name}', Kind={Kind}, ContainingType={ContainingType}, Locations={LocationCount}",
            symbol.Name, symbol.Kind, symbol.ContainingType?.Name ?? "none", symbol.Locations.Length);

        // Log solution state
        _logger.LogInformation("Solution has {ProjectCount} projects, {DocumentCount} total documents",
            solution.Projects.Count(), solution.Projects.Sum(p => p.Documents.Count()));

        // DEBUG: Manually find references using SymbolFinder to see what Roslyn can find
        try
        {
            var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(
                symbol, solution, ct);
            var refList = references.ToList();
            _logger.LogInformation("SymbolFinder found {ReferenceGroupCount} reference groups", refList.Count);
            foreach (var refGroup in refList)
            {
                var locations = refGroup.Locations.ToList();
                _logger.LogInformation("  Symbol '{SymbolName}' has {LocationCount} reference locations",
                    refGroup.Definition.Name, locations.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error using SymbolFinder");
        }

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

        try
        {
            // Use Roslyn's Renamer API for the actual rename
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
            _logger.LogInformation("Rename operation completed. Project changes: {ProjectChangeCount}",
                projectChanges.Count);

            var fileChanges = new List<FileChange>();

            foreach (var projectChange in projectChanges)
            {
                var changedDocs = projectChange.GetChangedDocuments().ToList();
                _logger.LogInformation("Project '{ProjectName}' has {ChangedDocCount} changed documents",
                    projectChange.NewProject.Name, changedDocs.Count);

                foreach (var docId in changedDocs)
                {
                    var oldDoc = solution.GetDocument(docId);
                    var newDoc = newSolution.GetDocument(docId);

                    if (oldDoc != null && newDoc != null)
                    {
                        var oldText = await oldDoc.GetTextAsync(ct);
                        var newText = await newDoc.GetTextAsync(ct);
                        var textChanges = newText.GetTextChanges(oldText).ToList();

                        _logger.LogInformation("Document '{DocName}' has {ChangeCount} text changes",
                            oldDoc.Name, textChanges.Count);

                        // Count actual symbol renames, not just text change spans
                        // Roslyn may consolidate multiple renames into fewer text changes
                        int symbolRenames = 0;
                        foreach (var change in textChanges)
                        {
                            var oldTextSpan = oldText.GetSubText(change.Span).ToString();
                            _logger.LogDebug("Text change at span {Span}: '{OldText}' -> '{NewText}'",
                                change.Span, oldTextSpan, change.NewText);

                            // Count occurrences of old name being replaced
                            // Handle case where Roslyn does bulk replacements
                            if (change.NewText != null)
                            {
                                int oldNameCount = CountOccurrences(oldTextSpan, request.OldName);
                                int newNameCount = CountOccurrences(change.NewText, request.NewName);
                                symbolRenames += Math.Max(oldNameCount, newNameCount);
                            }
                        }

                        if (textChanges.Any())
                        {
                            modifiedFiles.Add(oldDoc.FilePath ?? docId.ToString());
                            renamedCount += symbolRenames;

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
        if (request.SymbolKind != SymbolKind.Any)
        {
            if (!MatchesSymbolKind(symbol, request.SymbolKind))
            {
                return false;
            }
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
        var sourceLocations = symbol.Locations.Where(l => l != null && l.IsInSource).ToList();
        if (!sourceLocations.Any())
        {
            _logger.LogDebug("Symbol '{SymbolName}' is not in source code", symbol.Name);
            return false;
        }

        // Additional validation for specific symbol types
        if (!IsSymbolAccessibleForRename(symbol))
        {
            _logger.LogDebug("Symbol '{SymbolName}' is not accessible for rename", symbol.Name);
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
        if (request.SymbolKind == SymbolKind.TypeParameter && !(symbol is ITypeParameterSymbol))
        {
            return false;
        }

        return true;
    }

    private bool MatchesSymbolKind(ISymbol symbol, SymbolKind requestedKind)
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

    private bool IsSymbolAccessibleForRename(ISymbol symbol)
    {
        if (symbol == null)
        {
            return false;
        }

        // Can't rename symbols from metadata
        if (!symbol.Locations.Any(l => l.IsInSource))
            return false;

        // Can't rename compiler-generated symbols
        if (symbol.IsImplicitlyDeclared)
            return false;

        // Can't rename symbols in generated code
        var attributes = symbol.GetAttributes();
        if (attributes.Any(a => a.AttributeClass?.Name == "GeneratedCodeAttribute"))
            return false;

        return true;
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
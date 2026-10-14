using Microsoft.CodeAnalysis;

namespace MCPsharp.Services.Roslyn;

/// <summary>
/// Detection of naming conflicts a rename would introduce.
/// </summary>
public partial class RenameSymbolService
{
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

            if (!_workspace.IsInitialized)
            {
                LogNoCompilationForNamespaceCheck(_logger);
                return;
            }

            var namespaceName = containingNamespace.ToDisplayString();

            // Find all symbols with the target name in the same namespace, in any project of the solution.
            // Namespace symbols from different compilations are distinct instances, so compare by name.
            var existingSymbols = _workspace.FindSourceSymbols(
                n => string.Equals(n, newName, StringComparison.Ordinal),
                SymbolFilter.TypeAndMember, ct)
                .Where(s => s != null &&
                           s.ContainingNamespace != null &&
                           string.Equals(s.ContainingNamespace.ToDisplayString(), namespaceName, StringComparison.Ordinal) &&
                           !CrossProjectSymbolComparer.AreEquivalent(s, originalSymbol) &&
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
                if (iface == null)
                    continue;

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

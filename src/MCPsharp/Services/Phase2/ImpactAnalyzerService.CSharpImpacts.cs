using MCPsharp.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace MCPsharp.Services.Phase2;

public partial class ImpactAnalyzerService
{
    private async Task<IReadOnlyList<CSharpImpact>> FindCSharpImpactsAsync(CodeChange change, CancellationToken ct = default)
    {
        var impacts = new List<CSharpImpact>();

        // Find the symbol in the workspace
        var document = _workspace.GetDocument(change.FilePath);

        if (document == null)
        {
            // Try to add the document to workspace if not found
            await _workspace.AddDocumentAsync(change.FilePath);
            document = _workspace.GetDocument(change.FilePath);
        }

        if (document == null)
            return impacts;

        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel == null)
            return impacts;

        var root = await document.GetSyntaxRootAsync(ct);
        if (root == null)
            return impacts;

        // Find all symbols matching the name (including properties, methods, fields)
        var symbols = root.DescendantNodes()
            .Select(n => semanticModel.GetDeclaredSymbol(n, ct))
            .Where(s => s != null && s.Name.Equals(change.SymbolName, StringComparison.Ordinal))
            .ToList();

        // If we're looking for a property/method but didn't find it directly,
        // try to find symbols that contain methods with this name
        if (symbols.Count == 0)
        {
            var containerSymbols = root.DescendantNodes()
                .Select(n => semanticModel.GetDeclaredSymbol(n, ct))
                .OfType<INamedTypeSymbol>()
                .Where(s => s.GetMembers(change.SymbolName).Any())
                .ToList();

            symbols.AddRange(containerSymbols);
        }

        // Additional fallback: Try to find symbols that contain the name in their properties/methods
        if (symbols.Count == 0)
        {
            var allNamedTypeSymbols = root.DescendantNodes()
                .Select(n => semanticModel.GetDeclaredSymbol(n, ct))
                .OfType<INamedTypeSymbol>()
                .ToList();

            // Check if any type has a property/field/method with the target name
            foreach (var typeSymbol in allNamedTypeSymbols)
            {
                var members = typeSymbol.GetMembers(change.SymbolName);
                if (members.Any())
                {
                    symbols.AddRange(members);
                }
            }
        }

        // Fallback: If no symbols found through Roslyn, do text-based analysis for common scenarios
        if (symbols.Count == 0)
        {
            await FindImpactsByTextAnalysisAsync(change, impacts, ct);
        }

        // Process found symbols through Roslyn
        foreach (var symbol in symbols.OfType<ISymbol>())
        {
            // Find all references to this symbol across every project in the solution
            if (!_workspace.IsInitialized)
                continue;

            var solution = _workspace.Solution;
            var references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    if (location.Document.FilePath == null)
                        continue;

                    // Skip only implicit locations, but include implementations in other files
                    if (location.IsImplicit)
                        continue;

                    var lineSpan = location.Location.GetLineSpan();
                    var impactType = DetermineImpactType(change, symbol);

                    impacts.Add(new CSharpImpact
                    {
                        FilePath = location.Document.FilePath,
                        Line = lineSpan.StartLinePosition.Line + 1,
                        ImpactType = impactType,
                        Description = $"{impactType} in {symbol.ContainingType?.Name ?? symbol.Name}: {change.ChangeType}"
                    });
                }
            }

            // For interface changes, also find implementations
            if (symbol.Kind == Microsoft.CodeAnalysis.SymbolKind.NamedType &&
                ((INamedTypeSymbol)symbol).TypeKind == TypeKind.Interface)
            {
                await FindInterfaceImplementationsAsync((INamedTypeSymbol)symbol, solution, _workspace.GetAllDocuments(), impacts, change, ct);
            }
        }

        // Always run text-based search as a fallback to catch missed references
        await FindImpactsByTextAnalysisAsync(change, impacts, ct);

        // For config changes, also find C# files that reference the config
        if (change.ChangeType.Contains("Config") || change.FilePath.EndsWith(".json", StringComparison.Ordinal) || change.FilePath.EndsWith(".yml", StringComparison.Ordinal) || change.FilePath.EndsWith(".yaml", StringComparison.Ordinal))
        {
            var rootProject = GetProjectRoot(change.FilePath);
            await FindConfigReferencesInCSharpAsync(change, rootProject, impacts, ct);
        }

        return impacts;
    }

    private static async Task FindInterfaceImplementationsAsync(
        INamedTypeSymbol interfaceSymbol,
        Solution solution,
        IReadOnlyList<Document> allDocuments,
        List<CSharpImpact> impacts,
        CodeChange change,
        CancellationToken ct)
    {
        // Find all classes that implement this interface
        var implementations = await SymbolFinder.FindImplementationsAsync(interfaceSymbol, solution, cancellationToken: ct);

        foreach (var implementation in implementations.Where(i => i.Locations.Any(l => l.SourceTree?.FilePath != null)))
        {
            var filePath = implementation.Locations.First(l => l.SourceTree?.FilePath != null).SourceTree!.FilePath;
            if (!string.IsNullOrEmpty(filePath) && filePath != change.FilePath)
            {
                impacts.Add(new CSharpImpact
                {
                    FilePath = filePath,
                    Line = 1, // We don't have precise line info for implementations
                    ImpactType = "interface_implementation",
                    Description = $"Class {implementation.Name} implements interface {interfaceSymbol.Name}"
                });
            }
        }

        // Fallback: Manually scan all documents for implementations if SymbolFinder didn't work
        if (!implementations.Any())
        {
            foreach (var doc in allDocuments)
            {
                if (string.IsNullOrEmpty(doc.FilePath) || doc.FilePath == change.FilePath)
                    continue;

                try
                {
                    var semanticModel = await doc.GetSemanticModelAsync(ct);
                    var root = await doc.GetSyntaxRootAsync(ct);

                    if (semanticModel != null && root != null)
                    {
                        // Look for class declarations that implement the interface
                        var classDeclarations = root.DescendantNodes()
                            .Where(n => n.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ClassDeclaration))
                            .ToList();

                        foreach (var classDecl in classDeclarations)
                        {
                            var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct);
                            if (classSymbol is INamedTypeSymbol namedTypeSymbol)
                            {
                                var implementsInterface = namedTypeSymbol.AllInterfaces
                                    .Any(i => i.Name.Equals(interfaceSymbol.Name, StringComparison.Ordinal));

                                if (implementsInterface)
                                {
                                    impacts.Add(new CSharpImpact
                                    {
                                        FilePath = doc.FilePath,
                                        Line = classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                                        ImpactType = "interface_implementation",
                                        Description = $"Class {namedTypeSymbol.Name} implements interface {interfaceSymbol.Name}"
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Ignore errors in individual documents; the text-based search below still runs
                }
            }
        }

        // Final fallback: Text-based search for interface implementations
        var projectRoot = GetProjectRoot(change.FilePath);
        await FindInterfaceImplementationsByTextAsync(interfaceSymbol, projectRoot, impacts, change, ct);
    }

    private static string DetermineImpactType(CodeChange change, ISymbol symbol)
    {
        return change.ChangeType switch
        {
            "delete" => "breaking_change",
            "signature_change" => "breaking_change",
            "rename" => "breaking_change",
            _ when symbol.DeclaredAccessibility == Microsoft.CodeAnalysis.Accessibility.Public => "reference",
            _ => "reference"
        };
    }
}

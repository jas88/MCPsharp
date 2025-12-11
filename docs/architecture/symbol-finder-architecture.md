# Symbol Finder Architecture

## Problem Statement

The RenameSymbolService has a fundamental architectural flaw in its `FindSymbolAsync()` method:

### Current Issues

1. **Ad-hoc Search Mechanisms**: Uses different search approaches for different symbol types without a unified strategy
2. **Incomplete Coverage**: Local variables and parameters were added as afterthought fallbacks instead of being first-class search targets
3. **Fragile Selection Logic**: `SelectBestCandidate()` has complex scoring logic that's hard to maintain and reason about
4. **Limited Roslyn API Usage**: `compilation.GetSymbolsWithName()` only works for compilation-level symbols (types, members, namespaces)
5. **Scope Blindness**: Cannot properly search scope-local symbols (locals, parameters) which require document-level analysis

### Why This Matters

Symbol renaming is safety-critical - missing a symbol or selecting the wrong one can break code. The current implementation:
- Failed to find local variables initially (fixed in recent commits)
- Has no clear documentation of which symbol types are handled
- Makes it hard to add new symbol search strategies
- Mixes concerns: search logic, filtering, and selection all intertwined

## Solution: Strategy Pattern for Symbol Search

### Design Principles

1. **Separation of Concerns**: Search, filter, and select as distinct operations
2. **Strategy per Symbol Kind**: Each symbol type gets its own specialized search strategy
3. **Explicit Scope Handling**: Clear distinction between compilation-level and scope-local symbols
4. **Composability**: Strategies can be combined for fallback and multi-pass search
5. **Testability**: Each strategy can be tested independently

### Architecture Overview

```
┌────────────────────────────────────┐
│   RenameSymbolService              │
│                                    │
│   - Uses SymbolFinderService       │
│   - Focuses on rename logic        │
└────────────────┬───────────────────┘
                 │
                 │ delegates to
                 ▼
┌────────────────────────────────────┐
│   SymbolFinderService              │
│                                    │
│   + FindSymbolAsync()              │
│   + GetSearchStrategy()            │
│   - _strategies: Dictionary        │
└────────────────┬───────────────────┘
                 │
                 │ owns
                 ▼
┌────────────────────────────────────┐
│   ISymbolSearchStrategy            │
│                                    │
│   + SearchAsync()                  │
│   + ApplicableFor(SymbolKind)     │
└────────────────┬───────────────────┘
                 │
                 │ implementations
                 │
    ┌────────────┼────────────┬────────────┬───────────┐
    │            │            │            │           │
    ▼            ▼            ▼            ▼           ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌─────────┐ ┌──────────┐
│Compila-│ │ Scope  │ │Position│ │Document │ │Fallback  │
│tion    │ │ Local  │ │ Based  │ │ Walker  │ │Strategy  │
│Strategy│ │Strategy│ │Strategy│ │Strategy │ │          │
└────────┘ └────────┘ └────────┘ └─────────┘ └──────────┘
```

### Strategy Mapping

| Symbol Type    | Primary Strategy      | Fallback Strategy     | Notes                          |
|----------------|-----------------------|-----------------------|--------------------------------|
| Class          | CompilationStrategy   | PositionBasedStrategy | Uses GetSymbolsWithName()      |
| Interface      | CompilationStrategy   | PositionBasedStrategy | Uses GetSymbolsWithName()      |
| Method         | CompilationStrategy   | PositionBasedStrategy | Handles overloads              |
| Property       | CompilationStrategy   | PositionBasedStrategy | Normalizes accessors           |
| Field          | CompilationStrategy   | PositionBasedStrategy | Uses GetSymbolsWithName()      |
| Namespace      | CompilationStrategy   | -                     | Always compilation-level       |
| TypeParameter  | CompilationStrategy   | DocumentWalkerStrategy| Generic type parameters        |
| Local          | ScopeLocalStrategy    | DocumentWalkerStrategy| Requires semantic model        |
| Parameter      | ScopeLocalStrategy    | DocumentWalkerStrategy| Requires semantic model        |

### Strategy Descriptions

#### 1. CompilationStrategy
**Purpose**: Search for compilation-level symbols (types, members, namespaces)

**Implementation**:
```csharp
public class CompilationStrategy : ISymbolSearchStrategy
{
    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request)
    {
        var compilation = _workspace.GetCompilation();
        var filters = new[] {
            SymbolFilter.TypeAndMember,
            SymbolFilter.Type,
            SymbolFilter.Member,
            SymbolFilter.Namespace
        };

        var results = new List<ISymbol>();
        foreach (var filter in filters)
        {
            var symbols = compilation.GetSymbolsWithName(
                n => string.Equals(n, request.Name, StringComparison.OrdinalIgnoreCase),
                filter);
            results.AddRange(symbols);
        }

        return results;
    }
}
```

**Best For**: Classes, interfaces, methods, properties, fields, namespaces

#### 2. ScopeLocalStrategy
**Purpose**: Search for scope-local symbols (locals, parameters)

**Implementation**:
```csharp
public class ScopeLocalStrategy : ISymbolSearchStrategy
{
    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request)
    {
        var results = new List<ISymbol>();

        // Strategy: Must walk all documents and semantic models
        // because locals/parameters are not in compilation symbol table
        foreach (var document in _workspace.GetAllDocuments())
        {
            var semanticModel = await _workspace.GetSemanticModelAsync(document);
            var syntaxRoot = await document.GetSyntaxRootAsync();

            // Find all identifiers with matching name
            var identifiers = syntaxRoot.DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(id => string.Equals(id.Identifier.Text,
                    request.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var identifier in identifiers)
            {
                var symbolInfo = semanticModel.GetSymbolInfo(identifier);
                var symbol = symbolInfo.Symbol;

                if (symbol is ILocalSymbol || symbol is IParameterSymbol)
                {
                    results.Add(symbol);
                }
            }
        }

        return results.Distinct(SymbolEqualityComparer.Default).ToList();
    }
}
```

**Best For**: Local variables, method parameters

#### 3. PositionBasedStrategy
**Purpose**: Find symbol at specific file location

**Implementation**:
```csharp
public class PositionBasedStrategy : ISymbolSearchStrategy
{
    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request)
    {
        if (request.FilePath == null || !request.Line.HasValue || !request.Column.HasValue)
        {
            return new List<ISymbol>();
        }

        var document = _workspace.GetDocument(request.FilePath);
        var syntaxTree = await document.GetSyntaxTreeAsync();
        var text = await syntaxTree.GetTextAsync();

        var line = text.Lines[request.Line.Value - 1];
        var position = line.Start + request.Column.Value;

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, position);

        return symbol != null ? new List<ISymbol> { symbol } : new List<ISymbol>();
    }
}
```

**Best For**: When user provides precise location (preferred approach)

#### 4. DocumentWalkerStrategy
**Purpose**: Exhaustive search when other strategies fail

**Implementation**:
```csharp
public class DocumentWalkerStrategy : ISymbolSearchStrategy
{
    public async Task<List<ISymbol>> SearchAsync(SymbolSearchRequest request)
    {
        // Similar to ScopeLocalStrategy but checks ALL symbol types
        // Last resort - comprehensive but slower
    }
}
```

**Best For**: Fallback for edge cases

### Symbol Selection Algorithm

After strategies return candidates, we need to select the best match:

```csharp
public class SymbolSelector
{
    public ISymbol SelectBest(List<ISymbol> candidates, SymbolSearchRequest request)
    {
        // 1. Filter by symbol kind
        if (request.SymbolKind != SymbolKind.Any)
        {
            candidates = candidates.Where(c => MatchesKind(c, request.SymbolKind)).ToList();
        }

        // 2. Filter by containing type
        if (!string.IsNullOrEmpty(request.ContainingType))
        {
            candidates = candidates
                .Where(c => c.ContainingType?.Name == request.ContainingType)
                .ToList();
        }

        // 3. Score remaining candidates
        var scored = candidates
            .Select(c => new { Symbol = c, Score = CalculateScore(c, request) })
            .OrderByDescending(x => x.Score)
            .ToList();

        return scored.First().Symbol;
    }

    private int CalculateScore(ISymbol symbol, SymbolSearchRequest request)
    {
        int score = 0;

        // Exact name match
        if (symbol.Name == request.Name) score += 100;

        // In source (not metadata)
        if (symbol.Locations.Any(l => l.IsInSource)) score += 50;

        // Matches file path
        if (request.FilePath != null &&
            symbol.Locations.Any(l => l.GetLineSpan().Path == request.FilePath))
            score += 30;

        // Not compiler-generated
        if (!symbol.IsImplicitlyDeclared) score += 25;

        return score;
    }
}
```

### Request/Response Models

```csharp
public class SymbolSearchRequest
{
    public required string Name { get; init; }
    public SymbolKind SymbolKind { get; init; } = SymbolKind.Any;
    public string? ContainingType { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}

public class SymbolSearchResult
{
    public ISymbol? Symbol { get; init; }
    public List<ISymbol> Candidates { get; init; } = new();
    public SymbolSearchStrategy UsedStrategy { get; init; }
    public bool IsAmbiguous { get; init; }
}
```

## Implementation Plan

### Phase 1: Core Infrastructure
1. Create `ISymbolSearchStrategy` interface
2. Create `SymbolSearchRequest` and `SymbolSearchResult` models
3. Create `SymbolSelector` for candidate selection

### Phase 2: Strategy Implementations
1. Implement `CompilationStrategy`
2. Implement `ScopeLocalStrategy`
3. Implement `PositionBasedStrategy`
4. Implement `DocumentWalkerStrategy` (fallback)

### Phase 3: Service Layer
1. Create `SymbolFinderService` that orchestrates strategies
2. Add strategy registration and selection logic
3. Add caching for repeated searches

### Phase 4: Integration
1. Refactor `RenameSymbolService.FindSymbolAsync()` to use `SymbolFinderService`
2. Keep existing tests passing (backward compatibility)
3. Add new tests for each strategy

### Phase 5: Documentation
1. Add XML comments to all public APIs
2. Create usage examples
3. Document strategy selection rules

## Benefits of New Architecture

1. **Clarity**: Each symbol type has an explicit, documented search path
2. **Maintainability**: Add new strategies without modifying existing code
3. **Testability**: Test each strategy in isolation
4. **Performance**: Can optimize hot paths (e.g., position-based search first)
5. **Extensibility**: Easy to add new symbol types or search approaches
6. **Debuggability**: Clear logging of which strategy was used

## Backward Compatibility

The new architecture is a refactoring, not a breaking change:

1. `RenameSymbolService.FindSymbolAsync()` signature remains the same
2. All existing tests should pass
3. Behavior is identical, implementation is cleaner
4. Migration is transparent to callers

## Future Enhancements

1. **Strategy Caching**: Cache compilation-level symbol lookups
2. **Parallel Search**: Run multiple strategies concurrently
3. **Smart Strategy Selection**: Learn from past searches to optimize strategy order
4. **Incremental Search**: For interactive scenarios (autocomplete, etc.)

## References

- Roslyn API: `compilation.GetSymbolsWithName()`
- Roslyn API: `SymbolFinder.FindSymbolAtPositionAsync()`
- Existing code: `SymbolResolutionService` (similar multi-strategy approach)
- Design Pattern: Strategy Pattern (GoF)

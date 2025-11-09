# MCPsharp Code Navigation Feature Design

## Executive Summary

This document presents a comprehensive architecture and implementation strategy for code navigation features in MCPsharp. The design addresses the challenge of providing IDE-like navigation capabilities through a stateless MCP protocol, leveraging existing Roslyn infrastructure while introducing new navigation-specific services.

## 1. Navigation Types Analysis

### 1.1 Core Navigation Features

#### Go to Definition
- **Purpose**: Jump from symbol usage to its declaration
- **Input**: File path + line + column (current cursor position)
- **Output**: Single location (file + line + column) or null if not found
- **Challenges**:
  - Symbol at position might be ambiguous (e.g., overloaded methods)
  - Symbol might be in metadata (external library)
  - Partial classes/methods spread across files

#### Find Overrides
- **Purpose**: Find all implementations that override a virtual/abstract member
- **Input**: Symbol identifier (by location or name + context)
- **Output**: List of locations where overrides exist
- **Challenges**:
  - Deep inheritance hierarchies
  - Interface implementations vs class overrides
  - Explicit vs implicit interface implementations

#### Find Overloads
- **Purpose**: Find all method overloads with same name but different parameters
- **Input**: Method symbol (by location or name + containing type)
- **Output**: List of method signatures with locations
- **Challenges**:
  - Generic methods with type constraints
  - Extension methods
  - Operator overloads

#### Find Base Symbol
- **Purpose**: Navigate to base class/interface definition
- **Input**: Derived symbol (by location)
- **Output**: Base symbol location(s)
- **Challenges**:
  - Multiple inheritance (interfaces)
  - Shadow/new members
  - Generic type parameters

#### Find Derived Types
- **Purpose**: Find all types that inherit from or implement a type
- **Input**: Base type symbol
- **Output**: List of derived type locations
- **Challenges**:
  - Solution-wide search performance
  - Partial classes
  - Generic type specializations

### 1.2 Existing Related Features
- `find_references` - Already implemented
- `find_implementations` - Already implemented
- `find_callers` - Already implemented
- `find_call_chains` - Already implemented

## 2. Architecture Design

### 2.1 Component Hierarchy

```
MCP Layer (Stateless Interface)
    ↓
NavigationService (Orchestration)
    ├─ SymbolResolutionService (Location → Symbol)
    ├─ DefinitionNavigator (Go to Definition)
    ├─ InheritanceNavigator (Overrides, Base, Derived)
    ├─ OverloadNavigator (Method Overloads)
    └─ NavigationCache (Performance Optimization)
         ↓
Existing Roslyn Services
    ├─ RoslynWorkspace
    ├─ SymbolQueryService
    ├─ AdvancedReferenceFinderService
    └─ TypeUsageService
```

### 2.2 Service Responsibilities

#### NavigationService (Main Orchestrator)
```csharp
public interface INavigationService
{
    Task<NavigationResult> GoToDefinitionAsync(string filePath, int line, int column);
    Task<MultiNavigationResult> FindOverridesAsync(string filePath, int line, int column);
    Task<MultiNavigationResult> FindOverloadsAsync(string filePath, int line, int column);
    Task<NavigationResult> FindBaseSymbolAsync(string filePath, int line, int column);
    Task<MultiNavigationResult> FindDerivedTypesAsync(string filePath, int line, int column);
    Task<NavigationResult> GoToImplementationAsync(string filePath, int line, int column);
}
```

#### SymbolResolutionService
```csharp
public interface ISymbolResolutionService
{
    Task<ResolvedSymbol?> ResolveSymbolAtPositionAsync(
        string filePath, int line, int column,
        SymbolResolutionOptions options = default);

    Task<ISymbol?> ResolveSymbolByNameAsync(
        string symbolName,
        string? containingType = null,
        string? containingNamespace = null);
}

public class ResolvedSymbol
{
    public ISymbol Symbol { get; init; }
    public SymbolLocation DeclarationLocation { get; init; }
    public SymbolConfidence Confidence { get; init; }
    public List<ISymbol>? AlternativeSymbols { get; init; }
}

public enum SymbolConfidence
{
    Exact = 100,
    High = 80,
    Medium = 60,
    Low = 40,
    Ambiguous = 20
}
```

#### NavigationCache
```csharp
public interface INavigationCache
{
    void CacheSymbolHierarchy(INamedTypeSymbol type, HierarchyInfo info);
    HierarchyInfo? GetCachedHierarchy(INamedTypeSymbol type);
    void InvalidateForFile(string filePath);
    void Clear();
}
```

### 2.3 Stateless Symbol Identification Strategy

Since MCP is stateless, we need robust ways to identify symbols:

1. **Primary: Location-based** (file + line + column)
   - Most natural for editor integration
   - Maps directly to cursor position

2. **Secondary: Qualified Name** (namespace.type.member)
   - Fallback when location is ambiguous
   - Useful for external references

3. **Tertiary: Symbol ID** (generated hash)
   - For round-trip scenarios
   - Cache key for performance

## 3. API Specification

### 3.1 Go to Definition Tool

```json
{
  "name": "go_to_definition",
  "description": "Navigate to the definition of a symbol at the specified location",
  "inputSchema": {
    "type": "object",
    "properties": {
      "filePath": {
        "type": "string",
        "description": "Path to the file containing the symbol"
      },
      "line": {
        "type": "integer",
        "description": "0-indexed line number"
      },
      "column": {
        "type": "integer",
        "description": "0-indexed column number"
      },
      "includeContent": {
        "type": "boolean",
        "description": "Include surrounding code context",
        "default": false
      }
    },
    "required": ["filePath", "line", "column"]
  }
}
```

**Response Format:**
```json
{
  "definition": {
    "file": "/path/to/file.cs",
    "line": 42,
    "column": 8,
    "symbol": {
      "name": "MyMethod",
      "kind": "method",
      "signature": "public void MyMethod(string param)",
      "containingType": "MyClass",
      "namespace": "MyApp.Services"
    },
    "confidence": "exact",
    "context": {
      "before": ["    public class MyClass", "    {"],
      "target": "        public void MyMethod(string param)",
      "after": ["        {", "            // Implementation"]
    }
  },
  "alternatives": []
}
```

### 3.2 Find Overrides Tool

```json
{
  "name": "find_overrides",
  "description": "Find all overrides of a virtual/abstract member",
  "inputSchema": {
    "type": "object",
    "properties": {
      "filePath": { "type": "string" },
      "line": { "type": "integer" },
      "column": { "type": "integer" },
      "includeInterface": {
        "type": "boolean",
        "description": "Include interface implementations",
        "default": true
      }
    },
    "required": ["filePath", "line", "column"]
  }
}
```

**Response Format:**
```json
{
  "baseSymbol": {
    "name": "Process",
    "signature": "public virtual void Process()",
    "declaringType": "BaseProcessor"
  },
  "overrides": [
    {
      "file": "/path/to/derived.cs",
      "line": 15,
      "column": 20,
      "declaringType": "DerivedProcessor",
      "signature": "public override void Process()",
      "isSealed": false,
      "isExplicit": false
    }
  ],
  "totalFound": 3
}
```

### 3.3 Find Overloads Tool

```json
{
  "name": "find_overloads",
  "description": "Find all overloaded versions of a method",
  "inputSchema": {
    "type": "object",
    "properties": {
      "filePath": { "type": "string" },
      "line": { "type": "integer" },
      "column": { "type": "integer" },
      "includeExtensions": {
        "type": "boolean",
        "description": "Include extension method overloads",
        "default": false
      }
    },
    "required": ["filePath", "line", "column"]
  }
}
```

**Response Format:**
```json
{
  "methodName": "Calculate",
  "containingType": "Calculator",
  "overloads": [
    {
      "signature": "public int Calculate(int a, int b)",
      "parameters": ["int a", "int b"],
      "returnType": "int",
      "file": "/path/to/calculator.cs",
      "line": 10,
      "column": 12,
      "isCurrentMethod": true
    },
    {
      "signature": "public double Calculate(double a, double b)",
      "parameters": ["double a", "double b"],
      "returnType": "double",
      "file": "/path/to/calculator.cs",
      "line": 15,
      "column": 12,
      "isCurrentMethod": false
    }
  ],
  "totalOverloads": 2
}
```

### 3.4 Find Base Symbol Tool

```json
{
  "name": "find_base_symbol",
  "description": "Navigate to the base class or interface definition",
  "inputSchema": {
    "type": "object",
    "properties": {
      "filePath": { "type": "string" },
      "line": { "type": "integer" },
      "column": { "type": "integer" },
      "findOriginalDefinition": {
        "type": "boolean",
        "description": "Navigate to the original virtual/abstract definition",
        "default": true
      }
    },
    "required": ["filePath", "line", "column"]
  }
}
```

### 3.5 Find Derived Types Tool

```json
{
  "name": "find_derived_types",
  "description": "Find all types that derive from or implement this type",
  "inputSchema": {
    "type": "object",
    "properties": {
      "filePath": { "type": "string" },
      "line": { "type": "integer" },
      "column": { "type": "integer" },
      "includeSealedTypes": {
        "type": "boolean",
        "description": "Include sealed derived types",
        "default": true
      },
      "maxDepth": {
        "type": "integer",
        "description": "Maximum inheritance depth to search",
        "default": -1
      }
    },
    "required": ["filePath", "line", "column"]
  }
}
```

## 4. Symbol Resolution Algorithm

### 4.1 Core Resolution Logic

```csharp
public async Task<ResolvedSymbol?> ResolveSymbolAtPositionAsync(
    string filePath, int line, int column,
    SymbolResolutionOptions options)
{
    // Step 1: Get syntax tree and semantic model
    var document = _workspace.GetDocument(filePath);
    var syntaxTree = await document.GetSyntaxTreeAsync();
    var semanticModel = await document.GetSemanticModelAsync();

    // Step 2: Find token at position
    var position = GetPosition(syntaxTree, line, column);
    var token = syntaxTree.GetRoot().FindToken(position);

    // Step 3: Try multiple resolution strategies
    ISymbol? symbol = null;
    var confidence = SymbolConfidence.Exact;

    // Strategy 1: Direct symbol info
    var symbolInfo = semanticModel.GetSymbolInfo(token.Parent);
    if (symbolInfo.Symbol != null)
    {
        symbol = symbolInfo.Symbol;
    }
    // Strategy 2: Check for declaration
    else if (token.Parent is MemberDeclarationSyntax memberDecl)
    {
        symbol = semanticModel.GetDeclaredSymbol(memberDecl);
    }
    // Strategy 3: Check for type syntax
    else if (token.Parent is TypeSyntax typeSyntax)
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
        symbol = typeInfo.Type;
    }
    // Strategy 4: Handle ambiguous cases
    else if (symbolInfo.CandidateSymbols.Length > 0)
    {
        symbol = SelectBestCandidate(symbolInfo.CandidateSymbols, options);
        confidence = SymbolConfidence.Ambiguous;
    }

    // Step 4: Get original definition if needed
    if (symbol != null && options.ResolveToOriginalDefinition)
    {
        symbol = symbol.OriginalDefinition;
    }

    // Step 5: Build result
    return symbol == null ? null : new ResolvedSymbol
    {
        Symbol = symbol,
        DeclarationLocation = GetDeclarationLocation(symbol),
        Confidence = confidence,
        AlternativeSymbols = symbolInfo.CandidateSymbols
            .Where(s => s != symbol)
            .ToList()
    };
}
```

### 4.2 Handling Edge Cases

#### Partial Classes/Methods
```csharp
private async Task<List<SymbolLocation>> GetAllPartialLocations(ISymbol symbol)
{
    var locations = new List<SymbolLocation>();

    foreach (var location in symbol.Locations)
    {
        if (location.IsInSource)
        {
            var lineSpan = location.GetLineSpan();
            locations.Add(new SymbolLocation
            {
                FilePath = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                IsPartial = symbol.IsPartialDefinition()
            });
        }
    }

    // Sort by file path for consistent ordering
    return locations.OrderBy(l => l.FilePath).ToList();
}
```

#### External Symbols (Metadata)
```csharp
private NavigationResult HandleMetadataSymbol(ISymbol symbol)
{
    return new NavigationResult
    {
        Success = false,
        IsExternal = true,
        ExternalInfo = new ExternalSymbolInfo
        {
            AssemblyName = symbol.ContainingAssembly?.Name,
            TypeName = symbol.ContainingType?.ToDisplayString(),
            MemberName = symbol.Name,
            Documentation = symbol.GetDocumentationCommentXml(),
            CanDecompile = _decompiledSources.CanDecompile(symbol)
        },
        Message = $"Symbol '{symbol.Name}' is defined in external assembly '{symbol.ContainingAssembly?.Name}'"
    };
}
```

## 5. Performance Optimization Strategy

### 5.1 Multi-Level Caching

```csharp
public class NavigationCacheManager
{
    private readonly MemoryCache _symbolCache;       // L1: Hot symbols
    private readonly MemoryCache _hierarchyCache;    // L2: Type hierarchies
    private readonly MemoryCache _overloadCache;     // L3: Method overloads

    public NavigationCacheManager()
    {
        var options = new MemoryCacheOptions
        {
            SizeLimit = 10000,
            CompactionPercentage = 0.25
        };

        _symbolCache = new MemoryCache(options);
        _hierarchyCache = new MemoryCache(options);
        _overloadCache = new MemoryCache(options);
    }

    public async Task<T> GetOrAddAsync<T>(
        string key,
        Func<Task<T>> factory,
        CacheLevel level = CacheLevel.L1)
    {
        var cache = GetCache(level);

        if (cache.TryGetValue(key, out T cachedValue))
        {
            return cachedValue;
        }

        var value = await factory();

        var entryOptions = new MemoryCacheEntryOptions()
            .SetSize(1)
            .SetSlidingExpiration(GetExpiration(level))
            .RegisterPostEvictionCallback(OnEviction);

        cache.Set(key, value, entryOptions);
        return value;
    }
}
```

### 5.2 Lazy Loading Strategy

```csharp
public class LazyHierarchyLoader
{
    private readonly ConcurrentDictionary<string, Task<HierarchyInfo>> _loadingTasks;

    public async Task<HierarchyInfo> GetHierarchyAsync(
        INamedTypeSymbol type,
        HierarchyOptions options)
    {
        var key = GetHierarchyKey(type, options);

        return await _loadingTasks.GetOrAdd(key, async _ =>
        {
            var info = new HierarchyInfo { Type = type };

            // Load immediate relationships first
            if (options.IncludeBase)
            {
                info.BaseType = type.BaseType;
            }

            if (options.IncludeInterfaces)
            {
                info.Interfaces = type.Interfaces.ToList();
            }

            // Load derived types lazily if requested
            if (options.IncludeDerived)
            {
                info.DerivedTypesLoader = new Lazy<Task<List<INamedTypeSymbol>>>(
                    () => FindDerivedTypesAsync(type, options.MaxDepth));
            }

            return info;
        });
    }
}
```

### 5.3 Incremental Updates

```csharp
public class IncrementalNavigationIndex
{
    private readonly ConcurrentDictionary<string, FileSymbolIndex> _fileIndices;
    private readonly IFileWatcher _fileWatcher;

    public IncrementalNavigationIndex(IFileWatcher fileWatcher)
    {
        _fileWatcher = fileWatcher;
        _fileWatcher.FileChanged += OnFileChanged;
    }

    private async void OnFileChanged(string filePath)
    {
        // Invalidate only affected portions
        if (_fileIndices.TryRemove(filePath, out var oldIndex))
        {
            // Find symbols that might be affected
            var affectedSymbols = oldIndex.GetPublicSymbols();

            // Invalidate related caches
            foreach (var symbol in affectedSymbols)
            {
                InvalidateSymbolCaches(symbol);
            }

            // Rebuild index for this file only
            await RebuildFileIndexAsync(filePath);
        }
    }
}
```

## 6. Integration Plan

### 6.1 Phase 1: Core Navigation (Week 1-2)
1. Implement `SymbolResolutionService`
2. Implement `NavigationService` with `GoToDefinition`
3. Add MCP tool registration
4. Unit tests for symbol resolution

### 6.2 Phase 2: Inheritance Navigation (Week 2-3)
1. Implement `InheritanceNavigator`
2. Add `FindOverrides`, `FindBaseSymbol`, `FindDerivedTypes`
3. Implement hierarchy caching
4. Integration tests with complex inheritance

### 6.3 Phase 3: Advanced Features (Week 3-4)
1. Implement `OverloadNavigator`
2. Add metadata symbol handling
3. Implement performance optimizations
4. Load testing with large projects

### 6.4 Phase 4: Polish & Edge Cases (Week 4-5)
1. Handle all edge cases (partial, generic, explicit interface)
2. Add confidence scoring
3. Implement incremental updates
4. Documentation and examples

## 7. Edge Case Handling Matrix

| Edge Case | Detection Strategy | Resolution Approach | Confidence Impact |
|-----------|-------------------|--------------------|--------------------|
| Partial Classes | Multiple `symbol.Locations` | Return primary location + list of partials | High (80%) |
| Partial Methods | `symbol.IsPartialDefinition()` | Navigate to implementation if exists | High (80%) |
| Generic Specializations | Check for `ConstructedFrom` | Resolve to generic definition | Medium (60%) |
| Extension Methods | `IMethodSymbol.IsExtensionMethod` | Include in overloads with flag | Exact (100%) |
| Explicit Interface Impl | `IMethodSymbol.ExplicitInterfaceImplementations` | Show interface + implementation | Exact (100%) |
| Operator Overloads | `IMethodSymbol.MethodKind == Operator` | Special formatting in response | Exact (100%) |
| Property Accessors | Parent is `IPropertySymbol` | Navigate to property, not accessor | High (80%) |
| Anonymous Types | `ITypeSymbol.IsAnonymousType` | Show inline definition | Low (40%) |
| Lambdas/Delegates | `IMethodSymbol.MethodKind == AnonymousFunction` | Navigate to declaration site | Medium (60%) |
| External Symbols | `!location.IsInSource` | Return metadata info + decompile option | N/A |
| Renamed Symbols | Track through `ISymbol.Name` changes | Use semantic model for resolution | High (80%) |
| Shadowed Members | Check `new` modifier | Show both base and derived | Ambiguous (20%) |
| Conditional Compilation | `#if` directives | Resolve based on current configuration | Medium (60%) |

## 8. Response Examples

### 8.1 Successful Navigation
```json
{
  "success": true,
  "navigation": {
    "file": "/src/Services/OrderService.cs",
    "line": 45,
    "column": 12,
    "symbol": {
      "name": "ProcessOrder",
      "kind": "method",
      "signature": "public async Task<OrderResult> ProcessOrder(Order order)"
    }
  },
  "confidence": "exact",
  "executionTime": 23
}
```

### 8.2 Ambiguous Symbol
```json
{
  "success": true,
  "navigation": {
    "file": "/src/Services/Calculator.cs",
    "line": 30,
    "column": 8,
    "symbol": {
      "name": "Calculate",
      "kind": "method"
    }
  },
  "confidence": "ambiguous",
  "alternatives": [
    {
      "signature": "int Calculate(int a, int b)",
      "line": 30
    },
    {
      "signature": "double Calculate(double a, double b)",
      "line": 35
    }
  ],
  "message": "Multiple overloads found. Showing best match."
}
```

### 8.3 External Symbol
```json
{
  "success": false,
  "isExternal": true,
  "externalInfo": {
    "assemblyName": "System.Collections.Generic",
    "typeName": "List<T>",
    "memberName": "Add",
    "documentation": "Adds an object to the end of the List<T>."
  },
  "message": "Symbol is defined in external assembly"
}
```

## 9. Testing Strategy

### 9.1 Unit Tests
- Symbol resolution accuracy
- Cache invalidation logic
- Edge case handling
- Performance benchmarks

### 9.2 Integration Tests
- Real C# projects of varying sizes
- Complex inheritance hierarchies
- Cross-project references
- Partial class scenarios

### 9.3 Performance Tests
- Large solution navigation (10,000+ files)
- Deep inheritance chains (10+ levels)
- Many overloads (20+ methods)
- Cache hit rates

## 10. Success Metrics

1. **Accuracy**: 95%+ correct navigation for standard cases
2. **Performance**: <100ms for local navigation, <500ms for solution-wide
3. **Cache Hit Rate**: >80% for repeated navigations
4. **Memory Usage**: <500MB for 10,000 file project
5. **Edge Case Coverage**: 100% of identified edge cases handled

## Conclusion

This design provides a robust, performant, and comprehensive navigation system for MCPsharp that bridges the gap between stateless MCP requests and stateful IDE-like navigation. The architecture leverages existing Roslyn infrastructure while adding navigation-specific optimizations and edge case handling, ensuring developers get accurate and fast navigation capabilities.
# Architecture Decision: Direct Roslyn vs csharp-ls Subprocess

## Question

Should we integrate csharp-ls code directly (or use Roslyn APIs directly) rather than spawning csharp-ls as a subprocess and communicating via LSP?

## Decision: Use Roslyn APIs Directly ✅

After analysis, **direct Roslyn integration is superior** for an MCP server. We should NOT use csharp-ls as a subprocess.

## Rationale

### Why NOT subprocess csharp-ls

**Cons of subprocess approach:**
1. **IPC overhead** — Every query requires stdin/stdout JSON-RPC round-trip
2. **Process management complexity** — Start, stop, health checks, restarts
3. **Two failure modes** — Both MCP server AND csharp-ls can fail independently
4. **LSP protocol mismatch** — LSP designed for text editor integration, not our use case
5. **Memory overhead** — Two separate .NET processes with their own GC heaps
6. **Debugging difficulty** — Stack traces split across processes
7. **Deployment complexity** — Bundle two binaries instead of one

**Limited benefits:**
- "Clean separation" — Not valuable when both are .NET in same codebase
- "Independent updates" — csharp-ls updates are infrequent; Roslyn is more stable

### Why Direct Roslyn APIs

**Major advantages:**
1. **Single process** — Simpler deployment, debugging, monitoring
2. **Zero IPC overhead** — Direct method calls, no serialization
3. **Shared memory** — Cache compilation state efficiently
4. **Full control** — Customize analysis for MCP use cases
5. **Roslyn is stable** — Microsoft maintains it, excellent documentation
6. **Better performance** — No subprocess startup time, no JSON-RPC overhead
7. **Easier testing** — Unit test semantic operations directly

**What we actually need from csharp-ls:**
- Parse C# files → `CSharpSyntaxTree.ParseText()`
- Find symbols → `Compilation.GetSymbolsWithName()`
- Get type info → `SemanticModel.GetTypeInfo()`
- Apply edits → Roslyn `SyntaxRewriter` or text manipulation

**All of these are direct Roslyn APIs.** We don't need LSP.

### Comparison Table

| Feature | Subprocess csharp-ls | Direct Roslyn |
|---------|---------------------|---------------|
| **Deployment** | Two binaries (csharp-ls + MCP server) | Single binary |
| **Performance** | IPC overhead, serialization | Direct method calls |
| **Memory** | Two process heaps | Single process heap |
| **Debugging** | Complex (two processes) | Simple (single process) |
| **Customization** | Limited (LSP protocol constraints) | Full (direct API access) |
| **Testing** | Integration tests only | Unit + integration tests |
| **Failure isolation** | Better (processes separate) | Worse (single process) |
| **Codebase complexity** | Process management + LSP client | Roslyn API learning curve |

**Verdict:** Direct Roslyn wins on nearly every dimension except failure isolation (which is minor for MCP use case).

## Revised Architecture

### Before (Subprocess approach)
```
Claude Code
    ↓ MCP (stdio)
MCP Server
    ↓ LSP (stdio)
csharp-ls process
    ↓
Roslyn APIs
```

### After (Direct Roslyn)
```
Claude Code
    ↓ MCP (stdio)
MCP Server
    ↓
Roslyn APIs (in-process)
```

## Implementation Changes

### Phase 1 Changes

Instead of implementing LSP client, we'll use Roslyn directly:

**Replace:**
- `LspClient` class
- LSP message handling
- Process management

**With:**
```csharp
// Core Roslyn components
public class RoslynWorkspace
{
    private AdhocWorkspace _workspace;
    private ProjectId _projectId;
    private Compilation _compilation;

    public async Task OpenProjectAsync(string projectPath)
    {
        _workspace = new AdhocWorkspace();

        // Load .csproj
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            "Project",
            "Project",
            LanguageNames.CSharp
        );

        _projectId = _workspace.AddProject(projectInfo).Id;

        // Add all .cs files
        foreach (var file in Directory.EnumerateFiles(projectPath, "*.cs", SearchOption.AllDirectories))
        {
            var documentInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(_projectId),
                Path.GetFileName(file),
                loader: TextLoader.From(TextAndVersion.Create(
                    SourceText.From(File.ReadAllText(file)),
                    VersionStamp.Default
                )),
                filePath: file
            );

            _workspace.AddDocument(documentInfo);
        }

        // Get compilation
        var project = _workspace.CurrentSolution.GetProject(_projectId);
        _compilation = await project.GetCompilationAsync();
    }

    public IEnumerable<INamedTypeSymbol> FindClasses(string name)
    {
        return _compilation.GetSymbolsWithName(
            name,
            SymbolFilter.Type
        ).OfType<INamedTypeSymbol>()
         .Where(t => t.TypeKind == TypeKind.Class);
    }

    public async Task<ClassStructure> GetClassStructureAsync(INamedTypeSymbol classSymbol)
    {
        var properties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(p => new PropertyInfo
            {
                Name = p.Name,
                Type = p.Type.ToDisplayString(),
                Accessibility = p.DeclaredAccessibility.ToString()
            });

        var methods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == MethodKind.Ordinary)
            .Select(m => new MethodInfo
            {
                Name = m.Name,
                ReturnType = m.ReturnType.ToDisplayString(),
                Parameters = m.Parameters.Select(p =>
                    new ParameterInfo { Name = p.Name, Type = p.Type.ToDisplayString() }
                )
            });

        return new ClassStructure
        {
            Name = classSymbol.Name,
            Namespace = classSymbol.ContainingNamespace.ToDisplayString(),
            Properties = properties.ToList(),
            Methods = methods.ToList()
        };
    }

    public async Task<Location> FindInsertionPointAsync(INamedTypeSymbol classSymbol)
    {
        var syntaxRef = classSymbol.DeclaringSyntaxReferences.First();
        var classDecl = await syntaxRef.GetSyntaxAsync() as ClassDeclarationSyntax;

        // Find last property or first method
        var lastProperty = classDecl.Members.OfType<PropertyDeclarationSyntax>().LastOrDefault();
        var firstMethod = classDecl.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault();

        if (lastProperty != null)
        {
            // Insert after last property
            return lastProperty.GetLocation();
        }
        else if (firstMethod != null)
        {
            // Insert before first method
            return firstMethod.GetLocation();
        }
        else
        {
            // Insert at start of class body
            return classDecl.OpenBraceToken.GetLocation();
        }
    }
}
```

### Key Roslyn APIs We'll Use

**Workspace APIs:**
- `AdhocWorkspace` — In-memory workspace
- `Project` / `Document` — Project structure
- `Compilation` — Compiled view of project

**Symbol APIs:**
- `INamedTypeSymbol` — Classes, interfaces, structs
- `IPropertySymbol` — Properties
- `IMethodSymbol` — Methods
- `GetSymbolsWithName()` — Find symbols by name

**Syntax APIs:**
- `SyntaxTree` — Parse tree
- `ClassDeclarationSyntax` — Class syntax node
- `SyntaxRewriter` — Transform syntax trees
- `SyntaxFactory` — Generate new syntax

**Semantic APIs:**
- `SemanticModel` — Type information
- `GetTypeInfo()` — Get type of expression
- `GetSymbolInfo()` — Get symbol at location
- `FindReferences()` — Find all references

## Dependencies

### NuGet Packages
```xml
<ItemGroup>
  <!-- Core Roslyn packages -->
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" />
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.8.0" />

  <!-- MCP/JSON-RPC -->
  <PackageReference Include="System.Text.Json" Version="8.0.0" />

  <!-- File operations -->
  <PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="8.0.0" />

  <!-- Logging -->
  <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
</ItemGroup>
```

**No external tools required.** Everything is in-process .NET libraries.

## Performance Implications

### Subprocess approach (estimated):
```
find_symbol("ExampleClass"):
  1. Serialize MCP request → JSON (1ms)
  2. Write to csharp-ls stdin (1ms)
  3. csharp-ls parse request (1ms)
  4. csharp-ls query Roslyn (10ms)
  5. csharp-ls serialize response (1ms)
  6. Read from stdout (1ms)
  7. MCP parse response (1ms)
Total: ~16ms per query
```

### Direct Roslyn approach (estimated):
```
find_symbol("ExampleClass"):
  1. Call RoslynWorkspace.FindClasses("ExampleClass") (10ms)
Total: ~10ms per query
```

**40% faster** with direct approach, and scales better under load.

### Memory Usage

**Subprocess:**
- MCP server process: ~50MB
- csharp-ls process: ~100MB
- Total: ~150MB

**Direct Roslyn:**
- Single process: ~80MB
- Total: ~80MB

**47% less memory** with direct approach.

## Migration Plan

Since we haven't implemented Phase 1 yet, we can adopt Direct Roslyn from the start. No migration needed.

### Updated Phase 1 Tasks

**Remove:**
- ❌ csharp-ls binary download/bundling
- ❌ LSP client implementation
- ❌ Process management
- ❌ LSP message handling

**Add:**
- ✅ Roslyn workspace initialization
- ✅ Symbol search using `GetSymbolsWithName()`
- ✅ Semantic model queries
- ✅ Syntax tree manipulation for edits
- ✅ Reference finding using Roslyn APIs

**Time savings:** -8 to -12 hours (no LSP client needed)

## Risks & Mitigation

### Risk: Roslyn learning curve
**Mitigation:**
- Roslyn has excellent documentation
- Many StackOverflow examples
- We only need subset of APIs (symbols, syntax)
- Start with simple queries, iterate

### Risk: Performance on large projects
**Mitigation:**
- Roslyn handles large projects well (used by VS)
- Implement incremental compilation (update only changed files)
- Cache `Compilation` and `SemanticModel` instances
- Add project load progress reporting

### Risk: Missing LSP protocol features
**Mitigation:**
- We don't need full LSP - just semantic queries
- If we need more later, can add Roslyn APIs incrementally
- LSP designed for editors, not for MCP use case

## Conclusion

**Direct Roslyn integration is the clear winner:**
- ✅ Simpler architecture (single process)
- ✅ Better performance (no IPC)
- ✅ Easier deployment (one binary)
- ✅ Easier testing (unit testable)
- ✅ More flexible (full Roslyn API access)
- ✅ Less code (no LSP client needed)

**Update MVP plan:** Replace Phase 1 LSP client with direct Roslyn workspace implementation.

## References

- [Roslyn API Documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
- [Roslyn Workspace APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.workspace)
- [Roslyn Symbol APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.isymbol)
- [Roslyn Syntax APIs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.syntaxnode)

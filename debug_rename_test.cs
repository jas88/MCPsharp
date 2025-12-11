using MCPsharp.Services.Roslyn;
using Microsoft.Extensions.Logging;
using NSubstitute;

var workspace = new RoslynWorkspace();
await workspace.InitializeTestWorkspaceAsync();

var code = @"
public class TestClass
{
    public void TestMethod()
    {
        int oldVariable = 42;
        var result = oldVariable + 10;
        Console.WriteLine(oldVariable);
    }
}";

await workspace.AddInMemoryDocumentAsync("test.cs", code);

// Check if compilation is available
var compilation = workspace.GetCompilation();
Console.WriteLine($"Compilation is null: {compilation == null}");

if (compilation != null)
{
    var diagnostics = compilation.GetDiagnostics();
    Console.WriteLine($"Diagnostics count: {diagnostics.Length}");
    foreach (var diag in diagnostics)
    {
        Console.WriteLine($"  {diag.Severity}: {diag.GetMessage()}");
    }

    // Try to find the symbol
    var symbols = compilation.GetSymbolsWithName(
        n => string.Equals(n, "oldVariable", StringComparison.OrdinalIgnoreCase),
        Microsoft.CodeAnalysis.SymbolFilter.Member).ToList();
    Console.WriteLine($"Found {symbols.Count} symbols named 'oldVariable'");
    foreach (var sym in symbols)
    {
        Console.WriteLine($"  Symbol: {sym.Name}, Kind: {sym.Kind}, ContainingSymbol: {sym.ContainingSymbol?.Name}");
    }
}

var referenceFinder = new AdvancedReferenceFinderService(
    workspace,
    new SymbolQueryService(workspace),
    Substitute.For<ICallerAnalysisService>(),
    Substitute.For<ICallChainService>(),
    Substitute.For<ITypeUsageService>());

var symbolQuery = new SymbolQueryService(workspace);
var logger = Substitute.For<ILogger<RenameSymbolService>>();

var service = new RenameSymbolService(
    workspace,
    referenceFinder,
    symbolQuery,
    logger);

var request = new RenameRequest
{
    OldName = "oldVariable",
    NewName = "newVariable",
    SymbolKind = SymbolKind.Local
};

var result = await service.RenameSymbolAsync(request);

Console.WriteLine($"Result.Success: {result.Success}");
Console.WriteLine($"Result.RenamedCount: {result.RenamedCount}");
Console.WriteLine($"Result.Errors: {string.Join(", ", result.Errors)}");
Console.WriteLine($"Result.Conflicts: {result.Conflicts.Count}");

using MCPsharp.Services.Roslyn;
using MCPsharp.Services.Roslyn.SymbolSearch;

var workspace = new RoslynWorkspace();
await workspace.InitializeTestWorkspaceAsync();

var code = @"
public class TestClass
{
    public string OldProperty { get; set; }

    public void Test()
    {
        OldProperty = ""test"";
        var value = OldProperty;
    }
}";

await workspace.AddInMemoryDocumentAsync("test.cs", code);

var finder = new SymbolFinderService(workspace, null);
var request = new SymbolSearchRequest
{
    Name = "OldProperty",
    SymbolKind = SymbolKind.Property
};

var result = await finder.FindSymbolAsync(request);

Console.WriteLine($"Found: {result.Symbol != null}");
if (result.Symbol != null)
{
    Console.WriteLine($"Symbol: {result.Symbol.Name}, Kind: {result.Symbol.Kind}");
    Console.WriteLine($"Type: {result.Symbol.GetType().Name}");
    Console.WriteLine($"Strategy: {result.UsedStrategy}");
}
else
{
    Console.WriteLine($"Candidates: {result.Candidates.Count}");
}

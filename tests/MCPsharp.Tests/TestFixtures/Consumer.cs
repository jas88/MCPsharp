namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test consumer class for reference finding tests
/// </summary>
public class Consumer
{
    private readonly IService _service;
    private string _name;

    public Consumer(IService service)
    {
        _service = service;
        _name = "Consumer";
    }

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public void Run()
    {
        _service.Execute(); // Method invocation reference
        var data = _service.GetData(); // Another method invocation
        System.Console.WriteLine(data);
    }

    public void UpdateName(string newName)
    {
        _name = newName; // Property write reference
    }

    public string ReadName()
    {
        return _name; // Property read reference
    }
}

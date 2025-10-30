namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Another implementation for testing multiple implementations
/// </summary>
public class DerivedService : IService
{
    public void Execute()
    {
        System.Console.WriteLine("Derived executing");
    }

    public string GetData()
    {
        return "derived";
    }
}

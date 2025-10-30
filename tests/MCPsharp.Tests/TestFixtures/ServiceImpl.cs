namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Test implementation for reference finding tests
/// </summary>
public class ServiceImpl : IService
{
    private string _data = "test";

    public void Execute()
    {
        System.Console.WriteLine("Executing");
    }

    public string GetData()
    {
        return _data;
    }
}

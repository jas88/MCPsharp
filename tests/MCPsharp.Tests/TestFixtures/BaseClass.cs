namespace MCPsharp.Tests.TestFixtures;

/// <summary>
/// Base class for testing derived types
/// </summary>
public abstract class BaseClass
{
    public abstract void DoWork();

    public virtual string GetName()
    {
        return "Base";
    }
}

/// <summary>
/// Derived class for testing
/// </summary>
public class DerivedClass : BaseClass
{
    public override void DoWork()
    {
        System.Console.WriteLine("Working");
    }

    public override string GetName()
    {
        return "Derived";
    }
}

/// <summary>
/// Another derived class
/// </summary>
public class AnotherDerivedClass : BaseClass
{
    public override void DoWork()
    {
        System.Console.WriteLine("Working differently");
    }
}

using MCPsharp.Models.Refactoring;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class ExtractMethodServiceTests
{
    #region Method Characteristics Tests

    [Test]
    public async Task ExtractMethod_MultipleStatements_DetectsCorrectly()
    {
        var code = @"
using System;

class Test
{
    public void Method()
    {
        var a = 1;
        var b = 2;
        var c = 3;
        var sum = a + b + c;
        Console.WriteLine(sum);
    }
}";

        var filePath = await CreateTestFileAsync("MultipleStatements.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 8,
            EndLine = 12,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction, Is.Not.Null);
    }

    [Test]
    public async Task ExtractMethod_ComplexExpression_HandlesCorrectly()
    {
        var code = @"
using System;

class Calculator
{
    public void Calculate()
    {
        int x = 10;
        int y = 20;
        int z = 30;
        int result = (x + y) * z / 2;
        Console.WriteLine(result);
    }
}";

        var filePath = await CreateTestFileAsync("ComplexExpr.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 11,
            EndLine = 11,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
    }

    #endregion

    #region Accessibility Tests

    [Test]
    public async Task ExtractMethod_PublicAccessibility_GeneratesCorrectly()
    {
        var code = @"
class Test
{
    public void Method()
    {
        var x = 1;
        var y = 2;
    }
}";

        var filePath = await CreateTestFileAsync("PublicMethod.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 6,
            EndLine = 7,
            Accessibility = "public",
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Method.Signature, Does.Contain("public"));
    }

    [Test]
    public async Task ExtractMethod_DefaultPrivate_GeneratesCorrectly()
    {
        var code = @"
class Test
{
    public void Method()
    {
        var x = 1;
    }
}";

        var filePath = await CreateTestFileAsync("PrivateMethod.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 6,
            EndLine = 6,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Method.Signature, Does.Contain("private"));
    }

    #endregion

    #region Custom Method Name Tests

    [Test]
    public async Task ExtractMethod_CustomName_UsesProvidedName()
    {
        var code = @"
class Test
{
    public void Method()
    {
        var x = 1;
    }
}";

        var filePath = await CreateTestFileAsync("CustomName.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 6,
            EndLine = 6,
            MethodName = "MyCustomMethod",
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Method.Name, Is.EqualTo("MyCustomMethod"));
    }

    [Test]
    public async Task ExtractMethod_NoName_GeneratesDefault()
    {
        var code = @"
class Test
{
    public void Method()
    {
        var x = 1;
    }
}";

        var filePath = await CreateTestFileAsync("DefaultName.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 6,
            EndLine = 6,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Method.Name, Is.Not.Empty);
    }

    #endregion
}

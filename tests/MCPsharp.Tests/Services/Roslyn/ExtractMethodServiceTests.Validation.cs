using MCPsharp.Models.Refactoring;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class ExtractMethodServiceTests
{
    #region Error Cases

    [Test]
    public async Task ExtractMethod_InvalidLineRange_ReturnsError()
    {
        var code = @"
class Test
{
    public void Method()
    {
        var x = 1;
    }
}";

        var filePath = await CreateTestFileAsync("InvalidRange.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 10,
            EndLine = 5, // End before start
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public async Task ExtractMethod_NoStatements_ReturnsError()
    {
        var code = @"
class Test
{
    public void Method()
    {
        // Just comments
    }
}";

        var filePath = await CreateTestFileAsync("NoStatements.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 6,
            EndLine = 6,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task ExtractMethod_WithGoto_ReturnsError()
    {
        var code = @"
class Test
{
    public void Method()
    {
        int x = 0;
        retry:
        x++;
        if (x < 5)
            goto retry;
    }
}";

        var filePath = await CreateTestFileAsync("WithGoto.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 7,
            EndLine = 10,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error?.Message ?? "", Does.Contain("goto").IgnoreCase);
    }

    [Test]
    public async Task ExtractMethod_NotInMethod_ReturnsError()
    {
        var code = @"
class Test
{
    private int _field = 10;
}";

        var filePath = await CreateTestFileAsync("NotInMethod.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 4,
            EndLine = 4,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.False);
    }

    #endregion

    #region Preview Tests

    [Test]
    public async Task ExtractMethod_PreviewMode_ReturnsPreview()
    {
        var code = @"
class Test
{
    public void Method()
    {
        var x = 1;
        var y = 2;
        var sum = x + y;
    }
}";

        var filePath = await CreateTestFileAsync("Preview.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 6,
            EndLine = 8,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Preview, Is.Not.Null);
        Assert.That(result.Preview.OriginalCode, Is.Not.Empty);
        Assert.That(result.Preview.ModifiedCode, Is.Not.Empty);
    }

    #endregion
}

using MCPsharp.Models.Refactoring;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Roslyn;

public partial class ExtractMethodServiceTests
{
    #region Data Flow Tests

    [Test]
    public async Task ExtractMethod_ReadOnlyVariable_PassesAsParameter()
    {
        var code = @"
using System;

class Test
{
    public void Method()
    {
        int value = 10;
        Console.WriteLine(value);
    }
}";

        var filePath = await CreateTestFileAsync("ReadOnly.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 9,
            EndLine = 9,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Parameters, Does.Contain(result.Extraction.Parameters.FirstOrDefault(p => p.Name == "value")));
        Assert.That(result.Extraction.Parameters.First(p => p.Name == "value").Modifier, Is.Null);
    }

    [Test]
    public async Task ExtractMethod_ModifiedVariable_UsesRef()
    {
        var code = @"
class Test
{
    public void Method()
    {
        int counter = 0;
        counter++;
        counter += 5;
        var x = counter;
    }
}";

        var filePath = await CreateTestFileAsync("Modified.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 7,
            EndLine = 8,
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Parameters.Single(p => p.Name == "counter").Modifier, Is.EqualTo("ref"));
        Assert.That(result.Extraction.CallSite.Code, Is.EqualTo("ExtractedMethod(ref counter);"));
    }

    #endregion

    #region Integration Tests

    [Test]
    public async Task ExtractMethod_RealWorldScenario_Success()
    {
        var code = @"
using System;
using System.Collections.Generic;
using System.Linq;

class OrderProcessor
{
    public void ProcessOrder(Order order)
    {
        // Validate order
        if (order == null)
            throw new ArgumentNullException(nameof(order));

        if (order.Items == null || !order.Items.Any())
            throw new InvalidOperationException(""Order has no items"");

        // Calculate totals
        decimal subtotal = 0;
        foreach (var item in order.Items)
        {
            subtotal += item.Price * item.Quantity;
        }

        decimal tax = subtotal * 0.08m;
        decimal total = subtotal + tax;

        order.Subtotal = subtotal;
        order.Tax = tax;
        order.Total = total;

        // Log the order
        Console.WriteLine($""Order processed: {order.Id}"");
    }

    public class Order
    {
        public string Id { get; set; }
        public List<OrderItem> Items { get; set; }
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
    }

    public class OrderItem
    {
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }
}";

        var filePath = await CreateTestFileAsync("OrderProcessor.cs", code);

        var request = new ExtractMethodRequest
        {
            FilePath = filePath,
            StartLine = 18,
            EndLine = 29,
            MethodName = "CalculateOrderTotals",
            Preview = true
        };

        var result = await _service.ExtractMethodAsync(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Extraction!.Method.Name, Is.EqualTo("CalculateOrderTotals"));
        Assert.That(result.Preview, Is.Not.Null);
    }

    #endregion
}

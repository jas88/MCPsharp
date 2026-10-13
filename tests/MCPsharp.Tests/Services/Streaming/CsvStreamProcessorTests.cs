using System.Text;
using MCPsharp.Models.Streaming;
using MCPsharp.Services.Streaming;
using NUnit.Framework;

namespace MCPsharp.Tests.Services.Streaming;

[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class CsvStreamProcessorTests
{
    private static async Task<string> ProcessAsync(string text, Dictionary<string, object>? options = null)
    {
        var processor = new CsvStreamProcessor();
        var chunk = new StreamChunk { Data = Encoding.UTF8.GetBytes(text), IsLastChunk = true };

        var result = await processor.ProcessChunkAsync(chunk, options ?? new Dictionary<string, object>());

        return Encoding.UTF8.GetString(result.Data);
    }

    [Test]
    public async Task ProcessChunkAsync_FieldsWithDelimiterOrQuotes_AreRequoted()
    {
        var output = await ProcessAsync("name,\"Leeds, UK\",\"say \"\"hi\"\"\",plain\n");

        Assert.That(output, Is.EqualTo("name,\"Leeds, UK\",\"say \"\"hi\"\"\",plain" + Environment.NewLine));
    }

    [Test]
    public async Task ProcessChunkAsync_QuotedFieldWithoutSpecialCharacters_IsWrittenUnquoted()
    {
        var output = await ProcessAsync("\"a\",\"b c\"");

        Assert.That(output, Is.EqualTo("a,b c"));
    }

    [Test]
    public async Task ProcessChunkAsync_CustomDelimiter_QuotesOnlyFieldsContainingThatDelimiter()
    {
        var options = new Dictionary<string, object> { ["Delimiter"] = ";" };

        var output = await ProcessAsync("a,b;\"c;d\"", options);

        Assert.That(output, Is.EqualTo("a,b;\"c;d\""));
    }

    [Test]
    public async Task ProcessChunkAsync_SelectedColumns_KeepQuotingValid()
    {
        var options = new Dictionary<string, object> { ["SelectedColumnIndexes"] = new List<object> { 1, 0 } };

        var output = await ProcessAsync("x,\"1,000\"", options);

        Assert.That(output, Is.EqualTo("\"1,000\",x"));
    }

    [TestCase("plain", "plain")]
    [TestCase("a,b", "\"a,b\"")]
    [TestCase("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [TestCase("two\nlines", "\"two\nlines\"")]
    [TestCase("cr\rhere", "\"cr\rhere\"")]
    public void QuoteField_QuotesPerRfc4180(string field, string expected)
    {
        Assert.That(CsvStreamProcessor.QuoteField(field, new CsvProcessorConfig()), Is.EqualTo(expected));
    }
}

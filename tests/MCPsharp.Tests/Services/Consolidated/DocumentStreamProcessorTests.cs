using MCPsharp.Models.Consolidated;
using MCPsharp.Services.Consolidated;
using NUnit.Framework;
using static MCPsharp.Tests.Services.Consolidated.ConsolidatedStreamProcessorTestHelper;

namespace MCPsharp.Tests.Services.Consolidated;

/// <summary>
/// Consolidated json/xml/log processors
/// </summary>
[TestFixture]
[Category("Unit")]
[Category("Streaming")]
public class DocumentStreamProcessorTests
{
    [Test]
    public async Task Json_Document_IsValidatedAndReformatted()
    {
        var (result, output) = await RunTextAsync(new JsonStreamProcessor(),
            "[ {\"a\": 1}, // comment\n {\"b\": [true, null]}, ]",
            new Dictionary<string, object> { ["Indented"] = false });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo("[{\"a\":1},{\"b\":[true,null]}]"));
        Assert.That(result.RecordsProcessed, Is.EqualTo(2));
    }

    [Test]
    public async Task Json_Document_IsIndentedByDefault()
    {
        var (result, output) = await RunTextAsync(new JsonStreamProcessor(), "{\"a\":{\"b\":1}}");

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output.ReplaceLineEndings("\n"), Is.EqualTo("{\n  \"a\": {\n    \"b\": 1\n  }\n}"));
    }

    [Test]
    public async Task Json_InvalidDocument_ReportsFailure()
    {
        var (result, _) = await RunTextAsync(new JsonStreamProcessor(), "{\"a\": }");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.StartWith("Invalid JSON"));
    }

    [Test]
    public async Task JsonLines_SkipErrors_DropsInvalidLinesWithWarnings()
    {
        var (result, output) = await RunTextAsync(new JsonStreamProcessor(),
            "{ \"a\": 1 }\nnot json\n\n[1, 2]\n",
            options: new ProcessorOptions
            {
                SkipErrors = true,
                Parameters = new Dictionary<string, object> { ["JsonLines"] = true }
            });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo("{\"a\":1}\n[1,2]\n"));
        Assert.That(result.RecordsProcessed, Is.EqualTo(2));
        Assert.That(result.Warnings, Has.Count.EqualTo(1));
        Assert.That(result.Warnings![0].LineNumber, Is.EqualTo(2));
    }

    [Test]
    public async Task JsonLines_InvalidLineWithoutSkipErrors_ReportsFailure()
    {
        var (result, _) = await RunTextAsync(new JsonStreamProcessor(), "{}\n{oops}\n",
            new Dictionary<string, object> { ["JsonLines"] = true });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("line 2"));
    }

    [Test]
    public async Task Xml_Document_IsRewrittenAndElementsCounted()
    {
        var (result, output) = await RunTextAsync(new XmlStreamProcessor(),
            "<root>\n  <a x=\"1\"/>\n  <b>t &amp; u<!-- c --></b>\n</root>",
            new Dictionary<string, object> { ["Indent"] = false });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Does.EndWith("<root>\n  <a x=\"1\" />\n  <b>t &amp; u<!-- c --></b>\n</root>"));
        Assert.That(result.RecordsProcessed, Is.EqualTo(3));
    }

    [Test]
    public async Task Xml_Indent_ReformatsDocument()
    {
        var (result, output) = await RunTextAsync(new XmlStreamProcessor(), "<root><a><b/></a></root>");

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output.ReplaceLineEndings("\n"), Does.EndWith("<root>\n  <a>\n    <b />\n  </a>\n</root>"));
    }

    [TestCase("<root><a></root>")]
    [TestCase("<!DOCTYPE root [<!ENTITY e \"x\">]><root>&e;</root>")]
    public async Task Xml_MalformedOrDtd_ReportsFailure(string xml)
    {
        var (result, _) = await RunTextAsync(new XmlStreamProcessor(), xml);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.StartWith("Invalid XML"));
    }

    private const string SampleLog =
        "starting up\n" +
        "2024-01-01 10:00:00 INFO Service started\n" +
        "2024-01-01 10:00:01 DEBUG cache warm\n" +
        "2024-01-01 10:00:02 WARN slow request /api\n" +
        "2024-01-01 10:00:03 ERROR request failed\n" +
        "   at Foo.Bar()\n" +
        "   at Foo.Baz()\n" +
        "[10:00:04 INF] info via serilog\n" +
        "[10:00:05 ERR] error via serilog\n";

    [Test]
    public async Task Log_MinLevel_KeepsEntriesAndTheirContinuationLines()
    {
        var (result, output) = await RunTextAsync(new LogStreamProcessor(), SampleLog,
            new Dictionary<string, object> { ["MinLevel"] = "warn" });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(
            "2024-01-01 10:00:02 WARN slow request /api\n" +
            "2024-01-01 10:00:03 ERROR request failed\n" +
            "   at Foo.Bar()\n" +
            "   at Foo.Baz()\n" +
            "[10:00:05 ERR] error via serilog\n"));
        Assert.That(result.RecordsProcessed, Is.EqualTo(9));
        Assert.That(result.Metrics!["Level.error"], Is.EqualTo(2L));
    }

    [Test]
    public async Task Log_LevelsAndPattern_AreCombined()
    {
        var (result, output) = await RunTextAsync(new LogStreamProcessor(), SampleLog,
            new Dictionary<string, object>
            {
                ["Levels"] = new List<object> { "information", "error" },
                ["Pattern"] = "SERILOG",
                ["IgnoreCase"] = true
            });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo("[10:00:04 INF] info via serilog\n[10:00:05 ERR] error via serilog\n"));
    }

    [Test]
    public async Task Log_NoFilters_CopiesEveryLine()
    {
        var (result, output) = await RunTextAsync(new LogStreamProcessor(), SampleLog);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(output, Is.EqualTo(SampleLog));
    }

    [Test]
    public async Task Log_UnknownLevel_ReportsFailure()
    {
        var (result, _) = await RunTextAsync(new LogStreamProcessor(), SampleLog,
            new Dictionary<string, object> { ["MinLevel"] = "loud" });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("Unknown log level 'loud'"));
    }
}

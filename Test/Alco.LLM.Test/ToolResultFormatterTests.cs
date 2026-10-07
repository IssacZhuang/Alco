using Alco.AgentControlProtocol;
using System.Text.Json;
using NUnit.Framework;

namespace Alco.LLM.Test;

[TestFixture]
public class ToolResultFormatterTests
{
    private static ToolResultFormatter Formatter { get; } = new();

    [Test]
    public void Format_ToolOk_ReturnsMessageVerbatim()
    {
        var result = new ToolOk("Placed 'Wall' at (10,20).");

        Assert.That(Formatter.Format(result), Is.EqualTo("Placed 'Wall' at (10,20)."));
    }

    [Test]
    public void Format_ToolList_TruncatedWithHint_SerializesAllFields()
    {
        var result = new ToolList(["Tree_Oak:45", "Tree_Pine:32"], 15, true, "Use offset:10 for more");

        using var doc = JsonDocument.Parse(Formatter.Format(result));

        var root = doc.RootElement;
        Assert.That(root.GetProperty("items").EnumerateArray().First().GetString(), Is.EqualTo("Tree_Oak:45"));
        Assert.That(root.GetProperty("total").GetInt32(), Is.EqualTo(15));
        Assert.That(root.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("hint").GetString(), Is.EqualTo("Use offset:10 for more"));
    }

    [Test]
    public void Format_ToolList_AllFit_OmitsHint()
    {
        var result = new ToolList(["A", "B"], 2, false);

        using var doc = JsonDocument.Parse(Formatter.Format(result));

        Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
        Assert.That(doc.RootElement.TryGetProperty("hint", out _), Is.False);
    }

    [Test]
    public void Format_ToolList_Empty_HasEmptyItemsAndZeroTotal()
    {
        var result = new ToolList([], 0, false);

        using var doc = JsonDocument.Parse(Formatter.Format(result));

        Assert.That(doc.RootElement.GetProperty("items").GetArrayLength(), Is.EqualTo(0));
        Assert.That(doc.RootElement.GetProperty("total").GetInt32(), Is.EqualTo(0));
        Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
    }

    [Test]
    public void Format_ToolData_SerializesInnerObjectAsCamelCaseJson()
    {
        var result = new ToolData(new SampleData { X = 10, Y = 20, TerrainId = "Grass" });

        using var doc = JsonDocument.Parse(Formatter.Format(result));

        var root = doc.RootElement;
        Assert.That(root.GetProperty("x").GetInt32(), Is.EqualTo(10));
        Assert.That(root.GetProperty("y").GetInt32(), Is.EqualTo(20));
        Assert.That(root.GetProperty("terrainId").GetString(), Is.EqualTo("Grass"));
    }

    [Test]
    public void Format_ToolError_SerializesErrorAndCode()
    {
        var result = new ToolError("No game loaded", "NO_GAME");

        using var doc = JsonDocument.Parse(Formatter.Format(result));

        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("No game loaded"));
        Assert.That(doc.RootElement.GetProperty("code").GetString(), Is.EqualTo("NO_GAME"));
    }

    [Test]
    public void Format_ToolList_ExceedsCap_ReturnsValidTruncatedJson()
    {
        var items = Enumerable.Range(0, 300)
            .Select(i => $"Item_{i}:{new string('x', 100)}")
            .ToArray();
        var result = new ToolList(items, items.Length, false);

        string formatted = Formatter.Format(result);

        Assert.That(formatted.Length, Is.LessThanOrEqualTo(ToolResultFormatter.DefaultMaxFormattedLength));
        using var doc = JsonDocument.Parse(formatted);
        Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(doc.RootElement.GetProperty("items").GetArrayLength(), Is.LessThan(items.Length));
    }

    [Test]
    public void Format_ToolData_ExceedsCap_ReturnsValidTruncatedJson()
    {
        var result = new ToolData(new { text = new string('x', ToolResultFormatter.DefaultMaxFormattedLength + 100) });

        string formatted = Formatter.Format(result);

        Assert.That(formatted.Length, Is.LessThanOrEqualTo(ToolResultFormatter.DefaultMaxFormattedLength));
        using var doc = JsonDocument.Parse(formatted);
        Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(doc.RootElement.GetProperty("originalLength").GetInt32(), Is.GreaterThan(ToolResultFormatter.DefaultMaxFormattedLength));
    }

    [Test]
    public void Format_ToolError_ExceedsCap_ReturnsValidTruncatedJson()
    {
        var result = new ToolError(new string('x', ToolResultFormatter.DefaultMaxFormattedLength + 100), "HUGE_ERROR");

        string formatted = Formatter.Format(result);

        using var doc = JsonDocument.Parse(formatted);
        Assert.That(doc.RootElement.GetProperty("code").GetString(), Is.EqualTo("HUGE_ERROR"));
        Assert.That(doc.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Does.EndWith("...[truncated]"));
    }

    [Test]
    public void Format_ExceedsCap_TruncatesWithMarker()
    {
        string huge = new('x', ToolResultFormatter.DefaultMaxFormattedLength + 100);
        var result = new ToolOk(huge);

        string formatted = Formatter.Format(result);

        Assert.That(formatted, Does.EndWith("...[truncated]"));
        Assert.That(formatted.Length, Is.EqualTo(ToolResultFormatter.DefaultMaxFormattedLength));
    }

    [Test]
    public void Format_AtCapBoundary_NotTruncated()
    {
        string exact = new('x', ToolResultFormatter.DefaultMaxFormattedLength);
        var result = new ToolOk(exact);

        Assert.That(Formatter.Format(result), Is.EqualTo(exact));
    }

    private sealed class SampleData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string TerrainId { get; set; } = string.Empty;
    }
}

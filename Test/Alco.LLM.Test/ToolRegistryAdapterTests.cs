using Alco.AgentControlProtocol;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Alco.LLM.Test;

/// <summary>Tests the tool registry adapter's conversion to AI tools.</summary>
[TestFixture]
public class ToolRegistryAdapterTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>Tests that ToAITools returns one tool per registered function.</summary>
    [Test]
    public void ToAITools_ReturnsCorrectCount()
    {
        var registry = new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
        var tools = registry.ToAITools();

        Assert.That(tools.Count, Is.EqualTo(3)); // Add, Echo, ThrowError
    }

    /// <summary>Tests that each converted tool carries its name and description.</summary>
    [Test]
    public void ToAITools_EachToolHasCorrectMetadata()
    {
        var registry = new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
        var tools = registry.ToAITools();

        var addTool = tools.FirstOrDefault(t => t.Name == "Add") as AIFunction;
        var echoTool = tools.FirstOrDefault(t => t.Name == "Echo") as AIFunction;

        Assert.That(addTool, Is.Not.Null);
        Assert.That(addTool!.Description, Is.EqualTo("Adds two numbers"));
        Assert.That(echoTool, Is.Not.Null);
        Assert.That(echoTool!.Description, Is.EqualTo("Echoes the message back"));
    }

    /// <summary>Tests that each converted tool has a valid JSON schema.</summary>
    [Test]
    public void ToAITools_SchemaIsValidJson()
    {
        var registry = new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
        var tools = registry.ToAITools();

        foreach (var tool in tools)
        {
            var function = tool as AIFunction;
            Assert.That(function, Is.Not.Null, $"Tool {tool.Name} is not an AIFunction");
            var schema = function!.JsonSchema;
            Assert.That(schema.ValueKind != JsonValueKind.Undefined, Is.True, $"Tool {tool.Name} missing schema");

            var schemaJson = schema.GetRawText();
            Assert.DoesNotThrow(() => JsonDocument.Parse(schemaJson), $"Tool {tool.Name} has invalid JSON schema");
        }
    }
}

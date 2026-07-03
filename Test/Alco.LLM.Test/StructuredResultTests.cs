using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Alco.LLM.Test;

[TestFixture]
public class StructuredResultTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static ToolRegistry CreateRegistry()
    {
        return new ToolRegistry([typeof(FakeStructuredToolFunctions)], null, JsonOptions);
    }

    private static JsonSerializerOptions CreateConverterOptions()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new ConverterBackedDataConverter());
        return options;
    }

    private static ChatResponse CreateTextResponse(string text)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    private static ChatResponse CreateToolCallResponse(string callId, string name, IDictionary<string, object?> args)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, new AIContent[] { new FunctionCallContent(callId, name, args) })]);
    }

    private static FunctionResultContent GetFunctionResult(IReadOnlyList<ChatMessage> messages)
    {
        return messages.SelectMany(m => m.Contents.OfType<FunctionResultContent>()).Single();
    }

    [Test]
    public async Task ToolReturning_ToolOk_FormattedIntoHistoryButRawInEvent()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse("c1", "ConfirmThing", new Dictionary<string, object?> { ["name"] = "Widget" }));
        client.SetupResponse(CreateTextResponse("done"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var events = new List<LLMSessionEvent>();
        await foreach (var ev in session.ChatEventsAsync("confirm"))
        {
            events.Add(ev);
        }

        // Event preserves the raw AgentToolResult.
        var completed = events.OfType<ToolCallCompletedEvent>().Single();
        Assert.That(completed.Result, Is.TypeOf<ToolOk>());
        Assert.That(((ToolOk)completed.Result!).Message, Is.EqualTo("Confirmed 'Widget'."));

        // History received the formatted (plain text) value.
        var historyResult = GetFunctionResult(client.ReceivedMessagesHistory[1]);
        Assert.That(historyResult.Result, Is.EqualTo("Confirmed 'Widget'."));
    }

    [Test]
    public async Task ToolReturning_ToolData_FormattedAsJsonIntoHistoryButRawInEvent()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse("c1", "GetData", new Dictionary<string, object?> { ["id"] = 7 }));
        client.SetupResponse(CreateTextResponse("done"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var events = new List<LLMSessionEvent>();
        await foreach (var ev in session.ChatEventsAsync("get"))
        {
            events.Add(ev);
        }

        // Event preserves the raw AgentToolResult.
        var completed = events.OfType<ToolCallCompletedEvent>().Single();
        Assert.That(completed.Result, Is.TypeOf<ToolData>());

        // History received the formatted JSON string.
        var historyResult = GetFunctionResult(client.ReceivedMessagesHistory[1]);
        Assert.That(historyResult.Result, Is.TypeOf<string>());
        using var doc = JsonDocument.Parse((string)historyResult.Result!);
        Assert.That(doc.RootElement.GetProperty("id").GetInt32(), Is.EqualTo(7));
        Assert.That(doc.RootElement.GetProperty("name").GetString(), Is.EqualTo("Sample"));
    }

    [Test]
    public async Task ToolReturning_ToolData_UsesSessionJsonConverters()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse("c1", "GetConvertedData", new Dictionary<string, object?> { ["value"] = 9 }));
        client.SetupResponse(CreateTextResponse("done"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            JsonOptions = CreateConverterOptions(),
        });

        await session.ChatAsync("get converted");

        var historyResult = GetFunctionResult(client.ReceivedMessagesHistory[1]);
        Assert.That(historyResult.Result, Is.TypeOf<string>());
        using var doc = JsonDocument.Parse((string)historyResult.Result!);
        Assert.That(doc.RootElement.GetProperty("converted").GetInt32(), Is.EqualTo(9));
    }

    [Test]
    public async Task ToolReturning_ToolError_FormatsHistoryAndYieldsFailedEvent()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse("c1", "ReportError", new Dictionary<string, object?>()));
        client.SetupResponse(CreateTextResponse("done"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var events = new List<LLMSessionEvent>();
        await foreach (var ev in session.ChatEventsAsync("error"))
        {
            events.Add(ev);
        }

        Assert.That(events.OfType<ToolCallCompletedEvent>(), Is.Empty);
        var failed = events.OfType<ToolCallFailedEvent>().Single();
        Assert.That(failed.Error, Is.EqualTo("No game loaded"));
        Assert.That(failed.ErrorType, Is.EqualTo(nameof(ToolError)));
        Assert.That(failed.ErrorCode, Is.EqualTo("NO_GAME"));

        var historyResult = GetFunctionResult(client.ReceivedMessagesHistory[1]);
        Assert.That(historyResult.Result, Is.TypeOf<string>());
        using var doc = JsonDocument.Parse((string)historyResult.Result!);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("No game loaded"));
        Assert.That(doc.RootElement.GetProperty("code").GetString(), Is.EqualTo("NO_GAME"));
    }

    [Test]
    public async Task ToolReturning_ToolOk_UsesSessionMaxToolResultLength()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse("c1", "ConfirmThing", new Dictionary<string, object?> { ["name"] = new string('x', 100) }));
        client.SetupResponse(CreateTextResponse("done"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            MaxToolResultLength = 32,
        });

        await session.ChatAsync("confirm long");

        var historyResult = GetFunctionResult(client.ReceivedMessagesHistory[1]);
        Assert.That(historyResult.Result, Is.TypeOf<string>());
        var formatted = (string)historyResult.Result!;
        Assert.That(formatted.Length, Is.EqualTo(32));
        Assert.That(formatted, Does.EndWith("...[truncated]"));
    }

    [Test]
    public async Task ToolReturning_PlainString_PassesThroughUnchanged()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse("c1", "PlainString", new Dictionary<string, object?>()));
        client.SetupResponse(CreateTextResponse("done"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        await session.ChatAsync("plain");

        var historyResult = GetFunctionResult(client.ReceivedMessagesHistory[1]);
        Assert.That(historyResult.Result, Is.EqualTo("I am a plain string"));
    }

    [Test]
    public void ToolError_ErrorType_NotSerialized()
    {
        // The ErrorType field is runtime-only (JsonIgnore) and must not appear
        // in the model-facing formatted output.
        var formatter = new ToolResultFormatter();
        var toolError = new ToolError("boom", "RUNTIME_EXCEPTION", "InvalidOperationException");

        string formatted = formatter.Format(toolError);

        using var doc = JsonDocument.Parse(formatted);
        Assert.That(doc.RootElement.GetProperty("error").GetString(), Is.EqualTo("boom"));
        Assert.That(doc.RootElement.GetProperty("code").GetString(), Is.EqualTo("RUNTIME_EXCEPTION"));
        Assert.That(doc.RootElement.TryGetProperty("errorType", out _), Is.False);
    }

    private sealed class ConverterBackedDataConverter : JsonConverter<ConverterBackedData>
    {
        public override ConverterBackedData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException();
        }

        public override void Write(Utf8JsonWriter writer, ConverterBackedData value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("converted", value.Value);
            writer.WriteEndObject();
        }
    }
}

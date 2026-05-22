using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Alco.LLM.Test;

[TestFixture]
public class LLMSessionTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [Test]
    public void Constructor_WithSystemPrompt_InjectsSystemMessage()
    {
        var client = new FakeChatClient();
        var registry = new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
        var tools = registry.ToAITools();

        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            SystemPrompt = "You are a helpful assistant."
        });

        client.SetupResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hi")]));

        session.ChatAsync("Hello").Wait();

        var firstCallMessages = client.ReceivedMessagesHistory[0];
        Assert.That(firstCallMessages[0].Role, Is.EqualTo(ChatRole.System));
        Assert.That(firstCallMessages[0].Text, Is.EqualTo("You are a helpful assistant."));
    }

    [Test]
    public void Constructor_WithoutSystemPrompt_NoSystemMessage()
    {
        var client = new FakeChatClient();
        var registry = new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
        var tools = registry.ToAITools();

        var session = new LLMSession(client, registry, tools);

        client.SetupResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Hi")]));

        session.ChatAsync("Hello").Wait();

        var firstCallMessages = client.ReceivedMessagesHistory[0];
        Assert.That(firstCallMessages[0].Role, Is.EqualTo(ChatRole.User));
    }

    [Test]
    public async Task ChatAsync_MultiTurn_HistoryPersists()
    {
        var client = new FakeChatClient();
        var registry = new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
        var tools = registry.ToAITools();

        var session = new LLMSession(client, registry, tools);

        client.SetupResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "First")]));
        client.SetupResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "Second")]));

        await session.ChatAsync("Turn 1");
        await session.ChatAsync("Turn 2");

        // Second call should include first turn messages
        var secondCallMessages = client.ReceivedMessagesHistory[1];
        Assert.That(secondCallMessages.Count, Is.GreaterThanOrEqualTo(3));
        Assert.That(secondCallMessages[0].Text, Is.EqualTo("Turn 1"));
        Assert.That(secondCallMessages[1].Text, Is.EqualTo("First"));
        Assert.That(secondCallMessages[2].Text, Is.EqualTo("Turn 2"));
    }
}

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Alco.LLM.Test;

[TestFixture]
public class ToolCallLoopTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static ToolRegistry CreateRegistry()
    {
        return new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
    }

    private static ToolRegistry CreateAdvancedRegistry()
    {
        return new ToolRegistry([typeof(FakeAdvancedToolFunctions)], null, JsonOptions);
    }

    private static ChatResponse CreateTextResponse(string text)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    private static ChatResponse CreateToolCallResponse(params (string callId, string name, IDictionary<string, object?> args)[] calls)
    {
        var contents = new List<AIContent>();
        foreach (var (callId, name, args) in calls)
        {
            contents.Add(new FunctionCallContent(callId, name, args));
        }

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]);
    }

    private static List<FunctionResultContent> GetFunctionResults(IReadOnlyList<ChatMessage> messages)
    {
        return messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .ToList();
    }

    private static IReadOnlyDictionary<string, object?> GetFailureResult(FunctionResultContent content)
    {
        Assert.That(content.Result, Is.TypeOf<Dictionary<string, object?>>());
        return (Dictionary<string, object?>)content.Result!;
    }

    [SetUp]
    public void SetUp()
    {
        FakeAdvancedToolFunctions.Reset();
    }

    #region ChatAsync

    [Test]
    public async Task ChatAsync_NoToolCalls_ReturnsTextDirectly()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateTextResponse("Hello!"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Hi");

        Assert.That(result, Is.EqualTo("Hello!"));
        Assert.That(client.GetResponseCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ChatAsync_SingleToolCall_InvokesAndReturnsResult()
    {
        var client = new FakeChatClient();
        // First response: tool call
        client.SetupResponse(CreateToolCallResponse(("call1", "Add", new Dictionary<string, object?> { ["a"] = 3, ["b"] = 5 })));
        // Second response: text after tool result
        client.SetupResponse(CreateTextResponse("The result is 8."));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Add 3 and 5");

        Assert.That(result, Is.EqualTo("The result is 8."));
        Assert.That(client.GetResponseCallCount, Is.EqualTo(2));

        // Verify history: user -> assistant(tool_call) -> tool(result) -> assistant(text)
        var history = client.ReceivedMessagesHistory;
        Assert.That(history.Count, Is.EqualTo(2));

        // Second call should include tool results in history
        var secondCallMessages = history[1];
        Assert.That(secondCallMessages.Count, Is.GreaterThanOrEqualTo(3));
    }

    [Test]
    public async Task ChatAsync_MultipleToolCalls_InvokesAll()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "Add", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 }),
            ("call2", "Echo", new Dictionary<string, object?> { ["message"] = "hi" })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Do both");

        Assert.That(result, Is.EqualTo("Done."));
        // History for second call should have both tool results
        var secondCallMessages = client.ReceivedMessagesHistory[1];
        var toolResults = GetFunctionResults(secondCallMessages);
        Assert.That(toolResults.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task ChatAsync_UnknownTool_ReturnsErrorResult()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "NonExistent", new Dictionary<string, object?>())));
        client.SetupResponse(CreateTextResponse("OK"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Call unknown");

        Assert.That(result, Is.EqualTo("OK"));
        // Second call history should have a tool message with error
        var secondCallMessages = client.ReceivedMessagesHistory[1];
        var toolResult = GetFunctionResults(secondCallMessages).Single();
        var failure = GetFailureResult(toolResult);
        Assert.That(failure["success"], Is.False);
        Assert.That(failure["errorType"], Is.EqualTo(nameof(KeyNotFoundException)));
    }

    [Test]
    public async Task ChatAsync_ToolThrowsException_ReturnsErrorResult()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "ThrowError", new Dictionary<string, object?>())));
        client.SetupResponse(CreateTextResponse("Handled error."));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Trigger error");

        Assert.That(result, Is.EqualTo("Handled error."));
        var toolResult = GetFunctionResults(client.ReceivedMessagesHistory[1]).Single();
        var failure = GetFailureResult(toolResult);
        Assert.That(failure["success"], Is.False);
        Assert.That(failure["errorType"], Is.EqualTo(nameof(InvalidOperationException)));
    }

    [Test]
    public async Task ChatAsync_AsyncToolThrowsException_ReturnsStructuredErrorResult()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "ThrowAsync", new Dictionary<string, object?>())));
        client.SetupResponse(CreateTextResponse("Handled async error."));

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Trigger async error");

        Assert.That(result, Is.EqualTo("Handled async error."));
        var toolResult = GetFunctionResults(client.ReceivedMessagesHistory[1]).Single();
        var failure = GetFailureResult(toolResult);
        Assert.That(failure["success"], Is.False);
        Assert.That(failure["errorType"], Is.EqualTo(nameof(InvalidOperationException)));
    }

    [Test]
    public async Task ChatAsync_AutoInvokeToolsFalse_DoesNotInvokeToolOrContinueLoop()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "AddAsync", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 })));

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            AutoInvokeTools = false,
        });

        var result = await session.ChatAsync("Add without invoking");

        Assert.That(result, Is.EqualTo(string.Empty));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(0));
        Assert.That(client.GetResponseCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ChatAsync_ToolTimeout_ReturnsStructuredErrorResultAndContinues()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "SlowAsync", new Dictionary<string, object?> { ["milliseconds"] = 500 })));
        client.SetupResponse(CreateTextResponse("Timeout handled."));

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            ToolTimeout = TimeSpan.FromMilliseconds(1),
        });

        var result = await session.ChatAsync("Trigger timeout");

        Assert.That(result, Is.EqualTo("Timeout handled."));
        var toolResult = GetFunctionResults(client.ReceivedMessagesHistory[1]).Single();
        var failure = GetFailureResult(toolResult);
        Assert.That(failure["success"], Is.False);
        Assert.That(failure["errorType"], Is.EqualTo(nameof(TimeoutException)));
    }

    [Test]
    public void ChatAsync_ExternalCancellation_CancelsRequest()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateTextResponse("unused"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () => await session.ChatAsync("Cancel", cts.Token));
    }

    [Test]
    public async Task ChatAsync_MaxIterationsReached_ReturnsLastResponse()
    {
        var client = new FakeChatClient();
        // Return tool calls for 128 iterations (loop limit)
        for (int i = 0; i < 128; i++)
        {
            client.SetupResponse(CreateToolCallResponse(("call" + i, "Add", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 1 })));
        }

        // Final response without tools
        client.SetupResponse(CreateTextResponse("Stopped."));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var result = await session.ChatAsync("Loop test");

        Assert.That(result, Is.EqualTo("Stopped."));
        // 128 iterations with tools + 1 final without = 129 calls
        Assert.That(client.GetResponseCallCount, Is.EqualTo(129));
    }

    [Test]
    public async Task ChatAsync_ToolCallLoopPreservesHistory()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "Echo", new Dictionary<string, object?> { ["message"] = "test" })));
        client.SetupResponse(CreateTextResponse("Final."));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        await session.ChatAsync("Hello");

        // Second call should have history: user, assistant(tool_calls), tool(result)
        var messages = client.ReceivedMessagesHistory[1];
        Assert.That(messages.Any(m => m.Role == ChatRole.User), Is.True);
        Assert.That(messages.Any(m => m.Role == ChatRole.Assistant), Is.True);
        Assert.That(messages.Any(m => m.Role == ChatRole.Tool), Is.True);
    }

    #endregion

    #region ChatStreamingAsync

    [Test]
    public async Task ChatStreamingAsync_TextOnly_YieldsTextChunks()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(AsyncEnumerable());

        async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("Hello")] };
            yield return new ChatResponseUpdate { Contents = [new TextContent(" World")] };
        }

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var chunks = new List<string>();
        await foreach (var chunk in session.ChatStreamingAsync("Hi"))
        {
            chunks.Add(chunk);
        }

        Assert.That(chunks, Is.EqualTo(new[] { "Hello", " World" }));
    }

    [Test]
    public async Task ChatStreamingAsync_ToolCallYieldsNotificationThenText()
    {
        var client = new FakeChatClient();

        // First streaming: tool call
        client.SetupStreamingResponse(ToolCallStream());
        // Second streaming: text
        client.SetupStreamingResponse(TextStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "Add", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 })]
            };
        }

        async IAsyncEnumerable<ChatResponseUpdate> TextStream()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("Done.")] };
        }

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var chunks = new List<string>();
        await foreach (var chunk in session.ChatStreamingAsync("Add"))
        {
            chunks.Add(chunk);
        }

        // Should have: tool name notification, args notification, then text
        Assert.That(chunks.Count, Is.GreaterThanOrEqualTo(2));
        Assert.That(chunks.Last(), Is.EqualTo("Done."));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ChatStreamingAsync_NotificationFormatMatchesExpected()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());
        client.SetupStreamingResponse(TextStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "Echo", new Dictionary<string, object?> { ["message"] = "hello" })]
            };
        }

        async IAsyncEnumerable<ChatResponseUpdate> TextStream()
        {
            yield break;
        }

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var chunks = new List<string>();
        await foreach (var chunk in session.ChatStreamingAsync("test"))
        {
            chunks.Add(chunk);
        }

        // First chunk should be the tool name with ] suffix
        Assert.That(chunks[0], Does.Contain("Echo]"));
    }

    [Test]
    public async Task ChatStreamingAsync_AutoInvokeToolsFalse_YieldsNotificationWithoutContinuingLoop()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "AddAsync", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 })]
            };
        }

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            AutoInvokeTools = false,
        });

        var chunks = new List<string>();
        await foreach (var chunk in session.ChatStreamingAsync("Add"))
        {
            chunks.Add(chunk);
        }

        Assert.That(chunks[0], Does.Contain("AddAsync]"));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(0));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
    }

    #endregion
}

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

    private static (string Error, string Code) GetFailureResult(FunctionResultContent content)
    {
        Assert.That(content.Result, Is.TypeOf<string>());
        using var doc = JsonDocument.Parse((string)content.Result!);
        return (doc.RootElement.GetProperty("error").GetString()!, doc.RootElement.GetProperty("code").GetString()!);
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
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
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
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(2));

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
        var (error, code) = GetFailureResult(toolResult);
        Assert.That(code, Is.EqualTo("TOOL_NOT_FOUND"));
        Assert.That(error, Does.Contain("NonExistent"));
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
        var (error, code) = GetFailureResult(toolResult);
        Assert.That(code, Is.EqualTo("RUNTIME_EXCEPTION"));
        Assert.That(error, Is.EqualTo("Test error"));
    }

    [Test]
    public async Task ChatAsync_AutoInvokeToolsFalse_DoesNotInvokeToolOrContinueLoop()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "Add", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 })));

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            AutoInvokeTools = false,
        });

        var result = await session.ChatAsync("Add without invoking");

        Assert.That(result, Is.EqualTo(string.Empty));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(0));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ChatAsync_ToolTimeout_ReturnsStructuredErrorResultAndContinues()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(("call1", "Slow", new Dictionary<string, object?> { ["milliseconds"] = 500 })));
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
        var (error, code) = GetFailureResult(toolResult);
        Assert.That(code, Is.EqualTo("TIMEOUT"));
        Assert.That(error, Does.Contain("timed out"));
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
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(129));
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

    #region ChatEventsAsync

    [Test]
    public async Task ChatEventsAsync_TextOnly_YieldsStructuredRequestAndTextEvents()
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

        var events = new List<LLMSessionEvent>();
        await foreach (var sessionEvent in session.ChatEventsAsync("Hi"))
        {
            events.Add(sessionEvent);
        }

        Assert.That(events.Select(e => e.GetType()), Is.EqualTo(new[]
        {
            typeof(RequestStartedEvent),
            typeof(TextDeltaEvent),
            typeof(TextDeltaEvent),
            typeof(RequestCompletedEvent),
        }));
        Assert.That(((RequestStartedEvent)events[0]).RequestIndex, Is.EqualTo(0));
        Assert.That(((TextDeltaEvent)events[1]).Text, Is.EqualTo("Hello"));
        Assert.That(((TextDeltaEvent)events[2]).Text, Is.EqualTo(" World"));
        Assert.That(((RequestCompletedEvent)events[3]).RequestIndex, Is.EqualTo(0));
    }

    [Test]
    public async Task ChatEventsAsync_ToolCall_YieldsStartedAndCompletedEvents()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());
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

        var events = new List<LLMSessionEvent>();
        await foreach (var sessionEvent in session.ChatEventsAsync("Add"))
        {
            events.Add(sessionEvent);
        }

        var started = events.OfType<ToolCallStartedEvent>().Single();
        var completed = events.OfType<ToolCallCompletedEvent>().Single();

        Assert.That(events.Select(e => e.GetType()), Is.EqualTo(new[]
        {
            typeof(RequestStartedEvent),
            typeof(ToolCallStartedEvent),
            typeof(ToolCallCompletedEvent),
            typeof(RequestCompletedEvent),
            typeof(RequestStartedEvent),
            typeof(TextDeltaEvent),
            typeof(RequestCompletedEvent),
        }));
        Assert.That(started.CallId, Is.EqualTo("call1"));
        Assert.That(started.ToolName, Is.EqualTo("Add"));
        Assert.That(started.Arguments, Is.Not.Null);
        Assert.That(started.Arguments!["a"], Is.EqualTo(2));
        Assert.That(completed.CallId, Is.EqualTo("call1"));
        Assert.That(completed.ToolName, Is.EqualTo("Add"));
        Assert.That(completed.Result, Is.EqualTo(5));
        Assert.That(completed.Duration, Is.GreaterThanOrEqualTo(TimeSpan.Zero));
        Assert.That(events.OfType<TextDeltaEvent>().Single().Text, Is.EqualTo("Done."));
        Assert.That(events.OfType<RequestStartedEvent>().Select(e => e.RequestIndex), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(events.OfType<RequestCompletedEvent>().Select(e => e.RequestIndex), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public async Task ChatEventsAsync_ToolFailure_YieldsFailedEvent()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());
        client.SetupStreamingResponse(TextStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "NonExistent", new Dictionary<string, object?>())]
            };
        }

        async IAsyncEnumerable<ChatResponseUpdate> TextStream()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("Handled.")] };
        }

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        var events = new List<LLMSessionEvent>();
        await foreach (var sessionEvent in session.ChatEventsAsync("Call unknown"))
        {
            events.Add(sessionEvent);
        }

        var failed = events.OfType<ToolCallFailedEvent>().Single();
        Assert.That(failed.CallId, Is.EqualTo("call1"));
        Assert.That(failed.ToolName, Is.EqualTo("NonExistent"));
        Assert.That(failed.ErrorType, Is.EqualTo(nameof(KeyNotFoundException)));
        Assert.That(failed.ErrorCode, Is.EqualTo("TOOL_NOT_FOUND"));
        Assert.That(failed.Error, Does.Contain("NonExistent"));
        Assert.That(events.OfType<TextDeltaEvent>().Single().Text, Is.EqualTo("Handled."));
    }

    [Test]
    public async Task ChatEventsAsync_ToolTimeout_YieldsFailedEvent()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());
        client.SetupStreamingResponse(TextStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "Slow", new Dictionary<string, object?> { ["milliseconds"] = 500 })]
            };
        }

        async IAsyncEnumerable<ChatResponseUpdate> TextStream()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("Timeout handled.")] };
        }

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            ToolTimeout = TimeSpan.FromMilliseconds(1),
        });

        var events = new List<LLMSessionEvent>();
        await foreach (var sessionEvent in session.ChatEventsAsync("Trigger timeout"))
        {
            events.Add(sessionEvent);
        }

        var failed = events.OfType<ToolCallFailedEvent>().Single();
        Assert.That(failed.ErrorType, Is.EqualTo(nameof(TimeoutException)));
        Assert.That(failed.ErrorCode, Is.EqualTo("TIMEOUT"));
    }

    [Test]
    public async Task ChatEventsAsync_AutoInvokeToolsFalse_YieldsStartedOnly()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "Add", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 })]
            };
        }

        var registry = CreateAdvancedRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, new LLMSessionConfig
        {
            AutoInvokeTools = false,
        });

        var events = new List<LLMSessionEvent>();
        await foreach (var sessionEvent in session.ChatEventsAsync("Add"))
        {
            events.Add(sessionEvent);
        }

        Assert.That(events.OfType<ToolCallStartedEvent>().Count(), Is.EqualTo(1));
        Assert.That(events.OfType<ToolCallCompletedEvent>().Count(), Is.EqualTo(0));
        Assert.That(events.OfType<ToolCallFailedEvent>().Count(), Is.EqualTo(0));
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(0));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ChatEventsAsync_ExternalCancellation_CancelsRequest()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(AsyncEnumerable());

        async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent("unused")] };
        }

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in session.ChatEventsAsync("Cancel", cts.Token))
            {
            }
        });
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
    public async Task ChatStreamingAsync_ToolCallYieldsTextOnlyAfterToolLoop()
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

        Assert.That(chunks, Is.EqualTo(new[] { "Done." }));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ChatStreamingAsync_DoesNotEmitToolNotificationStrings()
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

        Assert.That(chunks, Is.Empty);
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(2));
    }

    [Test]
    public async Task ChatStreamingAsync_AutoInvokeToolsFalse_YieldsNoToolNotificationAndDoesNotContinueLoop()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(ToolCallStream());

        async IAsyncEnumerable<ChatResponseUpdate> ToolCallStream()
        {
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent("call1", "Add", new Dictionary<string, object?> { ["a"] = 2, ["b"] = 3 })]
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

        Assert.That(chunks, Is.Empty);
        Assert.That(FakeAdvancedToolFunctions.CallCount, Is.EqualTo(0));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
    }

    #endregion
}

using System.IO;
using System.Net.Http;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Alco.LLM.Test;

/// <summary>
/// Tests for LLM request retry, mid-stream failure handling, and MaxIterationsReached event.
/// </summary>
[TestFixture]
public class LLMRetryTests
{
    private static System.Text.Json.JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static ToolRegistry CreateRegistry()
    {
        return new ToolRegistry([typeof(FakeToolFunctions)], null, JsonOptions);
    }

    private static ChatResponse CreateTextResponse(string text)
    {
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
    }

    private static ChatResponse CreateToolCallResponse(string callId, string name, IDictionary<string, object?> args)
    {
        var contents = new List<AIContent> { new FunctionCallContent(callId, name, args) };
        return new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]);
    }

    private static IAsyncEnumerable<ChatResponseUpdate> TextStream(string text)
    {
        return AsyncEnumerable();
        async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent(text)] };
        }
    }

    private static IAsyncEnumerable<ChatResponseUpdate> PartialThenThrowStream(string partial, Exception ex)
    {
        return AsyncEnumerable();
        async IAsyncEnumerable<ChatResponseUpdate> AsyncEnumerable()
        {
            yield return new ChatResponseUpdate { Contents = [new TextContent(partial)] };
            await Task.Yield();
            throw ex;
        }
    }

    private static LLMSessionConfig FastRetryConfig(int maxAttempts = 3, bool enabled = true)
    {
        return new LLMSessionConfig
        {
            RetryPolicy = new LLMRetryPolicy
            {
                Enabled = enabled,
                MaxAttempts = maxAttempts,
                BaseDelayMs = 1,
                MaxDelayMs = 5,
            },
        };
    }

    [Test]
    public async Task Retry_BeforeFirstToken_SucceedsOnAttempt2()
    {
        var client = new FakeChatClient();
        client.SetupStreamingException(new HttpRequestException("429"));
        client.SetupStreamingResponse(TextStream("Recovered"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig());

        var events = new List<LLMSessionEvent>();
        await foreach (var ev in session.ChatEventsAsync("hi"))
        {
            events.Add(ev);
        }

        var text = string.Concat(events.OfType<TextDeltaEvent>().Select(e => e.Text));
        Assert.That(text, Is.EqualTo("Recovered"));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(2));

        var retries = events.OfType<RequestRetryEvent>().ToList();
        Assert.That(retries.Count, Is.EqualTo(1));
        Assert.That(retries[0].Attempt, Is.EqualTo(1));
        Assert.That(retries[0].MaxAttempts, Is.EqualTo(3));
        Assert.That(retries[0].ErrorType, Is.EqualTo(nameof(HttpRequestException)));
    }

    [Test]
    public async Task Retry_Exhausted_ThrowsLast()
    {
        var client = new FakeChatClient();
        client.SetupStreamingException(new HttpRequestException("429 a"));
        client.SetupStreamingException(new HttpRequestException("429 b"));
        client.SetupStreamingException(new HttpRequestException("429 c"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig(maxAttempts: 3));

        var events = new List<LLMSessionEvent>();
        HttpRequestException? caught = null;
        try
        {
            await foreach (var ev in session.ChatEventsAsync("hi"))
            {
                events.Add(ev);
            }
        }
        catch (HttpRequestException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(caught!.Message, Is.EqualTo("429 c"));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(3));
        var retries = events.OfType<RequestRetryEvent>().ToList();
        Assert.That(retries.Count, Is.EqualTo(2));
        Assert.That(retries[0].Attempt, Is.EqualTo(1));
        Assert.That(retries[1].Attempt, Is.EqualTo(2));
    }

    [Test]
    public async Task Retry_MidStreamFailure_DoesNotRetry()
    {
        var client = new FakeChatClient();
        client.SetupStreamingResponse(PartialThenThrowStream("Partial", new IOException("mid-stream disconnect")));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig());

        var events = new List<LLMSessionEvent>();
        IOException? caught = null;
        try
        {
            await foreach (var ev in session.ChatEventsAsync("hi"))
            {
                events.Add(ev);
            }
        }
        catch (IOException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(caught!.Message, Is.EqualTo("mid-stream disconnect"));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
        Assert.That(events.OfType<RequestRetryEvent>(), Is.Empty);
        // Partial text was already yielded before the failure.
        var text = string.Concat(events.OfType<TextDeltaEvent>().Select(e => e.Text));
        Assert.That(text, Is.EqualTo("Partial"));
    }

    [Test]
    public void Retry_UserCancel_DoesNotRetry()
    {
        var client = new FakeChatClient();
        // Queue a transient exception so that WITHOUT cancel-handling it would retry.
        client.SetupStreamingException(new HttpRequestException("429"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in session.ChatEventsAsync("hi", cts.Token))
            {
            }
        });
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(0));
    }

    [Test]
    public async Task RetryDisabled_ThrowsImmediately()
    {
        var client = new FakeChatClient();
        client.SetupStreamingException(new HttpRequestException("429"));
        client.SetupStreamingResponse(TextStream("Should not reach"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig(enabled: false));

        var events = new List<LLMSessionEvent>();
        HttpRequestException? caught = null;
        try
        {
            await foreach (var ev in session.ChatEventsAsync("hi"))
            {
                events.Add(ev);
            }
        }
        catch (HttpRequestException ex)
        {
            caught = ex;
        }

        Assert.That(caught, Is.Not.Null);
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(1));
        Assert.That(events.OfType<RequestRetryEvent>(), Is.Empty);
    }

    [Test]
    public async Task FinalRequest_RetriesOnTransientError()
    {
        var client = new FakeChatClient();
        // 128 tool-call responses to exhaust the iteration cap, then the final request
        // fails once with a transient error and succeeds on retry.
        for (int i = 0; i < 128; i++)
        {
            client.SetupResponse(CreateToolCallResponse(
                "call" + i, "Add", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 1 }));
        }
        client.SetupStreamingException(new HttpRequestException("429"));
        client.SetupStreamingResponse(TextStream("Final"));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig());

        var events = new List<LLMSessionEvent>();
        await foreach (var ev in session.ChatEventsAsync("loop"))
        {
            events.Add(ev);
        }

        var text = string.Concat(events.OfType<TextDeltaEvent>().Select(e => e.Text));
        Assert.That(text, Is.EqualTo("Final"));
        // 128 tool-loop calls + 1 failed final + 1 successful final retry = 130.
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(130));
        Assert.That(events.OfType<MaxIterationsReachedEvent>().Count(), Is.EqualTo(1));
        // The retry happened on the final request.
        var retry = events.OfType<RequestRetryEvent>().Single();
        Assert.That(retry.RequestIndex, Is.EqualTo(128));
    }

    [Test]
    public async Task MaxIterationsReached_YieldsEvent()
    {
        var client = new FakeChatClient();
        for (int i = 0; i < 128; i++)
        {
            client.SetupResponse(CreateToolCallResponse(
                "call" + i, "Add", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 1 }));
        }
        client.SetupResponse(CreateTextResponse("Stopped."));

        var registry = CreateRegistry();
        var tools = registry.ToAITools();
        var session = new LLMSession(client, registry, tools, FastRetryConfig());

        var events = new List<LLMSessionEvent>();
        await foreach (var ev in session.ChatEventsAsync("loop"))
        {
            events.Add(ev);
        }

        var maxIter = events.OfType<MaxIterationsReachedEvent>().Single();
        // The MaxIterationsReachedEvent must appear before the final RequestStartedEvent.
        var finalStart = events.OfType<RequestStartedEvent>().Last();
        Assert.That(events.IndexOf(maxIter), Is.LessThan(events.IndexOf(finalStart)));
        Assert.That(maxIter.RequestIndex, Is.EqualTo(finalStart.RequestIndex));

        var text = string.Concat(events.OfType<TextDeltaEvent>().Select(e => e.Text));
        Assert.That(text, Is.EqualTo("Stopped."));
        Assert.That(client.GetStreamingResponseCallCount, Is.EqualTo(129));
    }
}

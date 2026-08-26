using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.AI;
using NUnit.Framework;

namespace Alco.LLM.Test;

[TestFixture]
public class ParallelToolExecutionTests
{
    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    [SetUp]
    public void SetUp()
    {
        FakeParallelToolFunctions.Reset();
    }

    private static ToolRegistry CreateRegistry()
    {
        return new ToolRegistry([typeof(FakeParallelToolFunctions)], null, JsonOptions);
    }

    private static LLMSession CreateSession(FakeChatClient client, ToolRegistry registry, LLMSessionConfig? config = null)
    {
        return new LLMSession(client, registry, registry.ToAITools(), config);
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

    private static async Task<List<LLMSessionEvent>> CollectEventsAsync(LLMSession session, string message)
    {
        var events = new List<LLMSessionEvent>();
        await foreach (var sessionEvent in session.ChatEventsAsync(message))
        {
            events.Add(sessionEvent);
        }

        return events;
    }

    // AC1: consecutive agent-thread tools execute concurrently on the thread pool.
    [Test]
    public async Task ChatAsync_ConsecutiveAgentThreadTools_RunInParallel()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "Rendezvous", new Dictionary<string, object?> { ["id"] = "a" }),
            ("call2", "Rendezvous", new Dictionary<string, object?> { ["id"] = "b" })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry);

        await session.ChatAsync("go");

        // Each Rendezvous call only returns ":parallel" when both calls overlap in time.
        var results = GetFunctionResults(client.ReceivedMessagesHistory[1]);
        Assert.That(results.Select(r => r.Result), Is.EqualTo(new[] { "a:parallel", "b:parallel" }));
    }

    // AC2: a main-thread tool occupies an exclusive batch and never overlaps agent-thread batches.
    [Test]
    public async Task ChatAsync_MainThreadTool_DoesNotOverlapAgentThreadBatches()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "Tracked", new Dictionary<string, object?> { ["id"] = "a1", ["milliseconds"] = 50 }),
            ("call2", "Tracked", new Dictionary<string, object?> { ["id"] = "a2", ["milliseconds"] = 50 }),
            ("call3", "MainTracked", new Dictionary<string, object?> { ["id"] = "m" }),
            ("call4", "Tracked", new Dictionary<string, object?> { ["id"] = "a3", ["milliseconds"] = 10 })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry);

        using var pumpCts = new CancellationTokenSource();
        var pump = Task.Run(() =>
        {
            while (!pumpCts.IsCancellationRequested)
            {
                registry.DrainMainThreadQueue();
                Thread.Sleep(1);
            }
        });

        try
        {
            await session.ChatAsync("go");
        }
        finally
        {
            pumpCts.Cancel();
            await pump;
        }

        var log = FakeParallelToolFunctions.ExecutionLog.ToList();
        int mainEnter = log.IndexOf("enter:m");
        Assert.That(mainEnter, Is.GreaterThan(log.IndexOf("exit:a1")));
        Assert.That(mainEnter, Is.GreaterThan(log.IndexOf("exit:a2")));
        Assert.That(log.IndexOf("enter:a3"), Is.GreaterThan(log.IndexOf("exit:m")));
    }

    // AC3: one tool failing in a parallel batch does not affect the others.
    [Test]
    public async Task ChatEventsAsync_ParallelBatchOneToolThrows_OtherSucceeds()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "AgentThrow", new Dictionary<string, object?>()),
            ("call2", "Tracked", new Dictionary<string, object?> { ["id"] = "ok", ["milliseconds"] = 10 })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry);

        var events = await CollectEventsAsync(session, "go");

        var failed = events.OfType<ToolCallFailedEvent>().Single();
        Assert.That(failed.CallId, Is.EqualTo("call1"));
        Assert.That(failed.ErrorType, Is.EqualTo(nameof(InvalidOperationException)));

        var completed = events.OfType<ToolCallCompletedEvent>().Single();
        Assert.That(completed.CallId, Is.EqualTo("call2"));
        Assert.That(completed.Result, Is.EqualTo("ok"));

        var results = GetFunctionResults(client.ReceivedMessagesHistory[1]);
        Assert.That(results.Count, Is.EqualTo(2));
    }

    // AC4: one tool timing out in a parallel batch does not affect the others.
    [Test]
    public async Task ChatEventsAsync_ParallelBatchOneToolTimesOut_OtherSucceeds()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "AgentSlow", new Dictionary<string, object?> { ["milliseconds"] = 2000 }),
            ("call2", "Tracked", new Dictionary<string, object?> { ["id"] = "fast", ["milliseconds"] = 10 })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry, new LLMSessionConfig
        {
            ToolTimeout = TimeSpan.FromMilliseconds(200),
        });

        var events = await CollectEventsAsync(session, "go");

        var failed = events.OfType<ToolCallFailedEvent>().Single();
        Assert.That(failed.CallId, Is.EqualTo("call1"));
        Assert.That(failed.ErrorType, Is.EqualTo(nameof(TimeoutException)));

        var completed = events.OfType<ToolCallCompletedEvent>().Single();
        Assert.That(completed.CallId, Is.EqualTo("call2"));
        Assert.That(completed.Result, Is.EqualTo("fast"));
    }

    // AC5: results are ordered by original tool call order even when completion order differs.
    [Test]
    public async Task ChatAsync_ParallelBatch_ResultsFollowOriginalOrder()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "Tracked", new Dictionary<string, object?> { ["id"] = "slow", ["milliseconds"] = 300 }),
            ("call2", "Tracked", new Dictionary<string, object?> { ["id"] = "fast", ["milliseconds"] = 10 })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry);

        await session.ChatAsync("go");

        var results = GetFunctionResults(client.ReceivedMessagesHistory[1]);
        Assert.That(results.Select(r => r.CallId), Is.EqualTo(new[] { "call1", "call2" }));
        Assert.That(results.Select(r => r.Result), Is.EqualTo(new[] { "slow", "fast" }));
    }

    // AC6: MaxConcurrentTools <= 1 bypasses the parallel path and executes serially in order.
    [Test]
    public async Task ChatAsync_MaxConcurrentToolsOne_ExecutesSeriallyInOrder()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "Tracked", new Dictionary<string, object?> { ["id"] = "a", ["milliseconds"] = 50 }),
            ("call2", "Tracked", new Dictionary<string, object?> { ["id"] = "b", ["milliseconds"] = 50 })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry, new LLMSessionConfig
        {
            MaxConcurrentTools = 1,
        });

        await session.ChatAsync("go");

        Assert.That(FakeParallelToolFunctions.MaxActiveCount, Is.EqualTo(1));
        Assert.That(FakeParallelToolFunctions.ExecutionLog, Is.EqualTo(new[]
        {
            "enter:a", "exit:a", "enter:b", "exit:b",
        }));
    }

    // AC7: external cancellation propagates out of a parallel batch.
    [Test]
    public void ChatAsync_ExternalCancellationDuringParallelBatch_Throws()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "AgentSlow", new Dictionary<string, object?> { ["milliseconds"] = 5000 }),
            ("call2", "AgentSlow", new Dictionary<string, object?> { ["milliseconds"] = 5000 })));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // TaskCanceledException (derived from OperationCanceledException) is acceptable.
        Assert.CatchAsync<OperationCanceledException>(async () => await session.ChatAsync("go", cts.Token));
    }

    // AC8: Started events yield in original order, Completed events yield in original order.
    [Test]
    public async Task ChatEventsAsync_ParallelBatch_EventOrderFollowsOriginalOrder()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "Tracked", new Dictionary<string, object?> { ["id"] = "slow", ["milliseconds"] = 300 }),
            ("call2", "Tracked", new Dictionary<string, object?> { ["id"] = "fast", ["milliseconds"] = 10 })));
        client.SetupResponse(CreateTextResponse("Done."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry);

        var events = await CollectEventsAsync(session, "go");

        var toolEvents = events
            .Where(e => e is ToolCallStartedEvent or ToolCallCompletedEvent)
            .ToList();
        Assert.That(toolEvents.Select(e => e.GetType()), Is.EqualTo(new[]
        {
            typeof(ToolCallStartedEvent),
            typeof(ToolCallStartedEvent),
            typeof(ToolCallCompletedEvent),
            typeof(ToolCallCompletedEvent),
        }));
        Assert.That(((ToolCallStartedEvent)toolEvents[0]).CallId, Is.EqualTo("call1"));
        Assert.That(((ToolCallStartedEvent)toolEvents[1]).CallId, Is.EqualTo("call2"));
        Assert.That(((ToolCallCompletedEvent)toolEvents[2]).CallId, Is.EqualTo("call1"));
        Assert.That(((ToolCallCompletedEvent)toolEvents[3]).CallId, Is.EqualTo("call2"));
    }

    // AC9: timeout applies to a single agent-thread tool on the serial path.
    [Test]
    public async Task ChatEventsAsync_SingleAgentThreadToolTimeout_YieldsFailedEvent()
    {
        var client = new FakeChatClient();
        client.SetupResponse(CreateToolCallResponse(
            ("call1", "AgentSlow", new Dictionary<string, object?> { ["milliseconds"] = 2000 })));
        client.SetupResponse(CreateTextResponse("Timeout handled."));

        var registry = CreateRegistry();
        var session = CreateSession(client, registry, new LLMSessionConfig
        {
            ToolTimeout = TimeSpan.FromMilliseconds(50),
        });

        var events = await CollectEventsAsync(session, "go");

        var failed = events.OfType<ToolCallFailedEvent>().Single();
        Assert.That(failed.CallId, Is.EqualTo("call1"));
        Assert.That(failed.ErrorType, Is.EqualTo(nameof(TimeoutException)));
        Assert.That(failed.Duration, Is.LessThan(TimeSpan.FromMilliseconds(1500)));
    }
}

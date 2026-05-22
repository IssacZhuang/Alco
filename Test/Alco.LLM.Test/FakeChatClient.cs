using Microsoft.Extensions.AI;

namespace Alco.LLM.Test;

/// <summary>
/// Manual test double for <see cref="IChatClient"/> that returns pre-configured responses.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Queue<ChatResponse> _responses = new();
    private readonly Queue<IAsyncEnumerable<ChatResponseUpdate>> _streamingResponses = new();
    private readonly ChatClientMetadata _metadata = new("test", new Uri("http://localhost"), "test-model");

    public int GetResponseCallCount { get; private set; }
    public int GetStreamingResponseCallCount { get; private set; }
    public List<List<ChatMessage>> ReceivedMessagesHistory { get; } = new();

    public void SetupResponse(ChatResponse response)
    {
        _responses.Enqueue(response);
    }

    public void SetupStreamingResponse(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        _streamingResponses.Enqueue(updates);
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        GetResponseCallCount++;
        ReceivedMessagesHistory.Add(messages.ToList());

        if (_responses.Count == 0)
        {
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]));
        }

        return Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        GetStreamingResponseCallCount++;
        ReceivedMessagesHistory.Add(messages.ToList());

        if (_streamingResponses.Count == 0)
        {
            yield break;
        }

        var stream = _streamingResponses.Dequeue();
        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType == typeof(ChatClientMetadata) ? _metadata : null;
    }

    public void Dispose() { }
}

using Alco.AgentControlProtocol;
using Microsoft.Extensions.AI;

namespace Alco.LLM.Test;

/// <summary>
/// Manual test double for <see cref="IChatClient"/> that returns pre-configured responses.
/// Responses, streaming responses, and exceptions are served from a single FIFO queue so
/// the order of <see cref="SetupResponse"/>, <see cref="SetupStreamingResponse"/>, and
/// <see cref="SetupStreamingException"/> calls is preserved across streaming calls.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private abstract record QueuedItem;
    private sealed record ResponseItem(ChatResponse Response) : QueuedItem;
    private sealed record StreamingItem(IAsyncEnumerable<ChatResponseUpdate> Stream) : QueuedItem;
    private sealed record ExceptionItem(Exception Exception) : QueuedItem;

    private readonly Queue<QueuedItem> _streamingItems = new();
    private readonly Queue<ChatResponse> _nonStreamingResponses = new();
    private readonly ChatClientMetadata _metadata = new("test", new Uri("http://localhost"), "test-model");

    public int GetResponseCallCount { get; private set; }
    public int GetStreamingResponseCallCount { get; private set; }
    public List<List<ChatMessage>> ReceivedMessagesHistory { get; } = new();

    public void SetupResponse(ChatResponse response)
    {
        _streamingItems.Enqueue(new ResponseItem(response));
    }

    public void SetupStreamingResponse(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        _streamingItems.Enqueue(new StreamingItem(updates));
    }

    /// <summary>
    /// Queues an exception to be thrown by the next streaming response call, before any update is yielded.
    /// </summary>
    public void SetupStreamingException(Exception exception)
    {
        _streamingItems.Enqueue(new ExceptionItem(exception));
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetResponseCallCount++;
        ReceivedMessagesHistory.Add(messages.ToList());

        if (_nonStreamingResponses.Count > 0)
        {
            return Task.FromResult(_nonStreamingResponses.Dequeue());
        }

        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "")]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetStreamingResponseCallCount++;
        ReceivedMessagesHistory.Add(messages.ToList());

        if (_streamingItems.Count == 0)
        {
            yield break;
        }

        var item = _streamingItems.Dequeue();
        switch (item)
        {
            case ExceptionItem exItem:
                throw exItem.Exception;
            case StreamingItem streamItem:
                await foreach (var update in streamItem.Stream.WithCancellation(cancellationToken))
                {
                    yield return update;
                }
                break;
            case ResponseItem responseItem:
                foreach (var message in responseItem.Response.Messages)
                {
                    foreach (var content in message.Contents)
                    {
                        yield return new ChatResponseUpdate { Contents = [content] };
                    }
                }
                break;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType == typeof(ChatClientMetadata) ? _metadata : null;
    }

    public void Dispose() { }
}

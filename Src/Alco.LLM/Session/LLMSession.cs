using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Alco.LLM;

/// <summary>
/// Retry policy for transient LLM request failures. Applied to every streaming request
/// inside <see cref="LLMSession"/>. Retries only happen when no token has been received
/// yet; mid-stream failures are not retried to avoid duplicate output.
/// </summary>
public sealed record LLMRetryPolicy
{
    /// <summary>
    /// Whether retry is enabled. When <c>false</c>, any failure throws immediately.
    /// Default <c>true</c>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum number of attempts (including the first call). Default <c>3</c>.
    /// Values less than <c>1</c> are treated as <c>1</c>.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Base delay in milliseconds for exponential backoff. Default <c>1000</c>.
    /// </summary>
    public int BaseDelayMs { get; init; } = 1000;

    /// <summary>
    /// Maximum delay cap in milliseconds. Default <c>30000</c>.
    /// </summary>
    public int MaxDelayMs { get; init; } = 30000;
}

/// <summary>
/// Configuration for LLMSession.
/// </summary>
public class LLMSessionConfig
{
    /// <summary>
    /// The system prompt to initialize the chat history with.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Controls the temperature for the LLM response.
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// Controls whether to automatically invoke tool functions.
    /// </summary>
    public bool AutoInvokeTools { get; set; } = true;

    /// <summary>
    /// Controls the timeout for a single tool invocation. Values less than or equal to
    /// <see cref="TimeSpan.Zero"/> disable tool invocation timeout.
    /// </summary>
    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The LLM provider type, used to format tool results correctly per API protocol.
    /// OpenAI expects tool results in <see cref="ChatRole.Tool"/> messages,
    /// while Anthropic and Gemini expect them in <see cref="ChatRole.User"/> messages.
    /// </summary>
    public LLMProvider Provider { get; set; } = LLMProvider.OpenAI;

    /// <summary>
    /// Maximum number of tool calls that can execute concurrently within an agent-thread batch.
    /// Values less than or equal to 1 disable parallelism and execute all tool calls serially.
    /// </summary>
    public int MaxConcurrentTools { get; set; } = 10;

    /// <summary>
    /// JSON serializer options used when formatting structured tool results for LLM history.
    /// </summary>
    public JsonSerializerOptions? JsonOptions { get; set; }

    /// <summary>
    /// Hard cap for formatted model-facing tool result text.
    /// </summary>
    public int MaxToolResultLength { get; set; } = ToolResultFormatter.DefaultMaxFormattedLength;

    /// <summary>
    /// Retry policy for transient LLM request failures. Defaults to enabled with 3 attempts.
    /// </summary>
    public LLMRetryPolicy RetryPolicy { get; set; } = new();
}

/// <summary>
/// Represents the session for LLM operations, wrapping <see cref="IChatClient"/> and message history.
/// Implements the auto tool call loop for automatic function invocation.
/// </summary>
public sealed class LLMSession
{
    private const int MaxAutoInvokeIterations = 128;

    private readonly IChatClient _chatClient;
    private readonly ToolRegistry _registry;
    private readonly IList<AITool> _tools;
    private readonly List<ChatMessage> _chatHistory;
    private readonly ChatOptions _chatOptions;
    private readonly LLMProvider _provider;
    private readonly bool _autoInvokeTools;
    private readonly TimeSpan _toolTimeout;
    private readonly int _maxConcurrentTools;
    private readonly ToolResultFormatter _toolResultFormatter;
    private readonly LLMRetryPolicy _retryPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMSession"/> class.
    /// </summary>
    /// <param name="chatClient">The chat client to use for LLM communication.</param>
    /// <param name="registry">The tool registry for tool invocation.</param>
    /// <param name="tools">The AI tools to register with the LLM.</param>
    /// <param name="config">Optional configuration for the session.</param>
    public LLMSession(IChatClient chatClient, ToolRegistry registry, IList<AITool> tools, LLMSessionConfig? config = null)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));

        config ??= new LLMSessionConfig();
        _provider = config.Provider;
        _autoInvokeTools = config.AutoInvokeTools;
        _toolTimeout = config.ToolTimeout;
        _maxConcurrentTools = config.MaxConcurrentTools;
        _toolResultFormatter = new ToolResultFormatter(config.JsonOptions, config.MaxToolResultLength);
        _retryPolicy = config.RetryPolicy ?? new LLMRetryPolicy();

        _chatOptions = new ChatOptions
        {
            Temperature = (float)config.Temperature,
            Tools = tools,
        };

        _chatHistory = new List<ChatMessage>();
        if (!string.IsNullOrEmpty(config.SystemPrompt))
        {
            _chatHistory.Add(new ChatMessage(ChatRole.System, config.SystemPrompt));
        }
    }

    /// <summary>
    /// Sends a message and returns the full response, with automatic tool invocation.
    /// </summary>
    /// <param name="message">The user message to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The assistant's text response.</returns>
    public async Task<string> ChatAsync(string message, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        await foreach (var ev in ChatEventsAsync(message, cancellationToken))
        {
            if (ev is TextDeltaEvent textDelta)
                text.Append(textDelta.Text);
        }
        return text.ToString();
    }

    /// <summary>
    /// Sends a message and yields assistant text chunks only.
    /// Use <see cref="ChatEventsAsync"/> to observe tool calls and runtime events.
    /// </summary>
    /// <param name="message">The user message to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An async enumerable of text chunks.</returns>
    public async IAsyncEnumerable<string> ChatStreamingAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var sessionEvent in ChatEventsAsync(message, cancellationToken))
        {
            switch (sessionEvent)
            {
                case TextDeltaEvent textDelta:
                    yield return textDelta.Text;
                    break;
            }
        }
    }

    /// <summary>
    /// Sends a message and yields structured, real-time session events.
    /// Events are not persisted by the session; callers may collect them if needed.
    /// </summary>
    /// <param name="message">The user message to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An async enumerable of structured session events.</returns>
    public async IAsyncEnumerable<LLMSessionEvent> ChatEventsAsync(string message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _chatHistory.Add(new ChatMessage(ChatRole.User, message));
        int requestIndex = 0;

        for (int i = 0; i < MaxAutoInvokeIterations; i++)
        {
            var updates = new List<ChatResponseUpdate>();
            yield return new RequestStartedEvent(DateTimeOffset.UtcNow, requestIndex);

            await foreach (var item in InvokeWithRetryAsync(_chatHistory, _chatOptions, requestIndex, cancellationToken))
            {
                switch (item)
                {
                    case RetryStreamItem retryItem:
                        yield return retryItem.Event;
                        break;
                    case UpdateStreamItem updateItem:
                        updates.Add(updateItem.Update);
                        if (!string.IsNullOrEmpty(updateItem.Update.Text))
                        {
                            yield return new TextDeltaEvent(DateTimeOffset.UtcNow, updateItem.Update.Text);
                        }
                        break;
                }
            }

            var functionCalls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();

            _chatHistory.AddMessages(updates);

            if (functionCalls.Count == 0)
            {
                yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
                yield break;
            }

            if (!_autoInvokeTools)
            {
                foreach (var functionCall in functionCalls)
                {
                    yield return CreateStartedEvent(functionCall);
                }

                yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
                yield break;
            }

            // Tool calls are partitioned into batches: consecutive agent-thread tools form one
            // batch and execute concurrently on the thread pool; each main-thread tool gets an
            // exclusive batch. Batches execute strictly one after another.
            var results = new List<AIContent>(functionCalls.Count);
            int batchStart = 0;
            while (batchStart < functionCalls.Count)
            {
                bool isAgentThreadBatch = IsAgentThreadCall(functionCalls[batchStart]);
                int batchEnd = batchStart + 1;
                while (isAgentThreadBatch && batchEnd < functionCalls.Count && IsAgentThreadCall(functionCalls[batchEnd]))
                {
                    batchEnd++;
                }

                for (int callIndex = batchStart; callIndex < batchEnd; callIndex++)
                {
                    yield return CreateStartedEvent(functionCalls[callIndex]);
                }

                int batchCount = batchEnd - batchStart;
                if (isAgentThreadBatch && batchCount > 1 && _maxConcurrentTools > 1)
                {
                    // Each task writes a distinct array slot, and Task.WhenAll provides the
                    // happens-before guarantee for reading the slots afterwards.
                    using var semaphore = new SemaphoreSlim(_maxConcurrentTools);
                    var invocations = new ToolInvocationEventResult[batchCount];
                    var tasks = new Task[batchCount];
                    for (int slot = 0; slot < batchCount; slot++)
                    {
                        tasks[slot] = InvokeToolCallThrottledAsync(functionCalls[batchStart + slot], semaphore, invocations, slot, cancellationToken);
                    }

                    await Task.WhenAll(tasks);

                    for (int slot = 0; slot < batchCount; slot++)
                    {
                        results.Add(invocations[slot].ResultContent);
                        yield return invocations[slot].Event;
                    }
                }
                else
                {
                    for (int callIndex = batchStart; callIndex < batchEnd; callIndex++)
                    {
                        var invocation = await InvokeToolCallAsync(functionCalls[callIndex], cancellationToken);
                        results.Add(invocation.ResultContent);
                        yield return invocation.Event;
                    }
                }

                batchStart = batchEnd;
            }

            var toolRole = _provider == LLMProvider.OpenAI ? ChatRole.Tool : ChatRole.User;
            _chatHistory.Add(new ChatMessage(toolRole, results));
            yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
            requestIndex++;
        }

        // Loop exited without a tool-free response — the iteration cap was hit.
        yield return new MaxIterationsReachedEvent(DateTimeOffset.UtcNow, requestIndex);

        var finalOptions = new ChatOptions { Temperature = _chatOptions.Temperature };
        var finalUpdates = new List<ChatResponseUpdate>();
        yield return new RequestStartedEvent(DateTimeOffset.UtcNow, requestIndex);

        await foreach (var item in InvokeWithRetryAsync(_chatHistory, finalOptions, requestIndex, cancellationToken))
        {
            switch (item)
            {
                case RetryStreamItem retryItem:
                    yield return retryItem.Event;
                    break;
                case UpdateStreamItem updateItem:
                    finalUpdates.Add(updateItem.Update);
                    if (!string.IsNullOrEmpty(updateItem.Update.Text))
                    {
                        yield return new TextDeltaEvent(DateTimeOffset.UtcNow, updateItem.Update.Text);
                    }
                    break;
            }
        }

        _chatHistory.AddMessages(finalUpdates);
        yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
    }

    private static ToolCallStartedEvent CreateStartedEvent(FunctionCallContent functionCall)
    {
        return new ToolCallStartedEvent(
            DateTimeOffset.UtcNow,
            functionCall.CallId ?? string.Empty,
            functionCall.Name ?? string.Empty,
            functionCall.Arguments as IReadOnlyDictionary<string, object?>);
    }

    private bool IsAgentThreadCall(FunctionCallContent functionCall)
    {
        // Unknown tools fail fast with KeyNotFoundException and never touch game state,
        // so they are safe to schedule as part of an agent-thread batch.
        var descriptor = string.IsNullOrEmpty(functionCall.Name) ? null : _registry.GetTool(functionCall.Name);
        return descriptor == null || descriptor.IsOnAgentThread;
    }

    private async Task InvokeToolCallThrottledAsync(
        FunctionCallContent functionCall,
        SemaphoreSlim semaphore,
        ToolInvocationEventResult[] invocations,
        int slot,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            invocations[slot] = await InvokeToolCallAsync(functionCall, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<ToolInvocationEventResult> InvokeToolCallAsync(FunctionCallContent functionCall, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        object? result = null;
        Exception? error = null;

        try
        {
            var jsonArgs = functionCall.Arguments != null
                ? JsonSerializer.SerializeToElement(functionCall.Arguments)
                : JsonDocument.Parse("{}").RootElement;

            result = await InvokeToolWithTimeoutAsync(functionCall, jsonArgs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            stopwatch.Stop();
        }

        var callId = functionCall.CallId ?? string.Empty;
        var toolName = functionCall.Name ?? string.Empty;
        if (error != null)
        {
            var displayError = UnwrapException(error);
            var (code, errorType) = ClassifyError(displayError);
            var errorResult = new ToolError(displayError.Message, code, errorType);
            return new ToolInvocationEventResult(
                new FunctionResultContent(callId, _toolResultFormatter.Format(errorResult)),
                new ToolCallFailedEvent(
                    DateTimeOffset.UtcNow,
                    callId,
                    toolName,
                    displayError.Message,
                    errorType,
                    stopwatch.Elapsed,
                    errorResult.Code));
        }

        if (result is ToolError toolError)
        {
            return new ToolInvocationEventResult(
                new FunctionResultContent(callId, _toolResultFormatter.Format(toolError)),
                new ToolCallFailedEvent(
                    DateTimeOffset.UtcNow,
                    callId,
                    toolName,
                    toolError.Error,
                    toolError.ErrorType ?? nameof(ToolError),
                    stopwatch.Elapsed,
                    toolError.Code));
        }

        // Format structured results into compact text for LLM history; plain values pass through.
        // The raw result is preserved on the completion event for UI/debug consumers.
        object? llmResult = result is AgentToolResult toolResult
            ? _toolResultFormatter.Format(toolResult)
            : result;

        return new ToolInvocationEventResult(
            new FunctionResultContent(callId, llmResult),
            new ToolCallCompletedEvent(
                DateTimeOffset.UtcNow,
                callId,
                toolName,
                result,
                stopwatch.Elapsed));
    }

    private async Task<object?> InvokeToolWithTimeoutAsync(FunctionCallContent functionCall, JsonElement jsonArgs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(functionCall.Name))
        {
            throw new InvalidOperationException("Tool call name is missing.");
        }

        Task<object?> invokeTask = _registry.InvokeToolAsync(functionCall.Name, jsonArgs);

        if (_toolTimeout <= TimeSpan.Zero)
        {
            return await invokeTask.WaitAsync(cancellationToken);
        }

        using var timeoutCts = new CancellationTokenSource(_toolTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await invokeTask.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"Tool '{functionCall.Name}' timed out after {_toolTimeout.TotalMilliseconds:F0}ms.");
        }
    }

    private static (string Code, string ErrorType) ClassifyError(Exception unwrappedError)
    {
        string errorType = unwrappedError.GetType().Name;
        string code = unwrappedError switch
        {
            KeyNotFoundException => "TOOL_NOT_FOUND",
            TimeoutException => "TIMEOUT",
            _ => "RUNTIME_EXCEPTION",
        };
        return (code, errorType);
    }

    private static Exception UnwrapException(Exception error)
    {
        if (error is System.Reflection.TargetInvocationException { InnerException: not null } targetInvocationException)
        {
            return targetInvocationException.InnerException!;
        }

        if (error is AggregateException { InnerExceptions.Count: 1 } aggregateException)
        {
            return aggregateException.InnerExceptions[0];
        }

        return error;
    }

    /// <summary>
    /// Streaming retry wrapper. Yields <see cref="RetryStreamItem"/> for each retry attempt
    /// (before the backoff delay), then <see cref="UpdateStreamItem"/> for each streamed update
    /// from the successful attempt. Retries only happen when no update has been received yet;
    /// once the first update is yielded, mid-stream failures propagate without retry.
    /// </summary>
    private async IAsyncEnumerable<StreamItem> InvokeWithRetryAsync(
        List<ChatMessage> messages,
        ChatOptions options,
        int requestIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int maxAttempts = Math.Max(1, _retryPolicy.MaxAttempts);
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        bool hasFirst = false;
        ChatResponseUpdate? firstUpdate = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            RequestRetryEvent? retryEvent = null;
            bool shouldRetry = false;
            try
            {
                enumerator = _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                hasFirst = await enumerator.MoveNextAsync();
                if (hasFirst)
                {
                    firstUpdate = enumerator.Current;
                }
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync();
                }
                throw;
            }
            catch (Exception ex) when (_retryPolicy.Enabled && IsTransient(ex) && attempt < maxAttempts - 1)
            {
                if (enumerator != null)
                {
                    await enumerator.DisposeAsync();
                }
                enumerator = null;
                hasFirst = false;
                firstUpdate = null;
                int delayMs = ComputeBackoff(attempt);
                retryEvent = new RequestRetryEvent(
                    DateTimeOffset.UtcNow,
                    requestIndex,
                    attempt + 1,
                    maxAttempts,
                    delayMs,
                    ex.Message,
                    ex.GetType().Name);
                shouldRetry = true;
            }

            if (shouldRetry)
            {
                yield return new RetryStreamItem(retryEvent!);
                await Task.Delay(retryEvent!.DelayMs, cancellationToken);
                continue;
            }
        }

        if (hasFirst)
        {
            yield return new UpdateStreamItem(firstUpdate!);
            while (await enumerator!.MoveNextAsync())
            {
                yield return new UpdateStreamItem(enumerator.Current);
            }
        }
        await enumerator!.DisposeAsync();
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException
            || ex is IOException
            || ex is TimeoutException
            || ex is SocketException;
    }

    private int ComputeBackoff(int failedAttempt)
    {
        long exponential = (long)_retryPolicy.BaseDelayMs * (1L << failedAttempt);
        int cap = (int)Math.Min(exponential, _retryPolicy.MaxDelayMs);
        return Random.Shared.Next(0, cap + 1);
    }

    private abstract record StreamItem;
    private sealed record UpdateStreamItem(ChatResponseUpdate Update) : StreamItem;
    private sealed record RetryStreamItem(RequestRetryEvent Event) : StreamItem;

    private sealed record ToolInvocationEventResult(FunctionResultContent ResultContent, LLMSessionEvent Event);
}

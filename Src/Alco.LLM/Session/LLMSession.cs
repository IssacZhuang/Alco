using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Alco.LLM;

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
        _chatHistory.Add(new ChatMessage(ChatRole.User, message));

        for (int i = 0; i < MaxAutoInvokeIterations; i++)
        {
            var response = await _chatClient.GetResponseAsync(_chatHistory, _chatOptions, cancellationToken);
            var assistantMessage = response.Messages.LastOrDefault();

            if (assistantMessage == null)
            {
                return string.Empty;
            }

            var functionCalls = assistantMessage.Contents.OfType<FunctionCallContent>().ToList();
            if (functionCalls.Count == 0)
            {
                _chatHistory.Add(assistantMessage);
                return assistantMessage.Text;
            }

            if (!_autoInvokeTools)
            {
                _chatHistory.Add(assistantMessage);
                return assistantMessage.Text;
            }

            _chatHistory.Add(assistantMessage);
            await InvokeToolCallsAsync(functionCalls, cancellationToken);
        }

        // Max iterations reached, make one final request without tools
        var finalOptions = new ChatOptions { Temperature = _chatOptions.Temperature };
        var finalResponse = await _chatClient.GetResponseAsync(_chatHistory, finalOptions, cancellationToken);
        var finalMessage = finalResponse.Messages.LastOrDefault();

        if (finalMessage != null)
        {
            _chatHistory.Add(finalMessage);
        }

        return finalMessage?.Text ?? string.Empty;
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

            await foreach (var update in _chatClient.GetStreamingResponseAsync(_chatHistory, _chatOptions, cancellationToken))
            {
                updates.Add(update);

                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return new TextDeltaEvent(DateTimeOffset.UtcNow, update.Text);
                }

                foreach (var fc in update.Contents.OfType<FunctionCallContent>())
                {
                    yield return new ToolCallStartedEvent(
                        DateTimeOffset.UtcNow,
                        fc.CallId ?? string.Empty,
                        fc.Name ?? string.Empty,
                        CopyArguments(fc));
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
                yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
                yield break;
            }

            var results = new List<AIContent>(functionCalls.Count);
            foreach (var functionCall in functionCalls)
            {
                var invocation = await InvokeToolCallAsync(functionCall, cancellationToken);
                results.Add(invocation.ResultContent);
                yield return invocation.Event;
            }

            AddToolResultsToHistory(results);
            yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
            requestIndex++;
        }

        var finalOptions = new ChatOptions { Temperature = _chatOptions.Temperature };
        var finalUpdates = new List<ChatResponseUpdate>();
        yield return new RequestStartedEvent(DateTimeOffset.UtcNow, requestIndex);

        await foreach (var update in _chatClient.GetStreamingResponseAsync(_chatHistory, finalOptions, cancellationToken))
        {
            finalUpdates.Add(update);

            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return new TextDeltaEvent(DateTimeOffset.UtcNow, update.Text);
            }
        }

        _chatHistory.AddMessages(finalUpdates);
        yield return new RequestCompletedEvent(DateTimeOffset.UtcNow, requestIndex);
    }

    private async Task InvokeToolCallsAsync(List<FunctionCallContent> functionCalls, CancellationToken cancellationToken)
    {
        var results = new List<AIContent>(functionCalls.Count);
        foreach (var fc in functionCalls)
        {
            var invocation = await InvokeToolCallAsync(fc, cancellationToken);
            results.Add(invocation.ResultContent);
        }

        AddToolResultsToHistory(results);
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
            return new ToolInvocationEventResult(
                new FunctionResultContent(callId, CreateToolFailureResult(displayError)),
                new ToolCallFailedEvent(
                    DateTimeOffset.UtcNow,
                    callId,
                    toolName,
                    displayError.Message,
                    displayError.GetType().Name,
                    stopwatch.Elapsed));
        }

        return new ToolInvocationEventResult(
            new FunctionResultContent(callId, result),
            new ToolCallCompletedEvent(
                DateTimeOffset.UtcNow,
                callId,
                toolName,
                result,
                stopwatch.Elapsed));
    }

    private void AddToolResultsToHistory(List<AIContent> results)
    {
        var toolRole = _provider == LLMProvider.OpenAI ? ChatRole.Tool : ChatRole.User;
        _chatHistory.Add(new ChatMessage(toolRole, results));
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

    private static Dictionary<string, object?> CreateToolFailureResult(Exception error)
    {
        Exception displayError = UnwrapException(error);
        return new Dictionary<string, object?>
        {
            ["success"] = false,
            ["error"] = displayError.Message,
            ["errorType"] = displayError.GetType().Name,
        };
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

    private static IReadOnlyDictionary<string, object?>? CopyArguments(FunctionCallContent functionCall)
    {
        return functionCall.Arguments != null
            ? new Dictionary<string, object?>(functionCall.Arguments)
            : null;
    }

    private sealed record ToolInvocationEventResult(FunctionResultContent ResultContent, LLMSessionEvent Event);
}

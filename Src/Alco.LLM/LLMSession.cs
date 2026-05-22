using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Sends a message and yields streaming response chunks, with automatic tool invocation.
    /// Tool call notifications are yielded inline as text fragments.
    /// </summary>
    /// <param name="message">The user message to send.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An async enumerable of text chunks.</returns>
    public async IAsyncEnumerable<string> ChatStreamingAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _chatHistory.Add(new ChatMessage(ChatRole.User, message));

        for (int i = 0; i < MaxAutoInvokeIterations; i++)
        {
            var updates = new List<ChatResponseUpdate>();

            await foreach (var update in _chatClient.GetStreamingResponseAsync(_chatHistory, _chatOptions, cancellationToken))
            {
                updates.Add(update);

                // Yield text content
                if (!string.IsNullOrEmpty(update.Text))
                {
                    yield return update.Text;
                }

                // Yield tool call notifications inline
                foreach (var fc in update.Contents.OfType<FunctionCallContent>())
                {
                    if (fc.Name != null)
                    {
                        yield return $"{fc.Name}]";
                    }

                    if (fc.Arguments != null)
                    {
                        yield return JsonSerializer.Serialize(fc.Arguments);
                    }
                }
            }

            // Collect function calls from all updates
            var functionCalls = updates.SelectMany(u => u.Contents.OfType<FunctionCallContent>()).ToList();

            // Reconstruct full assistant message from updates (preserves reasoning_content, etc.)
            _chatHistory.AddMessages(updates);

            if (functionCalls.Count == 0)
            {
                yield break;
            }

            // Invoke tools and add results
            await InvokeToolCallsAsync(functionCalls, cancellationToken);
        }

        // Max iterations reached, make one final streaming request without tools
        var finalOptions = new ChatOptions { Temperature = _chatOptions.Temperature };
        var finalUpdates = new List<ChatResponseUpdate>();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(_chatHistory, finalOptions, cancellationToken))
        {
            finalUpdates.Add(update);

            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }

        _chatHistory.AddMessages(finalUpdates);
    }

    private async Task InvokeToolCallsAsync(List<FunctionCallContent> functionCalls, CancellationToken cancellationToken)
    {
        foreach (var fc in functionCalls)
        {
            object? result = null;
            Exception? error = null;

            try
            {
                var jsonArgs = fc.Arguments != null
                    ? JsonSerializer.SerializeToElement(fc.Arguments)
                    : JsonDocument.Parse("{}").RootElement;

                result = await _registry.InvokeToolAsync(fc.Name!, jsonArgs);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            var resultContent = error != null
                ? new FunctionResultContent(fc.CallId, error.Message)
                : new FunctionResultContent(fc.CallId, result);

            _chatHistory.Add(new ChatMessage(ChatRole.Tool, [resultContent]));
        }
    }
}

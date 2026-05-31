using System;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel.Primitives;
using Anthropic;
using GenerativeAI.Microsoft;

namespace Alco.LLM;

/// <summary>
/// A wrapper around <see cref="IChatClient"/> to act as an agent.
/// Uses <see cref="ToolRegistry"/> for tool management and
/// provides <see cref="IChatClient"/> integration for in-game chat sessions.
/// </summary>
public class LLMAgent
{
    private readonly IChatClient _chatClient;
    private readonly ToolRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IList<AITool> _tools;
    private readonly string? _systemPrompt;
    private readonly LLMProvider _provider;

    /// <summary>
    /// Gets the tool registry containing all discovered tool functions.
    /// </summary>
    public ToolRegistry Registry => _registry;

    /// <summary>
    /// Gets the JSON serializer options for tool parameter handling.
    /// </summary>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Gets the AI tools registered with this agent.
    /// </summary>
    public IList<AITool> Tools => _tools;

    private LLMAgent(IChatClient chatClient, ToolRegistry registry, JsonSerializerOptions jsonOptions, IList<AITool> tools, string? systemPrompt, LLMProvider provider)
    {
        _chatClient = chatClient;
        _registry = registry;
        _jsonOptions = jsonOptions;
        _tools = tools;
        _systemPrompt = systemPrompt;
        _provider = provider;
    }

    /// <summary>
    /// Creates an LLMAgent with the specified options.
    /// Discovers tool functions and registers them with the chat client and HTTP API layer.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <param name="jsonOptions">The JSON serializer options configured with engine type converters.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public static LLMAgent Create(LLMAgentOptions options, JsonSerializerOptions jsonOptions)
    {
        IChatClient chatClient = options.Provider switch
        {
            LLMProvider.OpenAI => CreateOpenAIChatClient(options),
            LLMProvider.Anthropic => CreateAnthropicChatClient(options),
            LLMProvider.Gemini => CreateGeminiChatClient(options),
            _ => throw new ArgumentException($"Unsupported LLM provider: {options.Provider}"),
        };

        var registry = new ToolRegistry(
            options.ToolTypes ?? Array.Empty<Type>(),
            options.ToolInstances,
            jsonOptions);

        var tools = registry.ToAITools();

        return new LLMAgent(chatClient, registry, jsonOptions, tools, options.SystemPrompt, options.Provider);
    }

    private static IChatClient CreateOpenAIChatClient(LLMAgentOptions options)
    {
        if (options.Endpoint == null)
            throw new ArgumentException("Endpoint is required for OpenAI provider.");

        var handler = new ReasoningContentHandler();
        var httpClient = new HttpClient(handler);
        var transport = new HttpClientPipelineTransport(httpClient);

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = options.Endpoint,
            Transport = transport,
        };
        var openAIClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(options.ApiKey), clientOptions);
        return openAIClient.GetChatClient(options.ModelId).AsIChatClient();
    }

    private static IChatClient CreateAnthropicChatClient(LLMAgentOptions options)
    {
        var client = options.Endpoint != null
            ? new AnthropicClient() { ApiKey = options.ApiKey, BaseUrl = options.Endpoint.ToString() }
            : new AnthropicClient() { ApiKey = options.ApiKey };
        return client.AsIChatClient(options.ModelId);
    }

    private static IChatClient CreateGeminiChatClient(LLMAgentOptions options)
    {
        var chatClient = new GenerativeAIChatClient(options.ApiKey, options.ModelId, false);
        return chatClient;
    }

    /// <summary>
    /// Creates a new LLM session using the agent's chat client.
    /// </summary>
    /// <param name="config">Optional configuration for the session.</param>
    /// <returns>A new LLMSession instance.</returns>
    public LLMSession CreateSession(LLMSessionConfig? config = null)
    {
        config ??= new LLMSessionConfig();
        config.SystemPrompt ??= _systemPrompt;
        config.Provider = _provider;
        return new LLMSession(_chatClient, _registry, _tools, config);
    }
}

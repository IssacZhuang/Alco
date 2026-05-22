using System;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;

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

    private LLMAgent(IChatClient chatClient, ToolRegistry registry, JsonSerializerOptions jsonOptions, IList<AITool> tools)
    {
        _chatClient = chatClient;
        _registry = registry;
        _jsonOptions = jsonOptions;
        _tools = tools;
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
        var clientOptions = new OpenAIClientOptions { Endpoint = options.Endpoint };
        var openAIClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(options.ApiKey), clientOptions);
        var chatClient = openAIClient.GetChatClient(options.ModelId).AsIChatClient();

        var registry = new ToolRegistry(
            options.ToolTypes ?? Array.Empty<Type>(),
            options.ToolInstances,
            jsonOptions);

        var tools = registry.ToAITools();

        return new LLMAgent(chatClient, registry, jsonOptions, tools);
    }

    /// <summary>
    /// Creates a new LLM session using the agent's chat client.
    /// </summary>
    /// <param name="config">Optional configuration for the session.</param>
    /// <returns>A new LLMSession instance.</returns>
    public LLMSession CreateSession(LLMSessionConfig? config = null)
    {
        return new LLMSession(_chatClient, _registry, _tools, config);
    }
}

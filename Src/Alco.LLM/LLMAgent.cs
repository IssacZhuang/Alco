using System;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// A wrapper around Semantic Kernel to act as an agent.
/// Uses <see cref="ToolRegistry"/> for tool management and
/// provides SK integration for in-game chat sessions.
/// </summary>
public class LLMAgent
{
    private readonly Kernel _kernel;
    private readonly ToolRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Gets the tool registry containing all discovered tool functions.
    /// </summary>
    public ToolRegistry Registry => _registry;

    /// <summary>
    /// Gets the JSON serializer options for tool parameter handling.
    /// </summary>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMAgent"/> class.
    /// </summary>
    /// <param name="kernel">The semantic kernel instance.</param>
    /// <param name="registry">The tool registry managing tool discovery.</param>
    /// <param name="jsonOptions">The JSON serializer options for tool parameter handling.</param>
    private LLMAgent(Kernel kernel, ToolRegistry registry, JsonSerializerOptions jsonOptions)
    {
        _kernel = kernel;
        _registry = registry;
        _jsonOptions = jsonOptions;
    }

    /// <summary>
    /// Creates an LLMAgent with the specified options.
    /// Discovers tool functions and registers them with both the SK kernel and the HTTP API layer.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public static LLMAgent Create(LLMAgentOptions options)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        for (int i = 0; i < options.JsonConverters.Count; i++)
        {
            jsonOptions.Converters.Add(options.JsonConverters[i]);
        }

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(options.ModelId, options.Endpoint, options.ApiKey);

        if (options.FunctionInvocationFilter != null)
        {
            builder.Services.AddSingleton(options.FunctionInvocationFilter);
        }

        builder.Services.AddSingleton(jsonOptions);

        var kernel = builder.Build();

        var registry = new ToolRegistry(
            options.ToolTypes ?? Array.Empty<Type>(),
            options.ToolInstances,
            jsonOptions);

        var plugin = registry.ToKernelPlugin();
        kernel.Plugins.Add(plugin);

        return new LLMAgent(kernel, registry, jsonOptions);
    }

    /// <summary>
    /// Creates a new LLM session using the agent's kernel.
    /// </summary>
    /// <param name="config">Optional configuration for the session.</param>
    /// <returns>A new LLMSession instance.</returns>
    public LLMSession CreateSession(LLMSessionConfig? config = null)
    {
        return new LLMSession(_kernel, config);
    }
}

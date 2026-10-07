using System;
using System.Text.Json;
using Alco.AgentControlProtocol;
using Alco.Engine;

namespace Alco.LLM;

/// <summary>
/// Factory for in-process LLM agents: provides the agent-facing JSON options and
/// creates <see cref="LLMAgent"/> instances that share an external
/// <see cref="ToolRegistry"/> — typically the one owned by the agent control host,
/// whose main-thread queue is drained there.
/// </summary>
public sealed class LLMSystem
{
    private readonly JsonSerializerOptions _jsonOptions;
    private ToolRegistry? _registry;

    /// <summary>
    /// Gets the JSON serializer options configured with engine type converters.
    /// </summary>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Gets or sets the tool registry shared by created agents. Set to the agent
    /// control host's registry so a single main-thread queue backs both the HTTP API
    /// and in-process agents; when left unset, <see cref="CreateAgent"/> builds one
    /// from the options.
    /// </summary>
    public ToolRegistry? Registry
    {
        get => _registry;
        set => _registry = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMSystem"/> class.
    /// </summary>
    /// <param name="engine">The game engine used to create JSON converters for engine types.</param>
    public LLMSystem(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _jsonOptions = engine.CreateAgentJsonOptions();
    }

    /// <summary>
    /// Creates an LLMAgent with the specified options, reusing the shared registry
    /// when one is set (see <see cref="Registry"/>) so agents and the agent control
    /// API share a single tool set.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public LLMAgent CreateAgent(LLMAgentOptions options)
    {
        _registry ??= new ToolRegistry(
            options.ToolTypes ?? Array.Empty<Type>(),
            options.ToolInstances,
            _jsonOptions);

        return LLMAgent.Create(options, _jsonOptions, _registry);
    }
}

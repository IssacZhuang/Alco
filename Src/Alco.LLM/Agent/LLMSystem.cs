using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Alco.Engine;

namespace Alco.LLM;

/// <summary>
/// Engine system that provides main-thread marshaling for tool function invocations.
/// Drains the <see cref="ToolRegistry"/> main thread queue on each tick.
/// </summary>
public class LLMSystem : BaseEngineSystem
{
    private readonly JsonSerializerOptions _jsonOptions;
    private ToolRegistry? _registry;

    /// <summary>
    /// Gets the JSON serializer options configured with engine type converters.
    /// </summary>
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <summary>
    /// Gets or sets the tool registry whose main thread queue is drained on each tick.
    /// Set after the LLM agent is created.
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
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        foreach (var converter in engine.CreateDefaultJsonConverters())
        {
            _jsonOptions.Converters.Add(converter);
        }
    }

    /// <summary>
    /// Creates an LLMAgent with the specified options.
    /// The agent's registry reference is wired up for main-thread queue draining.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public LLMAgent CreateAgent(LLMAgentOptions options)
    {
        var agent = LLMAgent.Create(options, _jsonOptions);
        _registry = agent.Registry;
        return agent;
    }

    /// <inheritdoc/>
    public override void OnTick(float delta)
    {
        if (_registry != null)
        {
            _registry.DrainMainThreadQueue();
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        base.Dispose();
    }
}

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Alco.Engine;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Engine system that provides main-thread marshaling for tool function invocations.
/// Drains both the SK function invocation queue and the <see cref="ToolRegistry"/>
/// main thread queue on each tick.
/// </summary>
public class LLMSystem : BaseEngineSystem, IFunctionInvocationFilter
{
    private readonly ConcurrentQueue<Action> _skCallbackQueue = new();
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
    /// The LLMSystem is automatically set as the function invocation filter
    /// and its registry reference is wired up.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public LLMAgent CreateAgent(LLMAgentOptions options)
    {
        var agent = LLMAgent.Create(options with { FunctionInvocationFilter = this }, _jsonOptions);
        _registry = agent.Registry;
        return agent;
    }

    /// <inheritdoc/>
    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        if (ShouldInvokeOnMainThread(context))
        {
            var tcs = new TaskCompletionSource<bool>();
            _skCallbackQueue.Enqueue(async () =>
            {
                try
                {
                    await next(context);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            await tcs.Task;
        }
        else
        {
            await next(context);
        }
    }

    /// <summary>
    /// Determines whether the function should be invoked on the main thread.
    /// Functions marked with <see cref="ToolFunctionAttribute.AsyncSafe"/> are
    /// executed directly on the calling thread.
    /// </summary>
    /// <param name="context">The function invocation context.</param>
    /// <returns><c>true</c> if the function should run on the main thread.</returns>
    protected virtual bool ShouldInvokeOnMainThread(FunctionInvocationContext context)
    {
        MethodInfo? methodInfo = context.Function.UnderlyingMethod;
        if (methodInfo == null)
        {
            return true;
        }

        var attr = methodInfo.GetCustomAttribute<ToolFunctionAttribute>();
        if (attr != null && attr.AsyncSafe)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override void OnTick(float delta)
    {
        while (_skCallbackQueue.TryDequeue(out var callback))
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to execute SK callback: {ex}");
            }
        }

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

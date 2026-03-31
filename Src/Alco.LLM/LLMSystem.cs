using System;
using System.Collections.Concurrent;
using System.Reflection;
using Alco.Engine;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

public class LLMSystem : BaseEngineSystem, IFunctionInvocationFilter
{
    private readonly ConcurrentQueue<Action> _callbackQueue = new ConcurrentQueue<Action>();

    public LLMSystem()
    {
    }

    /// <summary>
    /// Creates an LLMAgent with the specified options.
    /// The LLMSystem is automatically set as the function invocation filter.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public LLMAgent CreateAgent(LLMAgentOptions options)
    {
        return LLMAgent.Create(options with { FunctionInvocationFilter = this });
    }

    public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
    {
        if (ShouldInvokeOnMainThread(context))
        {
            var tcs = new TaskCompletionSource<bool>();
            _callbackQueue.Enqueue(async () =>
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

    public override void OnTick(float delta)
    {
        while (_callbackQueue.TryDequeue(out var callback))
        {
            try
            {
                callback();
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to execute callback: {ex}");
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();
    }
}

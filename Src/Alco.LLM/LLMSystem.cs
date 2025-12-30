using System;
using System.Collections.Concurrent;
using Alco.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

public class LLMSystem : BaseEngineSystem, IFunctionInvocationFilter
{
    private readonly ConcurrentQueue<Action> _callbackQueue = new ConcurrentQueue<Action>();

    public LLMSystem()
    {
    }

    /// <summary>
    /// Creates an LLMAgent configured to connect to a remote OpenAI-compatible service.
    /// </summary>
    /// <param name="uri">The custom endpoint URI.</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="modelId">The model ID to use.</param>
    /// <param name="plugins">The plugins to add to the kernel.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public LLMAgent CreateAgentFromRemote(Uri uri, string apiKey, string modelId, params ReadOnlySpan<object> plugins)
    {
        return LLMAgent.CreateFromRemote(uri, apiKey, modelId, this, plugins);
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

    protected virtual bool ShouldInvokeOnMainThread(FunctionInvocationContext context)
    {
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

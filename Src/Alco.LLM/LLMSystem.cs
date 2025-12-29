using System;
using System.Collections.Concurrent;
using Alco.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

public class LLMSystem : BaseEngineSystem
{
    public class LLMSynchronizationContext : IFunctionInvocationFilter
    {
        private readonly LLMSystem _llmSystem;

        public LLMSynchronizationContext(LLMSystem llmSystem)
        {
            _llmSystem = llmSystem;
        }

        public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
        {
            if (ShouldInvokeOnMainThread(context))
            {
                var tcs = new TaskCompletionSource<bool>();
                _llmSystem._callbackQueue.Enqueue(async () =>
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
    }
    private LLMContext? _context;
    private readonly ConcurrentQueue<Action> _callbackQueue = new ConcurrentQueue<Action>();
    private readonly LLMSynchronizationContext _llmSynchronizationContext;
    private readonly List<object> _plugins = new List<object>();

    /// <summary>
    /// Gets a value indicating whether the LLM system is connected.
    /// </summary>
    public bool IsConnected => _context != null;

    /// <summary>
    /// Gets the current LLM context if connected.
    /// </summary>
    public LLMContext? Context => _context;

    public LLMSystem()
    {
        _llmSynchronizationContext = new LLMSynchronizationContext(this);
    }

    /// <summary>
    /// Connects to the LLM service with the specified configuration.
    /// </summary>
    /// <param name="modelId">The model ID to use (e.g., "gpt-4").</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="orgId">Optional organization ID.</param>
    public void Connect(string modelId, string apiKey, string? orgId = null)
    {
        ConnectInternal(
            builder => builder.AddOpenAIChatCompletion(modelId, apiKey, orgId),
            $"LLMSystem connected successfully. Model: {modelId}",
            "Failed to connect LLMSystem"
        );
    }

    /// <summary>
    /// Connects to an OpenAI-compatible LLM service with a custom endpoint.
    /// </summary>
    /// <param name="modelId">The model ID to use.</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="uri">The custom endpoint URI.</param>
    public void Connect(string modelId, string apiKey, Uri uri)
    {
        ConnectInternal(
            builder => builder.AddOpenAIChatCompletion(modelId, uri, apiKey),
            $"LLMSystem connected successfully to custom endpoint. Model: {modelId}, URI: {uri}",
            "Failed to connect LLMSystem to custom endpoint"
        );
    }

    private void ConnectInternal(Action<IKernelBuilder> configure, string successMessage, string errorMessage)
    {
        if (IsConnected)
        {
            Log.Warning("LLMSystem is already connected. Disconnect first to reconnect.");
            return;
        }

        try
        {
            var builder = Kernel.CreateBuilder();
            configure(builder);
            builder.Services.AddSingleton<IFunctionInvocationFilter>(_llmSynchronizationContext);
            foreach (var plugin in _plugins)
            {
                try
                {
                    builder.Plugins.AddFromObject(plugin);
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to add plugin {plugin.GetType().Name}: {ex.Message}");
                }
            }
            var kernel = builder.Build();

            foreach (var plugin in kernel.Plugins)
            {
                foreach (var function in plugin)
                {
                    Log.Info($"Plugin function registered: {plugin.Name}.{function.Name}");
                }
            }

            _context = new LLMContext(kernel);

            Log.Info(successMessage);
        }
        catch (Exception ex)
        {
            Log.Error($"{errorMessage}: {ex.Message}");
            _context = null;
            throw;
        }
    }

    /// <summary>
    /// Disconnects the current session and clears the context.
    /// </summary>
    public void Disconnect()
    {
        if (!IsConnected)
        {
            return;
        }

        _context = null;
        Log.Info("LLMSystem disconnected.");
    }

    public void AddPlugin(object plugin)
    {
        _plugins.Add(plugin);
    }

    public void RemovePlugin(object plugin)
    {
        _plugins.Remove(plugin);
    }

    public void ClearPlugins()
    {
        _plugins.Clear();
    }

    public async Task<string> ChatAsync(string message)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Agent is not connected.");
        }
        return await _context!.ChatAsync(message);
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
                Log.Error($"Failed to execute callback: {ex.Message}");
            }
        }
    }

    public override void Dispose()
    {
        Disconnect();
        base.Dispose();
    }
}


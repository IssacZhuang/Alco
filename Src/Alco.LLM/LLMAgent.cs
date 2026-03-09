using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// A wrapper around Semantic Kernel to act as an agent.
/// </summary>
public class LLMAgent
{
    private readonly Kernel _kernel;

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMAgent"/> class.
    /// </summary>
    /// <param name="kernel">The semantic kernel instance.</param>
    public LLMAgent(Kernel kernel)
    {
        _kernel = kernel;
    }

    /// <summary>
    /// Creates an LLMAgent configured to connect to a remote OpenAI-compatible service.
    /// </summary>
    /// <param name="uri">The custom endpoint URI.</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="modelId">The model ID to use.</param>
    /// <param name="invocationFilter">Optional function invocation filter.</param>
    /// <param name="plugins">The list of plugins to add to the kernel.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public static LLMAgent CreateFromRemote(Uri uri, string apiKey, string modelId, IFunctionInvocationFilter? invocationFilter = null, params ReadOnlySpan<object> plugins)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId, uri, apiKey);

        if (invocationFilter != null)
        {
            builder.Services.AddSingleton(invocationFilter);
        }

        foreach (var plugin in plugins)
        {
            builder.Plugins.AddFromObject(plugin);
        }

        var kernel = builder.Build();
        return new LLMAgent(kernel);
    }

    /// <summary>
    /// Creates an LLMAgent configured to connect to a remote OpenAI-compatible service using plugin types.
    /// </summary>
    /// <param name="uri">The custom endpoint URI.</param>
    /// <param name="apiKey">The API key for authentication.</param>
    /// <param name="modelId">The model ID to use.</param>
    /// <param name="invocationFilter">Optional function invocation filter.</param>
    /// <param name="pluginTypes">The list of plugin types to add to the kernel.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public static LLMAgent CreateFromRemote(Uri uri, string apiKey, string modelId, IFunctionInvocationFilter? invocationFilter, params ReadOnlySpan<Type> pluginTypes)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(modelId, uri, apiKey);

        if (invocationFilter != null)
        {
            builder.Services.AddSingleton(invocationFilter);
        }

        foreach (var type in pluginTypes)
        {
            var instance = Activator.CreateInstance(type);
            if (instance != null)
            {
                builder.Plugins.AddFromObject(instance, type.Name);
            }
        }

        var kernel = builder.Build();
        return new LLMAgent(kernel);
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

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using ModelContextProtocol.Server;

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
    /// Creates an LLMAgent with the specified options.
    /// </summary>
    /// <param name="options">The options for creating the agent.</param>
    /// <returns>A new instance of <see cref="LLMAgent"/>.</returns>
    public static LLMAgent Create(LLMAgentOptions options)
    {
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(options.ModelId, options.Endpoint, options.ApiKey);

        if (options.FunctionInvocationFilter != null)
        {
            builder.Services.AddSingleton(options.FunctionInvocationFilter);
        }

        if (options.Plugins != null)
        {
            for (int i = 0; i < options.Plugins.Count; i++)
            {
                builder.Plugins.AddFromObject(options.Plugins[i]);
            }
        }

        if (options.PluginTypes != null)
        {
            for (int i = 0; i < options.PluginTypes.Count; i++)
            {
                var type = options.PluginTypes[i];
                var instance = Activator.CreateInstance(type);
                if (instance != null)
                {
                    builder.Plugins.AddFromObject(instance, type.Name);
                }
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

    public void PushToolToMcp(IMcpServerBuilder builder)
    {
        foreach (var plugin in _kernel.Plugins)
        {
            foreach (var function in plugin)
            {
                builder.Services.AddSingleton(services => McpServerTool.Create(function));
            }
        }
    }
}

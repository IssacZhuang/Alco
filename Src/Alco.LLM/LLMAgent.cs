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
    private readonly GameToolBridge _bridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="LLMAgent"/> class.
    /// </summary>
    /// <param name="kernel">The semantic kernel instance.</param>
    /// <param name="bridge">The game tool bridge managing tool registration.</param>
    public LLMAgent(Kernel kernel, GameToolBridge bridge)
    {
        _kernel = kernel;
        _bridge = bridge;
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

        var kernel = builder.Build();
        var bridge = new GameToolBridge(kernel, options.ToolTypes ?? Array.Empty<Type>(), options.ToolInstances);

        return new LLMAgent(kernel, bridge);
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

    /// <summary>
    /// Registers all tool functions with an MCP server builder.
    /// Each function is wrapped to ensure invocations route through
    /// the Semantic Kernel pipeline, preserving thread safety.
    /// </summary>
    /// <param name="builder">The MCP server builder to register tools with.</param>
    public void RegisterToolsToMcp(IMcpServerBuilder builder)
    {
        for (int i = 0; i < _bridge.Functions.Count; i++)
        {
            var wrapper = new McpKernelFunctionWrapper(_bridge.Functions[i], _kernel);
            builder.Services.AddSingleton(_ => McpServerTool.Create(wrapper));
        }
    }
}

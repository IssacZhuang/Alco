using System.Collections.Generic;
using Alco.AgentControlProtocol;
using Microsoft.Extensions.AI;

namespace Alco.LLM;

/// <summary>
/// Microsoft.Extensions.AI adapter for <see cref="ToolRegistry"/>.
/// Creates <see cref="AITool"/> instances from registered tool descriptors.
/// </summary>
public static class ToolRegistryMEAIAdapter
{
    /// <summary>
    /// Creates a list of <see cref="AITool"/> from all registered tools.
    /// Tools are registered as metadata for the LLM; actual invocation goes through
    /// <see cref="ToolRegistry.InvokeToolAsync"/>.
    /// </summary>
    /// <param name="registry">The tool registry to create tools from.</param>
    /// <returns>A list of AI tools containing all registered tools.</returns>
    public static IList<AITool> ToAITools(this ToolRegistry registry)
    {
        var tools = new List<AITool>();

        foreach (var (name, descriptor) in registry.Tools)
        {
            var tool = AIFunctionFactory.Create(
                descriptor.Method,
                descriptor.Target,
                name: descriptor.Name,
                description: descriptor.Description,
                serializerOptions: descriptor.JsonOptions);

            tools.Add(tool);
        }

        return tools;
    }
}

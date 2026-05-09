using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Semantic Kernel adapter for <see cref="ToolRegistry"/>.
/// Creates <see cref="KernelPlugin"/> instances from registered tool descriptors.
/// </summary>
public static class ToolRegistrySemanticKernelAdapter
{
    /// <summary>
    /// Creates a <see cref="KernelPlugin"/> named "GameTools" from all registered tools.
    /// </summary>
    /// <param name="registry">The tool registry to create the plugin from.</param>
    /// <returns>A kernel plugin containing all registered tools.</returns>
    public static KernelPlugin ToKernelPlugin(this ToolRegistry registry)
    {
        var functions = new List<KernelFunction>();

        foreach (var (name, descriptor) in registry.Tools)
        {
            var function = KernelFunctionFactory.CreateFromMethod(
                method: descriptor.Method,
                target: descriptor.Target,
                description: descriptor.Description,
                functionName: descriptor.Name);

            functions.Add(function);
        }

        return KernelPluginFactory.CreateFromFunctions("GameTools", functions);
    }
}

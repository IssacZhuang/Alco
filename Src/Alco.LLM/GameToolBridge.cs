using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Discovers tool functions from game tool types and registers them
/// with a Semantic Kernel as plugins.
/// </summary>
public class GameToolBridge
{
    private readonly Kernel _kernel;

    /// <summary>
    /// Gets the list of kernel functions registered by this bridge.
    /// </summary>
    public IReadOnlyList<KernelFunction> Functions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameToolBridge"/> class.
    /// </summary>
    /// <param name="kernel">The semantic kernel to register plugins with.</param>
    /// <param name="toolTypes">The list of types marked with <see cref="GameToolAttribute"/>.</param>
    public GameToolBridge(Kernel kernel, IList<Type> toolTypes)
    {
        _kernel = kernel;

        var functions = new List<KernelFunction>();

        for (int i = 0; i < toolTypes.Count; i++)
        {
            var type = toolTypes[i];
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null);

            foreach (var method in methods)
            {
                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                var function = KernelFunctionFactory.CreateFromMethod(method, description: description);
                functions.Add(function);
            }
        }

        var plugin = KernelPluginFactory.CreateFromFunctions("GameTools", functions);
        _kernel.Plugins.Add(plugin);

        Functions = functions;
    }
}

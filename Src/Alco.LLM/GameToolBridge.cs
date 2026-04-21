using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Discovers tool functions from game tool types and instances, registers them
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
    /// <param name="toolTypes">The list of types whose static methods marked with <see cref="ToolFunctionAttribute"/> are discovered.</param>
    /// <param name="toolInstances">The list of instances whose instance and static methods marked with <see cref="ToolFunctionAttribute"/> are discovered.</param>
    public GameToolBridge(Kernel kernel, IList<Type> toolTypes, IList<object>? toolInstances = null)
    {
        _kernel = kernel;

        var functions = new List<KernelFunction>();

        // Discover static methods from tool types
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

        // Discover instance methods from tool instances
        if (toolInstances != null)
        {
            for (int i = 0; i < toolInstances.Count; i++)
            {
                var instance = toolInstances[i];
                var type = instance.GetType();
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null);

                foreach (var method in methods)
                {
                    var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description;
                    var function = KernelFunctionFactory.CreateFromMethod(method, instance, description: description);
                    functions.Add(function);
                }
            }
        }

        var plugin = KernelPluginFactory.CreateFromFunctions("GameTools", functions);
        _kernel.Plugins.Add(plugin);

        Functions = functions;
    }
}

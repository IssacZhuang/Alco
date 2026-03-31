using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Wraps a <see cref="KernelFunction"/> as an <see cref="AIFunction"/> that routes
/// invocations through the Semantic Kernel public pipeline. This ensures that
/// <see cref="Microsoft.SemanticKernel.IFunctionInvocationFilter"/> instances
/// are triggered for MCP calls.
/// </summary>
internal sealed class McpKernelFunctionWrapper : AIFunction
{
    private readonly KernelFunction _function;
    private readonly Kernel _kernel;
    private readonly JsonElement _cachedSchema;

    /// <inheritdoc/>
    public override string Name => _function.Name;

    /// <inheritdoc/>
    public override string Description => _function.Description ?? string.Empty;

    /// <inheritdoc/>
    public override JsonElement JsonSchema => _cachedSchema;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpKernelFunctionWrapper"/> class.
    /// </summary>
    /// <param name="function">The kernel function to wrap.</param>
    /// <param name="kernel">The kernel instance used for invocation.</param>
    public McpKernelFunctionWrapper(KernelFunction function, Kernel kernel)
    {
        _function = function;
        _kernel = kernel;
        _cachedSchema = BuildSchemaFromParameters();
    }

    /// <inheritdoc/>
    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var kernelArgs = MapToKernelArguments(arguments);
        var result = await _function.InvokeAsync(_kernel, kernelArgs, cancellationToken);
        return result.GetValue<object>();
    }

    private KernelArguments MapToKernelArguments(AIFunctionArguments arguments)
    {
        var kernelArgs = new KernelArguments();
        if (arguments != null)
        {
            foreach (var kvp in arguments)
            {
                kernelArgs[kvp.Key] = kvp.Value;
            }
        }
        return kernelArgs;
    }

    private JsonElement BuildSchemaFromParameters()
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        for (int i = 0; i < _function.Metadata.Parameters.Count; i++)
        {
            var param = _function.Metadata.Parameters[i];
            var propSchema = new Dictionary<string, object>
            {
                ["type"] = MapTypeToJsonSchemaType(param.ParameterType)
            };

            if (!string.IsNullOrEmpty(param.Description))
            {
                propSchema["description"] = param.Description;
            }

            properties[param.Name] = propSchema;

            if (param.IsRequired)
            {
                required.Add(param.Name);
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(schema));
    }

    private static string MapTypeToJsonSchemaType(Type? type)
    {
        if (type == null) return "string";

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            return "integer";
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return "number";
        if (type == typeof(bool))
            return "boolean";

        return "string";
    }
}

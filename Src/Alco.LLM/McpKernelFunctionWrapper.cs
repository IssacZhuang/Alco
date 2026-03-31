using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Wraps a <see cref="KernelFunction"/> as an <see cref="AIFunction"/> that routes
/// invocations through the Semantic Kernel pipeline. Uses the provided
/// <see cref="JsonSerializerOptions"/> for schema generation and argument deserialization,
/// enabling engine data types as tool parameters.
/// </summary>
internal sealed class McpKernelFunctionWrapper : AIFunction
{
    private readonly KernelFunction _function;
    private readonly Kernel _kernel;
    private readonly JsonSerializerOptions _jsonOptions;
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
    /// <param name="jsonOptions">The JSON serializer options with engine converters.</param>
    public McpKernelFunctionWrapper(KernelFunction function, Kernel kernel, JsonSerializerOptions jsonOptions)
    {
        _function = function;
        _kernel = kernel;
        _jsonOptions = jsonOptions;
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

    /// <summary>
    /// Maps MCP arguments to kernel arguments, deserializing <see cref="JsonElement"/>
    /// values to the target parameter types using the configured converters.
    /// </summary>
    private KernelArguments MapToKernelArguments(AIFunctionArguments arguments)
    {
        var kernelArgs = new KernelArguments();
        if (arguments != null)
        {
            for (int i = 0; i < _function.Metadata.Parameters.Count; i++)
            {
                var param = _function.Metadata.Parameters[i];
                if (arguments.TryGetValue(param.Name, out var value))
                {
                    kernelArgs[param.Name] = ConvertArgument(value, param.ParameterType);
                }
            }
        }
        return kernelArgs;
    }

    /// <summary>
    /// Converts a raw argument value to the target parameter type.
    /// Handles <see cref="JsonElement"/> deserialization using engine converters.
    /// </summary>
    private object? ConvertArgument(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;
        if (value is JsonElement jsonElement)
        {
            return JsonSerializer.Deserialize(jsonElement, targetType, _jsonOptions);
        }
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        return JsonSerializer.Deserialize(json, targetType, _jsonOptions);
    }

    /// <summary>
    /// Builds a JSON schema for the tool parameters using <see cref="JsonSchemaExporter"/>,
    /// which respects the configured converters and naming policy.
    /// </summary>
    private JsonElement BuildSchemaFromParameters()
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        for (int i = 0; i < _function.Metadata.Parameters.Count; i++)
        {
            var param = _function.Metadata.Parameters[i];
            var paramSchema = JsonSchemaExporter.GetJsonSchemaAsNode(_jsonOptions, param.ParameterType);

            if (paramSchema is JsonObject obj && !string.IsNullOrEmpty(param.Description))
            {
                obj["description"] = param.Description;
            }

            properties[param.Name] = paramSchema;

            if (param.IsRequired)
            {
                required.Add(param.Name);
            }
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.Deserialize<JsonElement>(schema.ToJsonString());
    }
}

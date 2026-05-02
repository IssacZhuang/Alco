using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Alco.LLM;

/// <summary>
/// Central registry for game tool functions. Discovers <see cref="ToolFunctionAttribute"/> methods
/// from <see cref="GameToolAttribute"/> types and instances. Provides unified invocation with
/// automatic thread marshaling for non-async-safe tools.
/// </summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDescriptor> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();

    /// <summary>
    /// Gets all registered tool descriptors.
    /// </summary>
    public IReadOnlyDictionary<string, ToolDescriptor> Tools => _tools;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolRegistry"/> class.
    /// Discovers tool functions from the provided types and instances.
    /// </summary>
    /// <param name="toolTypes">Types whose static methods marked with <see cref="ToolFunctionAttribute"/> are discovered.</param>
    /// <param name="toolInstances">Instances whose instance and static methods marked with <see cref="ToolFunctionAttribute"/> are discovered.</param>
    /// <param name="jsonOptions">The JSON serializer options for parameter deserialization.</param>
    public ToolRegistry(
        IList<Type> toolTypes,
        IList<object>? toolInstances,
        JsonSerializerOptions jsonOptions)
    {
        for (int i = 0; i < toolTypes.Count; i++)
        {
            DiscoverMethods(toolTypes[i], target: null, jsonOptions);
        }

        if (toolInstances != null)
        {
            for (int i = 0; i < toolInstances.Count; i++)
            {
                var instance = toolInstances[i];
                DiscoverMethods(instance.GetType(), target: instance, jsonOptions);
            }
        }
    }

    /// <summary>
    /// Gets a tool descriptor by name. Returns <c>null</c> if not found.
    /// </summary>
    /// <param name="name">The tool name (case-insensitive).</param>
    /// <returns>The tool descriptor, or <c>null</c>.</returns>
    public ToolDescriptor? GetTool(string name)
    {
        _tools.TryGetValue(name, out var descriptor);
        return descriptor;
    }

    /// <summary>
    /// Invokes a tool by name with the provided JSON arguments.
    /// Handles thread marshaling for non-async-safe tools.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <param name="jsonArgs">The JSON element containing arguments.</param>
    /// <returns>The invocation result.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the tool is not found.</exception>
    public async Task<object?> InvokeToolAsync(string name, JsonElement jsonArgs)
    {
        var descriptor = GetTool(name)
            ?? throw new KeyNotFoundException($"Tool '{name}' not found.");

        var args = DeserializeArguments(descriptor, jsonArgs);

        if (descriptor.IsAsyncSafe)
        {
            return InvokeDirect(descriptor, args);
        }

        return await InvokeOnMainThread(descriptor, args);
    }

    /// <summary>
    /// Drains the main thread queue. Must be called from the engine's main thread on each tick.
    /// </summary>
    public void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var action))
        {
            action();
        }
    }

    private void DiscoverMethods(Type type, object? target, JsonSerializerOptions jsonOptions)
    {
        var bindingFlags = target != null
            ? BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            : BindingFlags.Public | BindingFlags.Static;

        var methods = type.GetMethods(bindingFlags)
            .Where(m => m.GetCustomAttribute<ToolFunctionAttribute>() != null);

        foreach (var method in methods)
        {
            var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
            var attr = method.GetCustomAttribute<ToolFunctionAttribute>()!;
            var parameterSchema = BuildParameterSchema(method, jsonOptions);

            var descriptor = new ToolDescriptor(
                name: method.Name,
                description: description,
                parameterSchema: parameterSchema,
                returnType: method.ReturnType,
                isAsyncSafe: attr.AsyncSafe,
                method: method,
                target: target,
                jsonOptions: jsonOptions);

            _tools[method.Name] = descriptor;
        }
    }

    private static JsonElement BuildParameterSchema(MethodInfo method, JsonSerializerOptions jsonOptions)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var param in method.GetParameters())
        {
            var paramSchema = param.ParameterType != null
                ? JsonSchemaExporter.GetJsonSchemaAsNode(jsonOptions, param.ParameterType)
                : JsonSchemaExporter.GetJsonSchemaAsNode(jsonOptions, typeof(string));

            if (paramSchema is JsonObject obj && !string.IsNullOrEmpty(param.Name))
            {
                var paramDesc = param.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!string.IsNullOrEmpty(paramDesc))
                {
                    obj["description"] = paramDesc;
                }
            }

            if (!string.IsNullOrEmpty(param.Name))
            {
                properties[param.Name] = paramSchema;
            }

            if (!param.HasDefaultValue)
            {
                required.Add(param.Name ?? $"arg{required.Count}");
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

    private static object?[] DeserializeArguments(ToolDescriptor descriptor, JsonElement jsonArgs)
    {
        var parameters = descriptor.Method.GetParameters();
        var args = new object?[parameters.Length];

        if (jsonArgs.ValueKind == JsonValueKind.Object)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (jsonArgs.TryGetProperty(param.Name!, out var propValue))
                {
                    args[i] = JsonSerializer.Deserialize(propValue, param.ParameterType!, descriptor.JsonOptions);
                }
                else if (param.HasDefaultValue)
                {
                    args[i] = param.DefaultValue;
                }
                else
                {
                    args[i] = param.ParameterType!.IsValueType
                        ? Activator.CreateInstance(param.ParameterType)
                        : null;
                }
            }
        }
        else if (jsonArgs.ValueKind == JsonValueKind.Undefined || jsonArgs.ValueKind == JsonValueKind.Null)
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                args[i] = parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : parameters[i].ParameterType!.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType!)
                        : null;
            }
        }

        return args;
    }

    private static object? InvokeDirect(ToolDescriptor descriptor, object?[] args)
    {
        var result = descriptor.Method.Invoke(descriptor.Target, args);
        if (result is Task task)
        {
            task.Wait();
            var resultProperty = task.GetType().GetProperty("Result");
            return resultProperty?.GetValue(task);
        }
        return result;
    }

    private Task<object?> InvokeOnMainThread(ToolDescriptor descriptor, object?[] args)
    {
        var tcs = new TaskCompletionSource<object?>();
        _mainThreadQueue.Enqueue(() =>
        {
            try
            {
                var result = InvokeDirect(descriptor, args);
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}

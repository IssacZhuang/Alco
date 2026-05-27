using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;

namespace Alco.LLM;

/// <summary>
/// Describes a single tool function discovered from a <see cref="AgentToolsAttribute"/> type.
/// Contains all metadata needed to invoke the function, generate JSON schemas,
/// and register it with HTTP API adapters.
/// </summary>
public sealed class ToolDescriptor
{
    /// <summary>
    /// Gets the name of the tool (the method name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the description of the tool (from <see cref="System.ComponentModel.DescriptionAttribute"/>).
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the JSON schema for the tool's parameters.
    /// </summary>
    public JsonElement ParameterSchema { get; }

    /// <summary>
    /// Gets the return type of the tool method.
    /// </summary>
    public Type ReturnType { get; }

    /// <summary>
    /// Gets whether this tool is async-safe and can be invoked directly on the calling thread.
    /// When <c>true</c>, the tool is invoked directly. When <c>false</c>, the tool is
    /// marshaled to the engine main thread before invocation.
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// Gets the reflected method info for invocation.
    /// </summary>
    public MethodInfo Method { get; }

    /// <summary>
    /// Gets the target instance for instance methods, or <c>null</c> for static methods.
    /// </summary>
    public object? Target { get; }

    /// <summary>
    /// Gets the JSON serializer options used for parameter deserialization.
    /// </summary>
    public JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolDescriptor"/> class.
    /// </summary>
    public ToolDescriptor(
        string name,
        string description,
        JsonElement parameterSchema,
        Type returnType,
        bool isAsync,
        MethodInfo method,
        object? target,
        JsonSerializerOptions jsonOptions)
    {
        Name = name;
        Description = description;
        ParameterSchema = parameterSchema;
        ReturnType = returnType;
        IsAsync = isAsync;
        Method = method;
        Target = target;
        JsonOptions = jsonOptions;
    }
}

using System;
using System.Collections.Generic;
using Microsoft.SemanticKernel;

namespace Alco.LLM;

/// <summary>
/// Options for creating an <see cref="LLMAgent"/>.
/// </summary>
public record LLMAgentOptions
{
    /// <summary>
    /// Gets or initializes the endpoint URI for the LLM service.
    /// </summary>
    public required Uri Endpoint { get; init; }

    /// <summary>
    /// Gets or initializes the API key for authentication.
    /// </summary>
    public required string ApiKey { get; init; }

    /// <summary>
    /// Gets or initializes the model ID to use.
    /// </summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Gets or initializes the list of tool types marked with <see cref="GameToolAttribute"/>
    /// to register with the agent. These types' static methods are discovered.
    /// </summary>
    public IList<Type>? ToolTypes { get; init; }

    /// <summary>
    /// Gets or initializes the list of tool instances whose instance and static methods
    /// marked with <see cref="ToolFunctionAttribute"/> are registered with the agent.
    /// </summary>
    public IList<object>? ToolInstances { get; init; }

    /// <summary>
    /// Gets or initializes the optional function invocation filter.
    /// </summary>
    public IFunctionInvocationFilter? FunctionInvocationFilter { get; init; }
}

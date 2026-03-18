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
    /// Gets or initializes the list of plugin instances to add to the kernel.
    /// </summary>
    public IList<object>? Plugins { get; init; }

    /// <summary>
    /// Gets or initializes the list of plugin types to add to the kernel.
    /// </summary>
    public IList<Type>? PluginTypes { get; init; }

    /// <summary>
    /// Gets or initializes the optional function invocation filter.
    /// </summary>
    public IFunctionInvocationFilter? FunctionInvocationFilter { get; init; }
}

using System;

namespace Alco.LLM;

/// <summary>
/// Marks a method as a tool function invocable by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ToolFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or initializes whether this function is safe to invoke on any thread
    /// without marshaling to the main thread. Defaults to <c>false</c>.
    /// </summary>
    public bool AsyncSafe { get; init; }
}

using System;

namespace Alco.LLM;

/// <summary>
/// Marks a method as an agent function invocable by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AgentFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or initializes whether this function requires main-thread marshaling.
    /// Defaults to <c>false</c>, meaning the function is thread-safe and invoked directly.
    /// Set to <c>true</c> to marshal invocation to the main thread.
    /// </summary>
    public bool IsAsync { get; init; }
}

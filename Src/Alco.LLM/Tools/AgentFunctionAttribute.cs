using System;

namespace Alco.LLM;

/// <summary>
/// Marks a method as an agent function invocable by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AgentFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or initializes whether this function is async-safe and can be invoked
    /// directly on the calling thread. Defaults to <c>false</c>, meaning the function
    /// requires main-thread execution. Set to <c>true</c> for thread-safe functions
    /// that can run on the request thread.
    /// </summary>
    public bool IsAsync { get; init; }
}

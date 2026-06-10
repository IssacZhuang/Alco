using System;

namespace Alco.LLM;

/// <summary>
/// Marks a method as an agent function invocable by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AgentFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or initializes whether this function runs on the agent thread (background thread).
    /// Defaults to <c>false</c>, meaning the function is marshaled to the engine main thread
    /// for execution. Set to <c>true</c> for functions that don't touch game state and can
    /// run directly on the agent thread.
    /// </summary>
    public bool IsOnAgentThread { get; init; }
}

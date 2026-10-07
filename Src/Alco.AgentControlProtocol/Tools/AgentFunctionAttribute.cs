using System;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Marks a method as an agent function invocable by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AgentFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets or initializes whether this function runs on background thread pool threads.
    /// Defaults to <c>false</c>, meaning the function is marshaled to the engine main thread
    /// for execution. Set to <c>true</c> for functions that don't touch game state; such
    /// functions may execute concurrently with other tool calls (including other invocations
    /// of themselves), so they must not rely on shared mutable state.
    /// </summary>
    public bool IsOnAgentThread { get; init; }
}

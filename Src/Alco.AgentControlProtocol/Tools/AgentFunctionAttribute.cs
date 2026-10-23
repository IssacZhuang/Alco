using System;

namespace Alco.AgentControlProtocol;

/// <summary>
/// Marks a method as an agent function invocable by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class AgentFunctionAttribute : Attribute
{
    /// <summary>
    /// Gets whether this function runs on a background thread pool thread instead of the engine main thread.
    /// </summary>
    public bool IsOnAgentThread { get; init; }
}

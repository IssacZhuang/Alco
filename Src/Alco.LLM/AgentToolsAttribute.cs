using System;

namespace Alco.LLM;

/// <summary>
/// Marks a class as an agent tool whose methods can be invoked by LLM agents.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class AgentToolsAttribute : Attribute
{
}

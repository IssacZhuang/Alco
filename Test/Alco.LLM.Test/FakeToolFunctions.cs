using Alco.AgentControlProtocol;
using System.ComponentModel;

namespace Alco.LLM.Test;

/// <summary>
/// Static tool functions for testing tool discovery and invocation.
/// All methods are thread-safe and run directly on the agent thread.
/// </summary>
[AgentTools]
public static class FakeToolFunctions
{
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Adds two numbers")]
    public static int Add(int a, int b)
    {
        return a + b;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Echoes the message back")]
    public static string Echo(string message)
    {
        return message;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Always throws an error")]
    public static string ThrowError()
    {
        throw new InvalidOperationException("Test error");
    }
}

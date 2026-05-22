using System.ComponentModel;

namespace Alco.LLM.Test;

/// <summary>
/// Static tool functions for testing tool discovery and invocation.
/// All methods are marked AsyncSafe so they execute directly in unit tests
/// without needing main-thread marshaling.
/// </summary>
[GameTool]
public static class FakeToolFunctions
{
    [ToolFunction(AsyncSafe = true)]
    [Description("Adds two numbers")]
    public static int Add(int a, int b)
    {
        return a + b;
    }

    [ToolFunction(AsyncSafe = true)]
    [Description("Echoes the message back")]
    public static string Echo(string message)
    {
        return message;
    }

    [ToolFunction(AsyncSafe = true)]
    [Description("Always throws an error")]
    public static string ThrowError()
    {
        throw new InvalidOperationException("Test error");
    }
}

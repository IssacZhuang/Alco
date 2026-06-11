using System.ComponentModel;

namespace Alco.LLM.Test;

/// <summary>
/// Additional tool functions for invocation reliability tests.
/// </summary>
[AgentTools]
public static class FakeAdvancedToolFunctions
{
    public static int CallCount { get; private set; }

    public static void Reset()
    {
        CallCount = 0;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Adds two numbers")]
    public static int Add(int a, int b)
    {
        CallCount++;
        return a + b;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes without returning a value")]
    public static string Complete()
    {
        CallCount++;
        return "done";
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Always throws an error")]
    public static string Throw()
    {
        throw new InvalidOperationException("Test error");
    }

    [AgentFunction]
    [Description("Waits before returning")]
    public static string Slow(int milliseconds)
    {
        CallCount++;
        Thread.Sleep(milliseconds);
        return "done";
    }

    [AgentFunction]
    [Description("Adds two numbers on the main thread")]
    public static int MainThreadAdd(int a, int b)
    {
        CallCount++;
        return a + b;
    }
}

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
    [Description("Adds two numbers asynchronously")]
    public static async Task<int> AddAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes an async task without returning a value")]
    public static async Task CompleteAsync()
    {
        await Task.Yield();
        CallCount++;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Throws asynchronously")]
    public static async Task<string> ThrowAsync()
    {
        await Task.Yield();
        throw new InvalidOperationException("Async test error");
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Waits before returning")]
    public static async Task<string> SlowAsync(int milliseconds)
    {
        CallCount++;
        await Task.Delay(milliseconds);
        return "done";
    }

    [AgentFunction]
    [Description("Adds two numbers on the main thread")]
    public static int MainThreadAdd(int a, int b)
    {
        CallCount++;
        return a + b;
    }

    [AgentFunction]
    [Description("Adds two numbers asynchronously on the main thread")]
    public static async Task<int> MainThreadAddAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }
}

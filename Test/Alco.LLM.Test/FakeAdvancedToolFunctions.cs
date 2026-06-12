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
    [Description("Adds two numbers asynchronously")]
    public static async Task<int> AddAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes asynchronously without a value")]
    public static async Task CompleteTaskAsync()
    {
        await Task.Yield();
        CallCount++;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Adds two numbers asynchronously using ValueTask")]
    public static async ValueTask<int> AddValueTaskAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes asynchronously using ValueTask")]
    public static async ValueTask CompleteValueTaskAsync()
    {
        await Task.Yield();
        CallCount++;
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

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Returns binary image data")]
    public static BinaryToolResult GetBinaryImage()
    {
        CallCount++;
        return new BinaryToolResult(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            "image/png",
            "test.png",
            new Dictionary<string, string>
            {
                ["X-Test-Width"] = "1",
            });
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

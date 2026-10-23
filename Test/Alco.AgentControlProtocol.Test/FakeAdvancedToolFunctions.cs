using System.ComponentModel;

namespace Alco.AgentControlProtocol.Test;

/// <summary>
/// Additional tool functions for invocation reliability tests.
/// </summary>
[AgentTools]
public static class FakeAdvancedToolFunctions
{
    /// <summary>
    /// Gets the number of tool invocations since the last reset.
    /// </summary>
    public static int CallCount { get; private set; }

    /// <summary>
    /// Resets the invocation counter.
    /// </summary>
    public static void Reset()
    {
        CallCount = 0;
    }

    /// <summary>
    /// Adds two numbers and counts the call.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Adds two numbers")]
    public static int Add(int a, int b)
    {
        CallCount++;
        return a + b;
    }

    /// <summary>
    /// Returns a completion marker and counts the call.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes without returning a value")]
    public static string Complete()
    {
        CallCount++;
        return "done";
    }

    /// <summary>
    /// Adds two numbers asynchronously and counts the call.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Adds two numbers asynchronously")]
    public static async Task<int> AddAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }

    /// <summary>
    /// Completes asynchronously and counts the call.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes asynchronously without a value")]
    public static async Task CompleteTaskAsync()
    {
        await Task.Yield();
        CallCount++;
    }

    /// <summary>
    /// Adds two numbers asynchronously using a <see cref="ValueTask"/> and counts the call.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Adds two numbers asynchronously using ValueTask")]
    public static async ValueTask<int> AddValueTaskAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }

    /// <summary>
    /// Completes asynchronously using a <see cref="ValueTask"/> and counts the call.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Completes asynchronously using ValueTask")]
    public static async ValueTask CompleteValueTaskAsync()
    {
        await Task.Yield();
        CallCount++;
    }

    /// <summary>
    /// Throws an exception to simulate a failing tool.
    /// </summary>
    [AgentFunction(IsOnAgentThread = true)]
    [Description("Always throws an error")]
    public static string Throw()
    {
        throw new InvalidOperationException("Test error");
    }

    /// <summary>
    /// Waits for the given duration before returning.
    /// </summary>
    [AgentFunction]
    [Description("Waits before returning")]
    public static string Slow(int milliseconds)
    {
        CallCount++;
        Thread.Sleep(milliseconds);
        return "done";
    }

    /// <summary>
    /// Adds two numbers on the main thread and counts the call.
    /// </summary>
    [AgentFunction]
    [Description("Adds two numbers on the main thread")]
    public static int MainThreadAdd(int a, int b)
    {
        CallCount++;
        return a + b;
    }

    /// <summary>
    /// Returns a binary image result and counts the call.
    /// </summary>
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

    /// <summary>
    /// Adds two numbers asynchronously on the main thread and counts the call.
    /// </summary>
    [AgentFunction]
    [Description("Adds two numbers asynchronously on the main thread")]
    public static async Task<int> MainThreadAddAsync(int a, int b)
    {
        await Task.Yield();
        CallCount++;
        return a + b;
    }
}

using Alco.AgentControlProtocol;
using System.ComponentModel;

namespace Alco.LLM.Test;

/// <summary>
/// Tool functions for parallel tool execution tests.
/// Tracks concurrency and execution order across invocations.
/// </summary>
[AgentTools]
public static class FakeParallelToolFunctions
{
    private static readonly object Lock = new();
    private static readonly List<string> Log = new();
    private static CountdownEvent _rendezvous = new(2);
    private static int _activeCount;
    private static int _maxActiveCount;

    /// <summary>
    /// Gets the maximum number of tracked tools that were executing simultaneously.
    /// </summary>
    public static int MaxActiveCount
    {
        get
        {
            lock (Lock)
            {
                return _maxActiveCount;
            }
        }
    }

    /// <summary>
    /// Gets a snapshot of the enter/exit execution log.
    /// </summary>
    public static IReadOnlyList<string> ExecutionLog
    {
        get
        {
            lock (Lock)
            {
                return Log.ToList();
            }
        }
    }

    /// <summary>
    /// Resets all tracked state between tests.
    /// </summary>
    public static void Reset()
    {
        lock (Lock)
        {
            Log.Clear();
            _activeCount = 0;
            _maxActiveCount = 0;
        }

        _rendezvous.Dispose();
        _rendezvous = new CountdownEvent(2);
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Signals arrival and waits for a second concurrent call")]
    public static string Rendezvous(string id)
    {
        _rendezvous.Signal();
        bool bothArrived = _rendezvous.Wait(TimeSpan.FromSeconds(5));
        return bothArrived ? $"{id}:parallel" : $"{id}:serial";
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Tracks concurrency while sleeping on a thread pool thread")]
    public static string Tracked(string id, int milliseconds)
    {
        Enter(id);
        Thread.Sleep(milliseconds);
        Exit(id);
        return id;
    }

    [AgentFunction]
    [Description("Tracks concurrency while executing on the main thread")]
    public static string MainTracked(string id)
    {
        Enter(id);
        Exit(id);
        return id;
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Sleeps on a thread pool thread without tracking")]
    public static string AgentSlow(int milliseconds)
    {
        Thread.Sleep(milliseconds);
        return "slow-done";
    }

    [AgentFunction(IsOnAgentThread = true)]
    [Description("Always throws an error")]
    public static string AgentThrow()
    {
        throw new InvalidOperationException("Parallel test error");
    }

    private static void Enter(string id)
    {
        lock (Lock)
        {
            _activeCount++;
            _maxActiveCount = Math.Max(_maxActiveCount, _activeCount);
            Log.Add($"enter:{id}");
        }
    }

    private static void Exit(string id)
    {
        lock (Lock)
        {
            _activeCount--;
            Log.Add($"exit:{id}");
        }
    }
}

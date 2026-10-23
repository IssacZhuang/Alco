global using NUnit.Framework;
global using TestFramework;
using System.Diagnostics;

/// <summary>Adds a console trace listener for the test assembly.</summary>
[SetUpFixture]
public class SetupTrace
{
    /// <summary>Starts capturing trace output.</summary>
    [OneTimeSetUp]
    public void StartTest()
    {
        Trace.Listeners.Add(new ConsoleTraceListener());
    }

    /// <summary>Flushes pending trace output.</summary>
    [OneTimeTearDown]
    public void EndTest()
    {
        Trace.Flush();
    }
}
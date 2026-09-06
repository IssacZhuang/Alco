using System;
using System.Threading.Tasks;
using Alco.Engine;
using NUnit.Framework;

namespace Alco.AgentControlProtocol.Test;

/// <summary>
/// Host globals for the factory-based tests; public by Roslyn scripting constraint.
/// </summary>
public sealed class HostGlobals
{
    public readonly GameEngine Engine;

    public readonly string Label = "host-label";

    public HostGlobals(GameEngine engine)
    {
        Engine = engine;
    }
}

[TestFixture]
public class ScriptToolTests
{
    private ScriptEngineHarness _harness = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _harness = new ScriptEngineHarness();
        _harness.Run();
    }

    [Test]
    public void ExecuteScript_BasicReturn_IsStringified()
    {
        Assert.That(_harness.Basic, Is.EqualTo("42"));
    }

    [Test]
    public void ExecuteScript_DefaultGlobals_ExposesEngineInstance()
    {
        Assert.That(_harness.DefaultEngineType, Is.EqualTo("ScriptEngineHarness"));
    }

    [Test]
    public void ExecuteScript_HostGlobals_ExposesCustomMembersAndEngine()
    {
        Assert.That(_harness.HostGlobalsLabel, Is.EqualTo("host-label"));
        Assert.That(_harness.HostGlobalsEngineType, Is.EqualTo("ScriptEngineHarness"));
    }

    [Test]
    public void ExecuteScript_FactoryReturnsNull_FallsBackToDefaultGlobals()
    {
        // The default ScriptGlobals has no Label member, so compiling `return Label;`
        // fails only when the null factory result fell back to the default globals.
        Assert.That(_harness.FactoryNullLabelError, Does.StartWith("Compilation error:"));
        Assert.That(_harness.FactoryNullEngineType, Is.EqualTo("ScriptEngineHarness"));
    }

    [Test]
    public void ExecuteScript_NonPublicGlobalsType_ReportsCompilationError()
    {
        Assert.That(_harness.NonPublicGlobals, Does.StartWith("Compilation error:"));
    }

    [Test]
    public void ExecuteScript_EmptyCode_ReportsNoCode()
    {
        Assert.That(_harness.EmptyCode, Is.EqualTo("No code provided."));
    }

    [Test]
    public void ExecuteScript_NoFailure()
    {
        Assert.That(_harness.Failure, Is.Null);
    }

    /// <summary>
    /// Drives one engine whose OnStart runs a battery of scripts on a background task
    /// (mirroring the agent thread); the engine loop pumps the main-thread queue the
    /// script tool posts to, and stops once the battery completes.
    /// </summary>
    private sealed class ScriptEngineHarness : GameEngine
    {
        private Task? _battery;
        private volatile bool _done;

        public string? Basic { get; private set; }
        public string? DefaultEngineType { get; private set; }
        public string? HostGlobalsLabel { get; private set; }
        public string? HostGlobalsEngineType { get; private set; }
        public string? FactoryNullLabelError { get; private set; }
        public string? FactoryNullEngineType { get; private set; }
        public string? NonPublicGlobals { get; private set; }
        public string? EmptyCode { get; private set; }
        public Exception? Failure { get; private set; }

        public ScriptEngineHarness()
            : base(GameEngineSetting.CreateNoGPU())
        {
        }

        protected override void OnStart()
        {
            base.OnStart();
            _battery = Task.Run(RunBattery);
        }

        protected override void OnUpdate(float delta)
        {
            base.OnUpdate(delta);
            if (_done)
            {
                Stop();
            }
        }

        private async Task RunBattery()
        {
            ScriptTool defaultTool = new(this);
            ScriptTool hostTool = new(this, () => new HostGlobals(this));
            ScriptTool nullFactoryTool = new(this, () => null);
            ScriptTool nonPublicTool = new(this, () => new PrivateGlobals());

            try
            {
                Basic = await defaultTool.ExecuteScript("return 6 * 7;");
                DefaultEngineType = await defaultTool.ExecuteScript("return Engine.GetType().Name;");
                HostGlobalsLabel = await hostTool.ExecuteScript("return Label;");
                HostGlobalsEngineType = await hostTool.ExecuteScript("return Engine.GetType().Name;");
                FactoryNullLabelError = await nullFactoryTool.ExecuteScript("return Label;");
                FactoryNullEngineType = await nullFactoryTool.ExecuteScript("return Engine.GetType().Name;");
                NonPublicGlobals = await nonPublicTool.ExecuteScript("return X;");
                EmptyCode = await defaultTool.ExecuteScript("");
            }
            catch (Exception ex)
            {
                Failure = ex;
            }
            finally
            {
                _done = true;
            }
        }

        private sealed class PrivateGlobals
        {
            public readonly int X = 1;
        }
    }
}

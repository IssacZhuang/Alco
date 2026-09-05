using System.Collections.Concurrent;
using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles.Test;

/// <summary>
/// Concurrency tests of the 2D/3D particle systems on the no-GPU backend:
/// concurrent instance churn (create + dispose) from worker threads while the
/// frame simulation steps on the test thread must never corrupt the shared
/// bookkeeping — no exceptions, no leaked pool slots, the instance registry
/// drained, the pending-kill queue emptied by a final frame. The churn also
/// forces pool growth (the Reallocated callback re-entering the shared gate)
/// and exercises the concurrent first-use material compile (the double-checked
/// cache's loser-disposal path).
/// </summary>
public class TestParticleThreadSafety
{
    /// <summary>The minimal engine host (real shader compilation on the no-GPU backend).</summary>
    public class Host(GameEngineSetting setting) : GameEngine(setting);

    private Host _engine = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        _engine = new Host(GameEngineSetting.CreateNoGPU());
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _engine.Dispose();
    }

    [Test(Description = "2D: concurrent create/dispose churn from 4 workers while the frame simulation steps stays consistent")]
    public void ConcurrentChurnWithFrameSimulation2D()
    {
        // 512 particles / 128-particle slices: eight concurrently held instances
        // force pool growth mid-churn (growth raises Reallocated with the shared
        // gate held — the reentrancy the shared gate exists for).
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 512, emitterSlots: 64);
        system.CreateInstance(BuildEffect2D("warmup"), new Transform2D(Vector2.Zero)).Dispose();
        using GPUCommandBuffer commands = BeginCommands();
        RunChurn(
            commands,
            () => system.RecordSimulation(commands, 1f / 60f),
            () => system.CreateInstance(BuildEffect2D("churn"), new Transform2D(Vector2.Zero)),
            () => system.Pool.AllocatedSlotCount,
            () => system.Instances.Count,
            () => system.Pool.PendingKills.Count);
    }

    [Test(Description = "3D: concurrent create/dispose churn from 4 workers while the frame simulation steps stays consistent")]
    public void ConcurrentChurnWithFrameSimulation3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, particleCapacity: 512, emitterSlots: 64);
        system.CreateInstance(BuildEffect3D("warmup"), new Transform3D(Vector3.Zero)).Dispose();
        using GPUCommandBuffer commands = BeginCommands();
        RunChurn(
            commands,
            () => system.RecordSimulation(commands, 1f / 60f),
            () => system.CreateInstance(BuildEffect3D("churn"), new Transform3D(Vector3.Zero)),
            () => system.Pool.AllocatedSlotCount,
            () => system.Instances.Count,
            () => system.Pool.PendingKills.Count);
    }

    [Test(Description = "2D: simultaneous first-use creation of the same group races the double-checked material cache")]
    public void ConcurrentFirstUseMaterialCompile2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 4096, emitterSlots: 64);
        // One shared asset: the first simultaneous creations all miss the material
        // cache, compile concurrently off the gate, and the losers must dispose
        // their duplicates cleanly.
        ParticleEffect2D effect = BuildEffect2D("first-use");
        var exceptions = new ConcurrentQueue<Exception>();
        var instances = new ConcurrentQueue<ParticleEffectInstance2D>();
        var start = new Barrier(4);
        var workers = new Task[4];
        for (int w = 0; w < workers.Length; w++)
        {
            workers[w] = Task.Run(() =>
            {
                start.SignalAndWait();
                try
                {
                    for (int i = 0; i < 25; i++)
                    {
                        instances.Enqueue(system.CreateInstance(effect, new Transform2D(Vector2.Zero)));
                    }
                }
                catch (Exception e)
                {
                    exceptions.Enqueue(e);
                }
            });
        }
        Task.WaitAll(workers);

        Assert.That(exceptions, Is.Empty, "a concurrent first-use creation failed");
        Assert.That(instances, Has.Count.EqualTo(100), "instances were lost to the material-cache race");
        foreach (ParticleEffectInstance2D instance in instances)
        {
            instance.Dispose();
        }
        Assert.That(system.Pool.AllocatedSlotCount, Is.EqualTo(0), "slots leaked through the concurrent first-use path");
    }

    /// <summary>
    /// The shared churn harness: four workers loop create-hold-dispose batches
    /// while the test thread steps the frame simulation until they finish, then
    /// a few more frames drain the pending kills and the consistency invariants
    /// are asserted.
    /// </summary>
    private static void RunChurn<TInstance>(
        GPUCommandBuffer commands,
        Action simulateFrame,
        Func<TInstance> createInstance,
        Func<int> poolSlotProbe,
        Func<int> instanceProbe,
        Func<int> killsProbe)
        where TInstance : IDisposable
    {
        var exceptions = new ConcurrentQueue<Exception>();
        var workers = new Task[4];
        for (int w = 0; w < workers.Length; w++)
        {
            workers[w] = Task.Run(() =>
            {
                try
                {
                    for (int batch = 0; batch < 12; batch++)
                    {
                        // Hold a batch of instances live across frames (the frame
                        // simulation must iterate them safely), then dispose them
                        // concurrently with the next frames.
                        var held = new List<TInstance>(8);
                        for (int i = 0; i < 8; i++)
                        {
                            held.Add(createInstance());
                        }
                        Thread.Sleep(1);
                        foreach (TInstance instance in held)
                        {
                            instance.Dispose();
                        }
                    }
                }
                catch (Exception e)
                {
                    exceptions.Enqueue(e);
                }
            });
        }

        while (!Task.WaitAll(workers, TimeSpan.FromMilliseconds(16)))
        {
            simulateFrame();
        }
        for (int i = 0; i < 4; i++)
        {
            simulateFrame(); // drain the pending-kill queue
        }
        GC.KeepAlive(commands);

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty, "the concurrent churn threw");
            Assert.That(instanceProbe(), Is.EqualTo(0), "live instances remained after the churn");
            Assert.That(poolSlotProbe(), Is.EqualTo(0), "pool slots leaked through the churn");
            Assert.That(killsProbe(), Is.EqualTo(0), "pending kills survived the drain frames");
        });
    }

    private GPUCommandBuffer BeginCommands()
    {
        GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        return commands;
    }

    private static ParticleEffect2D BuildEffect2D(string name)
        => new()
        {
            Name = name,
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = $"{name}-group",
                    MaxParticles = 128,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(1f, 2f),
                },
            ],
        };

    private static ParticleEffect3D BuildEffect3D(string name)
        => new()
        {
            Name = name,
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = $"{name}-group",
                    MaxParticles = 128,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(1f, 2f),
                },
            ],
        };
}

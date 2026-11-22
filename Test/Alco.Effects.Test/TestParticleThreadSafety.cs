using System.Collections.Concurrent;
using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;

namespace Alco.Effects.Test;

/// <summary>Tests that concurrent instance churn and first-use material compilation stay consistent in the 2D and 3D particle systems.</summary>
public class TestParticleThreadSafety
{
    /// <summary>The minimal engine host (real shader compilation on the no-GPU backend).</summary>
    public class Host(GameEngineSetting setting) : GameEngine(setting);

    private Host _engine = null!;

    /// <summary>Creates the shared no-GPU engine host.</summary>
    [OneTimeSetUp]
    public void SetUp() => _engine = new Host(GameEngineSetting.CreateNoGPU());

    /// <summary>Disposes the shared engine host.</summary>
    [OneTimeTearDown]
    public void TearDown() => _engine.Dispose();

    /// <summary>Tests concurrent instance churn while frames simulate in 2D.</summary>
    [Test(Description = "2D: concurrent create/dispose churn from 4 workers while the frame simulation steps stays consistent")]
    public void ConcurrentChurnWithFrameSimulation2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.ShaderSystem, particleCapacity: 512, emitterSlots: 64);
        system.CreateInstance(BuildEffect2D("warmup"), new Transform2D(Vector2.Zero)).Dispose();
        RenderContext context = new(_engine.RenderingSystem);
        try
        {
            RunChurn(
                () =>
                {
                    context.BeginFrame();
                    system.RecordSimulation(context, 1f / 60f);
                    context.Submit().Wait();
                },
                () => system.CreateInstance(BuildEffect2D("churn"), new Transform2D(Vector2.Zero)),
                () => system.Pool.AllocatedSlotCount,
                () => system.Instances.Count,
                () => system.Pool.PendingKills.Count);
        }
        finally
        {
            context.Destroy();
        }
    }

    /// <summary>Tests concurrent instance churn while frames simulate in 3D.</summary>
    [Test(Description = "3D: concurrent create/dispose churn from 4 workers while the frame simulation steps stays consistent")]
    public void ConcurrentChurnWithFrameSimulation3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, _engine.ShaderSystem, particleCapacity: 512, emitterSlots: 64);
        system.CreateInstance(BuildEffect3D("warmup"), new Transform3D(Vector3.Zero)).Dispose();
        RenderContext context = new(_engine.RenderingSystem);
        try
        {
            RunChurn(
                () =>
                {
                    context.BeginFrame();
                    system.RecordSimulation(context, 1f / 60f);
                    context.Submit().Wait();
                },
                () => system.CreateInstance(BuildEffect3D("churn"), new Transform3D(Vector3.Zero)),
                () => system.Pool.AllocatedSlotCount,
                () => system.Instances.Count,
                () => system.Pool.PendingKills.Count);
        }
        finally
        {
            context.Destroy();
        }
    }

    /// <summary>Tests concurrent first-use material compilation in 2D.</summary>
    [Test(Description = "2D: simultaneous first-use creation of the same group races the double-checked material cache")]
    public void ConcurrentFirstUseMaterialCompile2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.ShaderSystem, particleCapacity: 4096, emitterSlots: 64);
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
                        instances.Enqueue(system.CreateInstance(effect, new Transform2D(Vector2.Zero)));
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
            instance.Dispose();
        Assert.That(system.Pool.AllocatedSlotCount, Is.EqualTo(0), "slots leaked through the concurrent first-use path");
    }

    private static void RunChurn<TInstance>(
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
                        var held = new List<TInstance>(8);
                        for (int i = 0; i < 8; i++)
                            held.Add(createInstance());
                        Thread.Sleep(1);
                        foreach (TInstance instance in held)
                            instance.Dispose();
                    }
                }
                catch (Exception e)
                {
                    exceptions.Enqueue(e);
                }
            });
        }

        while (!Task.WaitAll(workers, TimeSpan.FromMilliseconds(16)))
            simulateFrame();
        for (int i = 0; i < 4; i++)
            simulateFrame();

        Assert.Multiple(() =>
        {
            Assert.That(exceptions, Is.Empty, "the concurrent churn threw");
            Assert.That(instanceProbe(), Is.EqualTo(0), "live instances remained after the churn");
            Assert.That(poolSlotProbe(), Is.EqualTo(0), "pool slots leaked through the churn");
            Assert.That(killsProbe(), Is.EqualTo(0), "pending kills survived the drain frames");
        });
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

using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;

namespace Alco.Particles.Test;

/// <summary>
/// Simulation-frame tests of the 2D and 3D systems on the no-GPU backend: the
/// per-frame draw plan must include a brand-new material's groups on their very
/// first frame, and a hitch-sized frame delta must clamp instead of
/// fast-forwarding one-shot effects to death.
/// </summary>
public class TestParticleSimulation
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

    [Test(Description = "A brand-new material's groups join the draw plan on their first simulated frame")]
    public void NewMaterialDrawsOnItsFirstFrame2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D(), CreateGroup2D());
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 60f);

        Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(2),
            "the effect's debut frame dropped out of the draw plan, so it neither dispatched nor drew");
    }

    [Test(Description = "The 3D draw plan also includes brand-new materials on their first simulated frame")]
    public void NewMaterialDrawsOnItsFirstFrame3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        ParticleEffect3DAsset effect = CreateEffect3D(CreateGroup3D());
        using ParticleEffectInstance3D instance = system.CreateInstance(effect, new Transform3D(new System.Numerics.Vector3(0f, 0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 60f);

        Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(1),
            "the effect's debut frame dropped out of the draw plan, so it neither dispatched nor drew");
    }

    [Test(Description = "A hitch-sized frame delta clamps instead of fast-forwarding a one-shot to death")]
    public void HitchDeltaClamps2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D(duration: 1f, looping: false));
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 10f);

        Assert.Multiple(() =>
        {
            Assert.That(instance.IsActive, Is.True, "an unclamped 10 s step deactivated the one-shot in its first frame");
            Assert.That(instance.Time, Is.EqualTo(ParticleEmission.MaxDeltaTime).Within(0.0001f),
                "the emission timeline advanced by the raw hitch delta");
            Assert.That(instance.GetGroupParams(0).DeltaTime, Is.EqualTo(ParticleEmission.MaxDeltaTime).Within(0.0001f),
                "the GPU simulate step would age every particle by the raw hitch delta");
        });
    }

    [Test(Description = "The rate limiter skips frames below the interval, then steps by the accumulated time")]
    public void RateLimitSkipsAndAccumulates2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationInterval = 1f / 64f;
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D());
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 256f); // the debut frame always simulates
        float debutTime = instance.Time;
        for (int i = 0; i < 3; i++)
        {
            system.RecordSimulation(commands, 1f / 256f);
        }

        Assert.Multiple(() =>
        {
            Assert.That(instance.Time, Is.EqualTo(debutTime), "frames below the interval advanced the emission timeline");
            Assert.That(instance.GetGroupParams(0).DeltaTime, Is.EqualTo(1f / 256f),
                "a skipped frame overwrote the GPU time step of the last simulated frame");
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(1), "a skipped frame dropped the cached draw plan");
        });

        system.RecordSimulation(commands, 1f / 256f); // the accumulator reaches the interval here
        Assert.Multiple(() =>
        {
            Assert.That(instance.Time, Is.EqualTo(debutTime + 1f / 64f).Within(0.000001f),
                "the accumulated step did not advance the timeline by the whole accumulated time");
            Assert.That(instance.GetGroupParams(0).DeltaTime, Is.EqualTo(1f / 64f).Within(0.000001f),
                "the accumulated time did not reach the GPU simulate step");
        });
    }

    [Test(Description = "Rate-limited steps are one fixed interval each; the accumulator carries the remainder")]
    public void RateLimitStepsAreFixedAndCarryRemainder2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationInterval = 1f / 64f; // 4/256 — all deltas below are dyadic, so the float math is exact
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D());
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 128f); // the debut frame always simulates
        float debutTime = instance.Time;

        system.RecordSimulation(commands, 1f / 128f); // acc 2/256: skips
        system.RecordSimulation(commands, 3f / 256f); // acc 5/256: one 4/256 step, 1/256 remains
        Assert.That(instance.GetGroupParams(0).DeltaTime, Is.EqualTo(1f / 64f),
            "the step used the variable accumulated delta instead of one fixed interval");

        system.RecordSimulation(commands, 3f / 256f); // carried 1/256 + 3/256: steps again
        Assert.That(instance.Time, Is.EqualTo(debutTime + 2f / 64f),
            "without the carried remainder this frame would have skipped (3/256 below the 4/256 interval)");
    }

    [Test(Description = "A hitch with a valid plan simulates one interval and discards the backlog instead of fast-forwarding")]
    public void RateLimitedHitchDiscardsBacklog2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationInterval = 1f / 64f;
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D());
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 64f); // the debut frame always simulates
        float debutTime = instance.Time;

        system.RecordSimulation(commands, 10f);
        Assert.Multiple(() =>
        {
            Assert.That(instance.Time, Is.EqualTo(debutTime + 1f / 64f),
                "the hitch advanced the timeline by more than one interval (fast-forward)");
            Assert.That(instance.GetGroupParams(0).DeltaTime, Is.EqualTo(1f / 64f),
                "the hitch reached the GPU simulate step as more than one interval (fast-forward)");
        });
    }

    [Test(Description = "With the rate limiter off every frame simulates, however small the delta")]
    public void RateLimitDisabledSimulatesEveryFrame2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationRateLimitEnabled = false;
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D());
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 256f);
        system.RecordSimulation(commands, 1f / 256f);

        Assert.That(instance.Time, Is.EqualTo(2f / 256f).Within(0.000001f),
            "a disabled limiter still skipped a sub-interval frame");
    }

    [Test(Description = "Disposing an actively drawing instance forces a resimulation instead of replaying its stale draw")]
    public void ActiveDisposalForcesResimulation2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationInterval = 1f / 64f;
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D());
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 64f);
        Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(1));

        instance.Dispose();
        system.RecordSimulation(commands, 1f / 256f); // below the interval, but the cached plan is invalid
        Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(0),
            "the stale draw of a disposed active instance would have replayed for up to one interval");
    }

    [Test(Description = "Reaping a finished one-shot does not force a resimulation (its last draw is empty anyway)")]
    public void FinishedOneShotDisposalKeepsTheRateLimit2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationInterval = 1f / 64f;
        var group = new ParticleGroup2DAsset
        {
            Name = "TestGroup",
            MaxParticles = 128,
            EmissionRate = 0f,
            Duration = 1f / 64f,
            Looping = false,
            Lifetime = new ParticleRange(0.05f, 0.05f),
        };
        ParticleEffect2DAsset effect = CreateEffect2D(group);
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        for (int i = 0; i < 32 && instance.IsActive; i++)
        {
            system.RecordSimulation(commands, 1f / 64f); // the delta matches the interval: every call steps
        }
        Assert.That(instance.IsActive, Is.False, "the one-shot never deactivated");

        instance.Dispose(); // queues the slice kill
        system.RecordSimulation(commands, 1f / 256f); // below the interval: must skip, leaving the kill pending
        Assert.That(system.Pool.PendingKills, Has.Count.EqualTo(1),
            "disposing a finished one-shot forced a resimulation, defeating the rate limit during one-shot-heavy play");
        for (int i = 0; i < 4; i++)
        {
            system.RecordSimulation(commands, 1f / 256f);
        }
        Assert.That(system.Pool.PendingKills, Is.Empty, "the pending kill never dispatched");
    }

    [Test(Description = "The 3D system rate-limits identically: sub-interval frames skip, then the accumulated time steps")]
    public void RateLimitSkipsAndAccumulates3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        system.SimulationInterval = 1f / 64f;
        ParticleEffect3DAsset effect = CreateEffect3D(CreateGroup3D());
        using ParticleEffectInstance3D instance = system.CreateInstance(effect, new Transform3D(new System.Numerics.Vector3(0f, 0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 256f); // the debut frame always simulates
        float debutTime = instance.Time;
        for (int i = 0; i < 3; i++)
        {
            system.RecordSimulation(commands, 1f / 256f);
        }

        Assert.Multiple(() =>
        {
            Assert.That(instance.Time, Is.EqualTo(debutTime), "frames below the interval advanced the emission timeline");
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(1), "a skipped frame dropped the cached draw plan");
        });

        system.RecordSimulation(commands, 1f / 256f); // the accumulator reaches the interval here
        Assert.Multiple(() =>
        {
            Assert.That(instance.Time, Is.EqualTo(debutTime + 1f / 64f).Within(0.000001f),
                "the accumulated step did not advance the timeline by the whole accumulated time");
            Assert.That(instance.GetGroupParams(0).DeltaTime, Is.EqualTo(1f / 64f).Within(0.000001f),
                "the accumulated time did not reach the GPU simulate step");
        });
    }

    private static ParticleGroup2DAsset CreateGroup2D(float duration = 0f, bool looping = true)
        => new()
        {
            Name = "TestGroup",
            MaxParticles = 128,
            EmissionRate = 100f,
            Duration = duration,
            Looping = looping,
            Lifetime = new ParticleRange(1f, 2f),
        };

    private static ParticleEffect2DAsset CreateEffect2D(params ParticleGroup2DAsset[] groups)
        => new() { Name = "TestEffect", Groups = [.. groups] };

    private static ParticleGroup3DAsset CreateGroup3D()
        => new()
        {
            Name = "TestGroup",
            MaxParticles = 128,
            EmissionRate = 100f,
            Lifetime = new ParticleRange(1f, 2f),
        };

    private static ParticleEffect3DAsset CreateEffect3D(params ParticleGroup3DAsset[] groups)
        => new() { Name = "TestEffect", Groups = [.. groups] };
}

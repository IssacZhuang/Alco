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

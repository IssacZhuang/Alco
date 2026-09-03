using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles.Test;

/// <summary>
/// Compute dispatch batching tests of the 2D and 3D systems: the frame's active
/// groups merge into one wide emit and one wide simulate dispatch per behavior
/// material (each 64-thread block resolving its group through the work-block
/// table), while groups with distinct behavior libraries still split — and every
/// active group contributes at least one emit block so its draw-args record resets
/// even with zero spawns.
/// </summary>
public class TestParticleDispatchBatching
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

    [Test(Description = "Groups sharing the default behavior merge into one emit and one simulate dispatch")]
    public void SharedBehaviorMergesDispatches2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 4096, emitterSlots: 8);
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D("A"), CreateGroup2D("B"));
        using ParticleEffectInstance2D instance1 = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(-1f, 0f)), seed: 1);
        using ParticleEffectInstance2D instance2 = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(1f, 0f)), seed: 2);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 60f);

        Assert.Multiple(() =>
        {
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(4), "both instances' groups must be in the plan");
            Assert.That(system.PlannedEmitDispatchCount, Is.EqualTo(1), "same-behavior groups must share one emit dispatch");
            Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(1), "same-behavior groups must share one simulate dispatch");
            // One emit block per group (zero/few spawns still dispatch one block for
            // the args reset), two simulate blocks per 128-particle slice.
            Assert.That(system.PlannedEmitBlockCount, Is.EqualTo(4), "every active group must contribute an emit block");
            Assert.That(system.PlannedSimulateBlockCount, Is.EqualTo(8), "128-particle slices must map to two 64-thread blocks each");
        });
    }

    [Test(Description = "Groups with distinct behavior libraries split into one dispatch pair each")]
    public void DistinctBehaviorsSplitDispatches2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 4096, emitterSlots: 8);
        ParticleGroup2DAsset custom = CreateGroup2D("Custom");
        custom.Behavior = _engine.RenderingSystem.ShaderSystem.GetLibrary("TestBehavior2D");
        ParticleEffect2DAsset effect = CreateEffect2D(CreateGroup2D("Default"), custom);
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 60f);

        Assert.Multiple(() =>
        {
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(2));
            Assert.That(system.PlannedEmitDispatchCount, Is.EqualTo(2), "distinct behaviors must not share an emit dispatch");
            Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(2), "distinct behaviors must not share a simulate dispatch");
            Assert.That(system.PlannedEmitBlockCount, Is.EqualTo(2), "each group's bucket covers exactly its own blocks");
            Assert.That(system.PlannedSimulateBlockCount, Is.EqualTo(4));
        });
    }

    [Test(Description = "The 3D system merges same-behavior groups into one emit and one simulate dispatch too")]
    public void SharedBehaviorMergesDispatches3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, particleCapacity: 4096, emitterSlots: 8);
        ParticleEffect3DAsset effect = CreateEffect3D(CreateGroup3D("A"), CreateGroup3D("B"));
        using ParticleEffectInstance3D instance1 = system.CreateInstance(effect, new Transform3D(new System.Numerics.Vector3(-1f, 0f, 0f)), seed: 1);
        using ParticleEffectInstance3D instance2 = system.CreateInstance(effect, new Transform3D(new System.Numerics.Vector3(1f, 0f, 0f)), seed: 2);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 60f);

        Assert.Multiple(() =>
        {
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(4));
            Assert.That(system.PlannedEmitDispatchCount, Is.EqualTo(1), "same-behavior groups must share one emit dispatch");
            Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(1), "same-behavior groups must share one simulate dispatch");
            Assert.That(system.PlannedEmitBlockCount, Is.EqualTo(4));
            Assert.That(system.PlannedSimulateBlockCount, Is.EqualTo(8));
        });
    }

    [Test(Description = "The 3D system splits groups with distinct behavior libraries")]
    public void DistinctBehaviorsSplitDispatches3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, particleCapacity: 4096, emitterSlots: 8);
        ParticleGroup3DAsset custom = CreateGroup3D("Custom");
        custom.Behavior = _engine.RenderingSystem.ShaderSystem.GetLibrary("TestBehavior3D");
        ParticleEffect3DAsset effect = CreateEffect3D(CreateGroup3D("Default"), custom);
        using ParticleEffectInstance3D instance = system.CreateInstance(effect, new Transform3D(new System.Numerics.Vector3(0f, 0f, 0f)), seed: 1);

        using GPUCommandBuffer commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        system.RecordSimulation(commands, 1f / 60f);

        Assert.Multiple(() =>
        {
            Assert.That(system.PlannedDrawGroupCount, Is.EqualTo(2));
            Assert.That(system.PlannedEmitDispatchCount, Is.EqualTo(2), "distinct behaviors must not share an emit dispatch");
            Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(2), "distinct behaviors must not share a simulate dispatch");
        });
    }

    private static ParticleGroup2DAsset CreateGroup2D(string name)
        => new()
        {
            Name = name,
            MaxParticles = 128,
            EmissionRate = 100f,
            Looping = true,
            Lifetime = new ParticleRange(1f, 2f),
        };

    private static ParticleEffect2DAsset CreateEffect2D(params ParticleGroup2DAsset[] groups)
        => new() { Name = "TestEffect", Groups = [.. groups] };

    private static ParticleGroup3DAsset CreateGroup3D(string name)
        => new()
        {
            Name = name,
            MaxParticles = 128,
            EmissionRate = 100f,
            Lifetime = new ParticleRange(1f, 2f),
        };

    private static ParticleEffect3DAsset CreateEffect3D(params ParticleGroup3DAsset[] groups)
        => new() { Name = "TestEffect", Groups = [.. groups] };
}

using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects.Test;

/// <summary>Verifies batched emit/simulate dispatch planning and draw-group merging across default and custom behavior shaders.</summary>
public class TestParticleDispatchBatching
{
    /// <summary>Minimal no-GPU engine host.</summary>
    public sealed class Host(GameEngineSetting setting) : GameEngine(setting);

    private Host _engine = null!;

    /// <summary>Creates the shared no-GPU engine host.</summary>
    [OneTimeSetUp]
    public void SetUp() => _engine = new Host(GameEngineSetting.CreateNoGPU());

    /// <summary>Disposes the shared engine host.</summary>
    [OneTimeTearDown]
    public void TearDown() => _engine.Dispose();

    /// <summary>2D: groups sharing a behavior coalesce into batched dispatches, while custom behaviors separate cleanly and 0-spawn skips emit.</summary>
    [Test]
    public void EmitAndSimulateBatchByBehavior2D()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 4096, emitterSlots: 64);

        var defaultEffect = new ParticleEffect2D
        {
            Name = "default2d",
            Groups =
            [
                new ParticleGroup2D { Name = "A", MaxParticles = 128, EmissionRate = 100f, Lifetime = new ParticleRange(1f, 2f) },
                new ParticleGroup2D { Name = "B", MaxParticles = 128, EmissionRate = 100f, Lifetime = new ParticleRange(1f, 2f) },
            ],
        };
        var customEffect = new ParticleEffect2D
        {
            Name = "custom2d",
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = "C",
                    MaxParticles = 128,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(1f, 2f),
                    Behavior = "TestBehavior2D",
                },
            ],
        };

        using ParticleEffectInstance2D inst1 = system.CreateInstance(defaultEffect, new Transform2D(Vector2.Zero));
        using ParticleEffectInstance2D inst2 = system.CreateInstance(defaultEffect, new Transform2D(Vector2.One));
        using ParticleEffectInstance2D inst3 = system.CreateInstance(customEffect, new Transform2D(Vector2.UnitX));

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 1f / 60f);
            context.DiscardFrame();

            Assert.Multiple(() =>
            {
                Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1));
                Assert.That(system.PlannedEmitDispatchCount, Is.EqualTo(2));
                Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(2));
                Assert.That(system.PlannedEmitBlockCount, Is.EqualTo(5));
                Assert.That(system.PlannedSimulateBlockCount, Is.EqualTo(10));
            });

            // A zero-dt tick produces zero spawns -> zero emit blocks, while simulation & draw-args reset continue.
            context.BeginFrame();
            system.RecordSimulation(context, 0f);
            context.DiscardFrame();

            Assert.Multiple(() =>
            {
                Assert.That(system.PlannedEmitDispatchCount, Is.Zero);
                Assert.That(system.PlannedEmitBlockCount, Is.Zero);
                Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(2));
            });
        }
        finally
        {
            context.Dispose();
        }
    }

    /// <summary>3D: groups sharing a behavior coalesce into batched dispatches, while custom behaviors separate cleanly.</summary>
    [Test]
    public void EmitAndSimulateBatchByBehavior3D()
    {
        using var system = new GpuParticleSystem3D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 4096, emitterSlots: 64);

        var defaultEffect = new ParticleEffect3D
        {
            Name = "default3d",
            Groups =
            [
                new ParticleGroup3D { Name = "A", MaxParticles = 128, EmissionRate = 100f, Lifetime = new ParticleRange(1f, 2f) },
                new ParticleGroup3D { Name = "B", MaxParticles = 128, EmissionRate = 100f, Lifetime = new ParticleRange(1f, 2f) },
            ],
        };
        var customEffect = new ParticleEffect3D
        {
            Name = "custom3d",
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = "C",
                    MaxParticles = 128,
                    EmissionRate = 100f,
                    Lifetime = new ParticleRange(1f, 2f),
                    Behavior = "TestBehavior3D",
                },
            ],
        };

        using ParticleEffectInstance3D inst1 = system.CreateInstance(defaultEffect, new Transform3D(Vector3.Zero));
        using ParticleEffectInstance3D inst2 = system.CreateInstance(defaultEffect, new Transform3D(Vector3.One));
        using ParticleEffectInstance3D inst3 = system.CreateInstance(customEffect, new Transform3D(Vector3.UnitX));

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 1f / 60f);
            context.DiscardFrame();

            Assert.Multiple(() =>
            {
                Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1));
                Assert.That(system.PlannedEmitDispatchCount, Is.EqualTo(2));
                Assert.That(system.PlannedSimulateDispatchCount, Is.EqualTo(2));
                Assert.That(system.PlannedEmitBlockCount, Is.EqualTo(5));
                Assert.That(system.PlannedSimulateBlockCount, Is.EqualTo(10));
            });
        }
        finally
        {
            context.Dispose();
        }
    }
}

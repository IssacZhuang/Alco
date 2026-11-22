using System.Numerics;
using NUnit.Framework;
using Alco.Engine;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;

namespace Alco.Effects.Test;

/// <summary>Verifies particle buffer pool allocation, slice coalescing, buffer growth migration, rollback on failure, and zero-GC hot path.</summary>
[TestFixture]
[NonParallelizable]
public class TestParticlePoolLifecycle
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

    /// <summary>Allocating, freeing, and reallocating slices reuses capacity without leaking slots.</summary>
    [Test]
    public void SlicesAndSlotsRecycleCleanly()
    {
        using var pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(
            _engine.RenderingSystem, particleCapacity: 512, emitterSlots: 16);

        uint s0 = pool.AllocateSlot();
        ParticleSlice sl0 = pool.AllocateSlice(100); // rounds up -> 128
        uint s1 = pool.AllocateSlot();
        ParticleSlice sl1 = pool.AllocateSlice(200); // rounds up -> 256
        Assert.That(pool.AllocatedSlotCount, Is.EqualTo(2));

        pool.FreeSlice(sl0);
        pool.FreeSlot(s0);
        Assert.That(pool.AllocatedSlotCount, Is.EqualTo(1));

        uint s2 = pool.AllocateSlot();
        ParticleSlice sl2 = pool.AllocateSlice(128);
        Assert.That(s2, Is.EqualTo(s0));
        Assert.That(sl2.Offset, Is.EqualTo(sl0.Offset));
        Assert.That(sl2.Capacity, Is.EqualTo(128u));

        pool.FreeSlice(sl1);
        pool.FreeSlot(s1);
        pool.FreeSlice(sl2);
        pool.FreeSlot(s2);
        Assert.That(pool.AllocatedSlotCount, Is.Zero);
    }

    /// <summary>Growing particle capacity reallocates backing buffers, flags migration, and completes behind RenderContext submission.</summary>
    [Test]
    public void PoolGrowthMigratesAndRetiresOldBuffersBehindRenderContext()
    {
        using var pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(
            _engine.RenderingSystem, particleCapacity: 128, emitterSlots: 4);

        Alco.Graphics.Buffer originalParticles = pool.Particles;
        Alco.Graphics.Buffer originalInstanceData = pool.InstanceData;
        ParticleSlice first = pool.AllocateSlice(128);
        ParticleSlice second = pool.AllocateSlice(128); // exceeds 128 -> grows to >= 256

        Assert.That(pool.ParticleCapacity, Is.GreaterThanOrEqualTo(256));
        Assert.That(pool.Particles, Is.Not.SameAs(originalParticles));
        Assert.That(pool.InstanceData, Is.Not.SameAs(originalInstanceData));
        Assert.That(pool.HasPendingMigration, Is.True);

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            bool changed = pool.RecordUploadsAndMigration(context);
            Assert.That(changed, Is.True);
            Assert.That(pool.HasPendingMigration, Is.False);
            context.Submit().Wait();
            _engine.RenderingSystem.Tick();
        }
        finally
        {
            context.Destroy();
            pool.FreeSlice(first);
            pool.FreeSlice(second);
        }
    }

    /// <summary>Growing emitter slots preserves existing parameter entries.</summary>
    [Test]
    public void EmitterSlotGrowthPreservesExistingParams()
    {
        using var pool = new ParticleBufferPool<GpuParticle3D, EmitterParams3D>(
            _engine.RenderingSystem, particleCapacity: 512, emitterSlots: 2);

        uint slot0 = pool.AllocateSlot();
        pool.SetParams(slot0, new EmitterParams3D { Capacity = 64, SliceOffset = 0, CustomData = 42.5f });
        uint slot1 = pool.AllocateSlot();
        uint slot2 = pool.AllocateSlot(); // triggers slot growth

        Assert.That(pool.SlotCapacity, Is.GreaterThanOrEqualTo(4));
        Assert.That(pool.ParamsSpan[(int)slot0].CustomData, Is.EqualTo(42.5f));

        pool.FreeSlot(slot2);
        pool.FreeSlot(slot1);
        pool.FreeSlot(slot0);
    }

    /// <summary>If a multi-group effect fails on a later group, earlier groups' slots and slices roll back cleanly.</summary>
    [Test]
    public void CreateInstanceRollsBackOnFailure()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.ShaderSystem, particleCapacity: 512, emitterSlots: 16);
        Image white = new(_engine.RenderingSystem.TextureWhite, ownsResource: false);

        var failingEffect = new ParticleEffect2D
        {
            Name = "partial-fail",
            Groups =
            [
                new ParticleGroup2D { Name = "Valid", MaxParticles = 64, EmissionRate = 10f },
                new ParticleGroup2D
                {
                    Name = "InvalidMaterial",
                    MaxParticles = 64,
                    Material = new MaterialAsset
                    {
                        Name = "broken",
                        Surface = "TestParticleSurface",
                        Textures = new Dictionary<string, Image> { ["bogus"] = white },
                    },
                },
            ],
        };

        Assert.Throws<InvalidOperationException>(() => system.CreateInstance(failingEffect, new Transform2D(Vector2.Zero)));
        Assert.That(system.Pool.AllocatedSlotCount, Is.Zero, "Rolled-back CreateInstance must leave zero allocated slots.");
    }

    /// <summary>Steady-state simulation and draw recording allocates zero managed memory.</summary>
    [Test]
    public void AdvanceFrameHotPathAllocatesNothing()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.ShaderSystem, particleCapacity: 1024, emitterSlots: 16);
        var effect = new ParticleEffect2D
        {
            Name = "hotpath",
            Groups = [new ParticleGroup2D { Name = "Main", MaxParticles = 128, EmissionRate = 60f, Lifetime = new ParticleRange(0.5f, 1f) }],
        };
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(Vector2.Zero), seed: 1);

        Texture target = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(64, 64, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment,
        });
        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            var passSig = new RenderPassSignature([Format.Rgba8Unorm]);
            var vp = Matrix4x4.Identity;

            void StepFrame()
            {
                context.BeginFrame();
                system.RecordSimulation(context, 1f / 60f);
                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
                };
                using (var render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = 64, Height = 64 });
                    render.SetScissor(new Scissor { Width = 64, Height = 64 });
                    system.Render(context, render, in vp);
                }
                context.Submit().Wait();
            }

            for (int i = 0; i < 64; i++)
                StepFrame();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
                StepFrame();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.Zero, $"Hot-path particle simulation + render allocated {allocated} B over 200 frames.");
        }
        finally
        {
            context.Destroy();
            target.Destroy();
        }
    }
}

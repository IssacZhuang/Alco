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
            context.Submit();
            _engine.RenderingSystem.WaitIdle();
        }
        finally
        {
            context.Dispose();
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
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 512, emitterSlots: 16);
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

    /// <summary>Allocating a fresh slice from high-water queues an init zeroing kill before first use.</summary>
    [Test]
    public void FreshSliceAllocationQueuesInitKill()
    {
        using var pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(
            _engine.RenderingSystem, particleCapacity: 256, emitterSlots: 4);

        ParticleSlice fresh = pool.AllocateSlice(64);
        Assert.That(pool.HasPendingKills, Is.True);
        Assert.That(pool.PendingKills, Has.Count.EqualTo(1));
        Assert.That(pool.PendingKills[0], Is.EqualTo((fresh.Offset, fresh.Capacity)));
        pool.FreeSlice(fresh);
    }

    /// <summary>Pool growth between RecordSimulation and Render renders using the snapshotted buffers without error.</summary>
    [Test]
    public void PoolGrowthBetweenRecordSimulationAndRenderUsesSnapshot()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 128, emitterSlots: 2);
        var effect = new ParticleEffect2D
        {
            Name = "snapshot-growth",
            Groups = [new ParticleGroup2D { Name = "G", MaxParticles = 128, EmissionRate = 60f, Lifetime = new ParticleRange(1f, 2f) }],
        };
        using ParticleEffectInstance2D first = system.CreateInstance(effect, new Transform2D(Vector2.Zero), seed: 1);
        ParticleEffectInstance2D? grown = null;

        Texture target = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(32, 32, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment,
        });
        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 1f / 60f);

            // Force both particle capacity growth and emitter slot growth mid-frame before Render().
            var extraEffect = new ParticleEffect2D
            {
                Name = "growth-trigger",
                Groups =
                [
                    new ParticleGroup2D { Name = "E1", MaxParticles = 256, EmissionRate = 60f, Lifetime = new ParticleRange(1f, 2f) },
                    new ParticleGroup2D { Name = "E2", MaxParticles = 64, EmissionRate = 60f, Lifetime = new ParticleRange(1f, 2f) },
                ],
            };
            grown = system.CreateInstance(extraEffect, new Transform2D(Vector2.One), seed: 2);

            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc))
            {
                render.SetViewport(new Viewport { Width = 32, Height = 32 });
                render.SetScissor(new Scissor { Width = 32, Height = 32 });
                var vp = Matrix4x4.Identity;
                system.Render(context, render, in vp);
            }
            context.Submit();
            _engine.RenderingSystem.WaitIdle();

            // Next frame replays migration & renders grown capacity cleanly.
            context.BeginFrame();
            system.RecordSimulation(context, 1f / 60f);
            using (var render = context.BeginRenderPass(passDesc))
            {
                render.SetViewport(new Viewport { Width = 32, Height = 32 });
                render.SetScissor(new Scissor { Width = 32, Height = 32 });
                var vp = Matrix4x4.Identity;
                system.Render(context, render, in vp);
            }
            context.Submit();
            _engine.RenderingSystem.WaitIdle();
            Assert.That(system.Pool.HasPendingMigration, Is.False);
        }
        finally
        {
            grown?.Dispose();
            context.Dispose();
            target.Dispose();
        }
    }

    /// <summary>DiscardFrame restores pending migrations, dirty parameters, and kills so the next frame replays them.</summary>
    [Test]
    public void DiscardFrameRestoresPoolMigrationsParamsAndKills()
    {
        using var pool = new ParticleBufferPool<GpuParticle2D, EmitterParams2D>(
            _engine.RenderingSystem, particleCapacity: 128, emitterSlots: 4);

        uint slot0 = pool.AllocateSlot();
        ParticleSlice slice0 = pool.AllocateSlice(128);
        pool.SetParams(slot0, new EmitterParams2D { Capacity = slice0.Capacity, SliceOffset = slice0.Offset });
        var kills = new List<(uint Offset, uint Count)>();

        var context = new RenderContext(_engine.RenderingSystem);
        try
        {
            context.BeginFrame();
            Assert.That(pool.RecordUploadsAndMigration(context), Is.True);
            pool.DrainPendingKills(context, kills);
            context.Submit();
            _engine.RenderingSystem.WaitIdle();

            // Queue growth migration, dirty param, and a new kill, then discard the recording frame.
            uint slot1 = pool.AllocateSlot();
            ParticleSlice slice1 = pool.AllocateSlice(128);
            pool.SetParams(slot1, new EmitterParams2D { Capacity = slice1.Capacity, SliceOffset = slice1.Offset });

            context.BeginFrame();
            Assert.That(pool.RecordUploadsAndMigration(context), Is.True);
            pool.DrainPendingKills(context, kills);
            Assert.That(kills, Has.Count.EqualTo(1));
            Assert.That(pool.HasPendingMigration, Is.False);
            Assert.That(pool.HasPendingKills, Is.False);
            context.DiscardFrame();

            context.BeginFrame();
            Assert.That(pool.ReconcileFrame(context), Is.True);
            Assert.That(pool.HasPendingMigration, Is.True);
            Assert.That(pool.HasPendingKills, Is.True);
            Assert.That(pool.RecordUploadsAndMigration(context), Is.True);
            pool.DrainPendingKills(context, kills);
            Assert.That(kills, Has.Count.EqualTo(1));
            context.Submit();
            _engine.RenderingSystem.WaitIdle();

            pool.FreeSlice(slice1);
            pool.FreeSlot(slot1);
        }
        finally
        {
            context.Dispose();
            pool.FreeSlice(slice0);
            pool.FreeSlot(slot0);
        }
    }

    /// <summary>TransientBufferPool tracks active RenderContext identity so identically numbered FrameIds never alias across contexts.</summary>
    [Test]
    public void TransientBufferPoolIsolatesFrameIdsAcrossContexts()
    {
        using var pool = new TransientBufferPool(_engine.RenderingSystem);
        var contextA = new RenderContext(_engine.RenderingSystem);
        var contextB = new RenderContext(_engine.RenderingSystem);
        try
        {
            contextA.BeginFrame(); // FrameId == 1
            Alco.Graphics.Buffer bufA = pool.Acquire(contextA, 256);

            contextB.BeginFrame(); // Also FrameId == 1, distinct context!
            Alco.Graphics.Buffer bufB = pool.Acquire(contextB, 256);
            Assert.That(bufB, Is.Not.SameAs(bufA));
            contextB.Submit();
            _engine.RenderingSystem.WaitIdle();

            contextA.Submit();
            _engine.RenderingSystem.WaitIdle();

            contextB.BeginFrame();
            Alco.Graphics.Buffer reused = pool.Acquire(contextB, 256);
            Assert.That(reused == bufA || reused == bufB, Is.True);
            contextB.Submit();
            _engine.RenderingSystem.WaitIdle();
        }
        finally
        {
            contextB.Dispose();
            contextA.Dispose();
        }
    }

    /// <summary>Sprites lacking Sampled usage fail clearly and TrimUnusedCaches safely destroys retired gradient/curve textures.</summary>
    [Test]
    public void SpriteWithoutSampledUsageThrows_AndTrimUnusedCachesCleansUp()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 256, emitterSlots: 8);

        Texture nonSampledTex = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(4, 4, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment,
        });
        using var badSprite = new Image(nonSampledTex, ownsResource: true);

        var badEffect = new ParticleEffect2D
        {
            Name = "non-sampled",
            Groups = [new ParticleGroup2D { Name = "Bad", MaxParticles = 64, Texture = badSprite }],
        };
        Assert.Throws<InvalidOperationException>(() => system.CreateInstance(badEffect, new Transform2D(Vector2.Zero)));

        var curveEffect = new ParticleEffect2D
        {
            Name = "cached-curves",
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = "G",
                    MaxParticles = 64,
                    ColorGradient = [new ParticleColorKey { Time = 0f, Color = ColorFloat.White }, new ParticleColorKey { Time = 1f, Color = ColorFloat.Transparent }],
                    SizeCurve = [new ParticleScalarKey { Time = 0f, Value = 1f }, new ParticleScalarKey { Time = 1f, Value = 0f }],
                },
            ],
        };
        ParticleEffectInstance2D inst = system.CreateInstance(curveEffect, new Transform2D(Vector2.Zero));
        inst.Dispose();
        Assert.DoesNotThrow(() => system.TrimUnusedCaches());
    }

    /// <summary>Headless RenderGraph execution skips render nodes cleanly and RenderGraph execution with depth-write allocates zero in steady state.</summary>
    [Test]
    public void RenderGraphNodesHandleHeadlessAndDepthWriteAndSteadyZeroAlloc()
    {
        using var particles2D = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 512, emitterSlots: 16);
        using var particles3D = new GpuParticleSystem3D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 512, emitterSlots: 16);
        using var trails2D = new GpuTrailSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, pointCapacity: 256, trailSlots: 8);
        using var trails3D = new GpuTrailSystem3D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, pointCapacity: 256, trailSlots: 8);

        var effect2D = new ParticleEffect2D
        {
            Name = "rg2d",
            Groups = [new ParticleGroup2D { Name = "G", MaxParticles = 64, EmissionRate = 60f, Lifetime = new ParticleRange(1f, 2f) }],
        };
        var effect3D = new ParticleEffect3D
        {
            Name = "rg3d-depthwrite",
            Groups = [new ParticleGroup3D { Name = "G", MaxParticles = 64, EmissionRate = 60f, Depth = DepthStencilState.WriteReverseZ }],
        };
        using var inst2D = particles2D.CreateInstance(effect2D, new Transform2D(Vector2.Zero), seed: 1);
        using var inst3D = particles3D.CreateInstance(effect3D, new Transform3D(Vector3.Zero), seed: 1);
        Assert.That(trails2D.TryCreateInstance(new TrailEffect2D { ExpectedPoints = 32, Depth = DepthStencilState.Write }, Vector2.Zero, out TrailEffectInstance2D trail2D), Is.True);
        Assert.That(trails3D.TryCreateInstance(new TrailEffect3D { ExpectedPoints = 32, Depth = DepthStencilState.WriteReverseZ }, Vector3.Zero, out TrailEffectInstance3D trail3D), Is.True);

        var cam2D = new Camera2D { Size = new Vector2(10f, 10f), Near = -10f, Far = 10f };
        var cam3D = new CameraPerspective { FieldOfView = MathF.PI / 2f, AspectRatio = 1f, Near = 0.1f, Far = 100f };
        Texture color = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(64, 64, 1),
            Format = Format.Rgba8Unorm,
            Usage = TextureUsage.ColorAttachment,
        });
        Texture depth = _engine.RenderingSystem.CreateTexture(new TextureDesc
        {
            Extent = new Extent3D(64, 64, 1),
            Format = Format.D32Float,
            Usage = TextureUsage.DepthStencilAttachment,
        });
        var context = new RenderContext(_engine.RenderingSystem);
        var graph = new RenderGraph(_engine.RenderingSystem, new Extent2D(64, 64), "effects_test_rg");
        try
        {
            graph.Use(new ParticleSimulateNode2D(particles2D));
            graph.Use(new ParticleRenderNode2D(particles2D, cam2D, colorLoadOp: LoadOp.Clear, depthTextureProvider: () => depth));
            graph.Use(new TrailRenderNode2D(trails2D, cam2D, colorLoadOp: LoadOp.Load, depthTextureProvider: () => depth));
            graph.Use(new ParticleSimulateNode3D(particles3D));
            graph.Use(new ParticleRenderNode3D(particles3D, cam3D, colorLoadOp: LoadOp.Load, depthTextureProvider: () => depth));
            graph.Use(new TrailRenderNode3D(trails3D, cam3D, colorLoadOp: LoadOp.Load, depthTextureProvider: () => depth));

            // Headless (default output) must not throw.
            context.BeginFrame();
            graph.Execute(context, output: default, deltaTime: 1f / 60f);
            context.Submit();
            _engine.RenderingSystem.WaitIdle();

            Assert.That(particles3D.HasDepthWritingEffects, Is.True);
            Assert.That(trails2D.HasDepthWritingEffects, Is.True);
            Assert.That(trails3D.HasDepthWritingEffects, Is.True);

            RenderGraphTarget target = RenderGraphTarget.FromTexture(color);
            for (int i = 0; i < 64; i++)
            {
                trails2D.Update(1f / 60f);
                trail2D.ExtendTo(new Vector2(i * 0.25f, 0f), 0f);
                trails3D.Update(1f / 60f);
                trail3D.ExtendTo(new Vector3(i * 0.25f, 0f, 0f));
                context.BeginFrame();
                graph.Execute(context, target, deltaTime: 1f / 60f);
                context.Submit();
                _engine.RenderingSystem.WaitIdle();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                trails2D.Update(1f / 60f);
                trail2D.ExtendTo(new Vector2((64 + i) * 0.25f, 0f), 0f);
                trails3D.Update(1f / 60f);
                trail3D.ExtendTo(new Vector3((64 + i) * 0.25f, 0f, 0f));
                context.BeginFrame();
                graph.Execute(context, target, deltaTime: 1f / 60f);
                context.Submit();
                _engine.RenderingSystem.WaitIdle();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, $"RenderGraph effect nodes allocated {allocated} B over 100 frames.");
        }
        finally
        {
            trail2D.Dispose();
            trail3D.Dispose();
            graph.Dispose();
            context.Dispose();
            depth.Dispose();
            color.Dispose();
        }
    }

    /// <summary>Steady-state simulation and draw recording allocates zero managed memory.</summary>
    [Test]
    public void AdvanceFrameHotPathAllocatesNothing()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, _engine.RenderingSystem.Shaders, particleCapacity: 1024, emitterSlots: 16);
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
                    Colors = [new ColorAttachment { Target = target, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
                };
                using (var render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = 64, Height = 64 });
                    render.SetScissor(new Scissor { Width = 64, Height = 64 });
                    system.Render(context, render, in vp);
                }
                context.Submit();
                _engine.RenderingSystem.WaitIdle();
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
            context.Dispose();
            target.Dispose();
        }
    }
}

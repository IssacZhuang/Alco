using NUnit.Framework;
using Alco.Engine;
using Alco.Rendering;

namespace Alco.Particles.Test;

/// <summary>
/// Lifecycle and bookkeeping tests of the shared particle buffer pool and the
/// systems' create/dispose paths on the no-GPU backend: growth retirement,
/// disposal (incl. unrecorded growth copies), slice/slot recycling, instance
/// teardown returning pool resources, creation rollback on failure, and the
/// steady-state per-frame CPU path's allocation budget.
/// </summary>
public class TestParticlePoolLifecycle
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

    private ParticleBufferPool<GpuParticle2D, EmitterParams2D> CreatePool(int particles = 256, int slots = 4)
        => new(_engine.RenderingSystem, particles, slots, "test");

    [Test(Description = "Growth retires the old buffers after the 2-frame grace")]
    public void GrowthRetiresOldBuffersAfterGrace()
    {
        using var pool = CreatePool(particles: 256);
        pool.AllocateSlice(200); // rounds to 256, exactly filling the pool
        GraphicsBuffer oldParticles = pool.Particles;
        GraphicsBuffer oldRenderList = pool.RenderList;

        pool.AllocateSlice(100); // 256 + 128 > 256: grows to 512

        Assert.Multiple(() =>
        {
            Assert.That(pool.ParticleCapacity, Is.EqualTo(512));
            Assert.That(pool.Particles, Is.Not.SameAs(oldParticles));
            Assert.That(oldParticles.IsDisposed, Is.False, "old pool disposed without the grace period");
            Assert.That(oldRenderList.IsDisposed, Is.False, "old render list disposed without the grace period");
        });

        using var commands = _engine.GraphicsDevice.CreateCommandBuffer("test");
        commands.Begin();
        pool.RecordMigration(commands); // records the copy, grace 2 -> 1
        Assert.That(oldParticles.IsDisposed, Is.False, "old pool disposed after only one grace frame");
        pool.RecordMigration(commands); // grace 1 -> 0: dispose
        Assert.Multiple(() =>
        {
            Assert.That(oldParticles.IsDisposed, Is.True, "old pool not disposed after the grace period");
            Assert.That(oldRenderList.IsDisposed, Is.True, "old render list not disposed after the grace period");
        });
    }

    [Test(Description = "Pool dispose releases growth-copy sources that were never recorded (and is idempotent)")]
    public void DisposeReleasesUnrecordedGrowthSources()
    {
        var pool = CreatePool(particles: 256);
        pool.AllocateSlice(200);
        GraphicsBuffer oldParticles = pool.Particles;
        GraphicsBuffer oldRenderList = pool.RenderList;

        pool.AllocateSlice(100); // grows; the old buffers sit in the pending-copy list
        pool.Dispose(); // disposed before any RecordMigration recorded the copies

        Assert.Multiple(() =>
        {
            Assert.That(oldParticles.IsDisposed, Is.True, "unrecorded growth-copy source (pool) leaked on dispose");
            Assert.That(oldRenderList.IsDisposed, Is.True, "unrecorded growth-copy source (render list) leaked on dispose");
        });
        Assert.DoesNotThrow(() => pool.Dispose(), "pool dispose is not idempotent");
    }

    [Test(Description = "A freed slice is recycled by size and its kill is queued")]
    public void FreedSliceRecyclesAndQueuesKill()
    {
        using var pool = CreatePool();
        ParticleSlice first = pool.AllocateSlice(64);
        ParticleSlice other = pool.AllocateSlice(64);

        pool.FreeSlice(first);

        Assert.Multiple(() =>
        {
            Assert.That(pool.PendingKills, Has.Count.EqualTo(1));
            Assert.That(pool.PendingKills[0], Is.EqualTo((first.Offset, first.Capacity)));
        });

        ParticleSlice recycled = pool.AllocateSlice(64);
        Assert.That(recycled.Offset, Is.EqualTo(first.Offset), "the freed slice was not recycled");

        pool.ClearPendingKills();
        Assert.That(pool.PendingKills, Is.Empty);
        Assert.That(other.Offset, Is.Not.EqualTo(first.Offset));
    }

    [Test(Description = "Emitter slots grow geometrically and free back to zero")]
    public void SlotsGrowAndFree()
    {
        using var pool = CreatePool(slots: 4);
        var slots = new uint[5];
        for (int i = 0; i < 4; i++)
        {
            slots[i] = pool.AllocateSlot();
        }
        Assert.That(pool.AllocatedSlotCount, Is.EqualTo(4));

        slots[4] = pool.AllocateSlot(); // exhausts the pool: doubles to 8
        Assert.Multiple(() =>
        {
            Assert.That(pool.SlotCapacity, Is.EqualTo(8));
            Assert.That(pool.AllocatedSlotCount, Is.EqualTo(5));
            Assert.That(slots.Distinct().Count(), Is.EqualTo(5), "a slot was handed out twice");
        });

        foreach (uint slot in slots)
        {
            pool.FreeSlot(slot);
        }
        Assert.That(pool.AllocatedSlotCount, Is.EqualTo(0));
    }

    [Test(Description = "Disposing an instance returns its slots and slices to the pool")]
    public void InstanceDisposeReturnsSlotsAndSlices()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        ParticleEffect2D effect = CreateEffect(CreateGroup(), CreateGroup());

        ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(1f, 2f)), seed: 7);
        Assert.That(system.Pool.AllocatedSlotCount, Is.EqualTo(2));

        instance.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(system.Pool.AllocatedSlotCount, Is.EqualTo(0), "slots were not returned");
            Assert.That(system.Instances, Is.Empty, "the instance was not unregistered");
        });

        // Both slices (offsets 0 and 128) are back on the free stack: direct pool
        // allocation recycles them instead of bump-allocating past the high water.
        ParticleSlice a = system.Pool.AllocateSlice(128);
        ParticleSlice b = system.Pool.AllocateSlice(128);
        Assert.Multiple(() =>
        {
            Assert.That(a.Offset, Is.LessThan(256u), "a recycled slice was expected below the high water mark");
            Assert.That(b.Offset, Is.LessThan(256u), "a recycled slice was expected below the high water mark");
            Assert.That(a.Offset, Is.Not.EqualTo(b.Offset));
        });

        // Double dispose is a no-op (AutoDisposable), not a double free.
        Assert.DoesNotThrow(() => instance.Dispose());
        Assert.That(system.Pool.AllocatedSlotCount, Is.EqualTo(0));
    }

    [Test(Description = "A failing CreateInstance frees the slots/slices of the groups already set up")]
    public void CreateInstanceRollsBackOnFailure()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1024, emitterSlots: 8);
        using Texture2D texture = _engine.RenderingSystem.CreateTexture2D(1, 1);

        // Group 1 is fine; group 2's material names a texture slot its surface does
        // not declare — the compile-time slot validation fails mid-construction.
        var badMaterial = new MaterialAsset
        {
            Name = "bad",
            Textures = new Dictionary<string, Texture2D> { ["bogus"] = texture },
        };
        ParticleEffect2D effect = CreateEffect(CreateGroup(), CreateGroup(material: badMaterial));

        Assert.Throws<InvalidDataException>(() => system.CreateInstance(effect, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1));
        Assert.Multiple(() =>
        {
            Assert.That(system.Pool.AllocatedSlotCount, Is.EqualTo(0), "slots of the failed creation leaked");
            Assert.That(system.Instances, Is.Empty);
        });

        // Both groups' slices (offsets 0 and 128) were returned to the free stack.
        ParticleSlice a = system.Pool.AllocateSlice(128);
        ParticleSlice b = system.Pool.AllocateSlice(128);
        Assert.Multiple(() =>
        {
            Assert.That(a.Offset, Is.LessThan(256u), "the in-flight group's slice leaked");
            Assert.That(b.Offset, Is.LessThan(256u), "the completed group's slice leaked");
            Assert.That(a.Offset, Is.Not.EqualTo(b.Offset));
        });
    }

    [Test(Description = "The steady-state per-frame CPU path (AdvanceFrame + dirty-range upload) allocates nothing")]
    public void AdvanceFrameHotPathAllocatesNothing()
    {
        using var system = new GpuParticleSystem2D(_engine.RenderingSystem, particleCapacity: 1 << 16, emitterSlots: 128);
        ParticleEffect2D looping = CreateEffect(CreateGroup(rate: 200f), CreateGroup(rate: 200f));
        ParticleEffect2D oneShot = CreateEffect(CreateGroup(rate: 0f, duration: 0.1f, looping: false));

        var instances = new ParticleEffectInstance2D[33];
        for (int i = 0; i < 32; i++)
        {
            instances[i] = system.CreateInstance(looping, new Transform2D(new System.Numerics.Vector2(i, 0f)), seed: 100 + i);
        }
        instances[32] = system.CreateInstance(oneShot, new Transform2D(new System.Numerics.Vector2(0f, 0f)), seed: 1);

        // One simulated frame: exactly the CPU work RecordSimulation owns (per-instance
        // timeline advance plus the dirty-range params upload).
        void SimulateFrame(float deltaTime)
        {
            uint dirtyMin = uint.MaxValue;
            uint dirtyMax = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                instances[i].AdvanceFrame(deltaTime, ref dirtyMin, ref dirtyMax);
            }
            if (dirtyMin <= dirtyMax)
            {
                system.Pool.Params.UpdateBufferRanged(dirtyMin, dirtyMax - dirtyMin + 1);
            }
        }

        // Warm up: JIT tiering, lazy behavior-material compilation, and the one-shot's
        // deactivation (duration 0.1s + max lifetime 2s) all happen here.
        for (int i = 0; i < 240; i++)
        {
            SimulateFrame(1f / 60f);
        }
        Assert.That(instances[32].IsActive, Is.False, "the one-shot instance should have deactivated during warmup");

        const int frames = 2000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < frames; i++)
        {
            SimulateFrame(1f / 60f);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.LessThanOrEqualTo(1024),
            $"the per-frame CPU path allocated {allocated} B over {frames} frames ({allocated / (double)frames:F2} B/frame)");
    }

    private static ParticleGroup2D CreateGroup(
        float rate = 100f, float duration = 0f, bool looping = true, MaterialAsset? material = null)
        => new()
        {
            Name = "TestGroup",
            MaxParticles = 128,
            EmissionRate = rate,
            Duration = duration,
            Looping = looping,
            Lifetime = new ParticleRange(1f, 2f),
            Material = material,
        };

    private static ParticleEffect2D CreateEffect(params ParticleGroup2D[] groups)
        => new() { Name = "TestEffect", Groups = [.. groups] };
}

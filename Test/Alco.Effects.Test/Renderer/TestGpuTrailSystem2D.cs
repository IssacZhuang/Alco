using System.Numerics;
using System.Reflection;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>Regresses trail pool reuse, material-cache allocations, and rendered ring continuity.</summary>
[TestFixture]
[NonParallelizable]
public sealed class TestGpuTrailSystem2D
{
    /// <summary>A freed large slice can serve smaller requests, including a non-power-of-two total budget.</summary>
    [TestCase(64)]
    [TestCase(95)]
    public void FreedLargeSliceSplits(int pointCapacity)
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, pointCapacity, 3);
        using var large = CreateTrail(system, 64);
        large.Dispose();

        using var first = CreateTrail(system, 32);
        using var second = CreateTrail(system, 32);
        Assert.That(system.TryCreateInstance(new TrailEffect2D { ExpectedPoints = 32 }, Vector2.Zero, out _), Is.False);
        Assert.That(first.IsAlive && second.IsAlive, Is.True);
    }

    /// <summary>Splitting a free slice does not require every other trail to be retired.</summary>
    [Test]
    public void FreedLargeSliceSplitsWhileAnotherTrailIsAlive()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 96, 3);
        using var large = CreateTrail(system, 64);
        using var retained = CreateTrail(system, 32);
        large.Dispose();

        using var first = CreateTrail(system, 32);
        using var second = CreateTrail(system, 32);
        retained.ExtendTo(Vector2.UnitX, 0f);
        Assert.That(retained.IsEmitting, Is.True);
        Assert.That(first.IsAlive && second.IsAlive, Is.True);
    }

    /// <summary>Freeing the middle slice joins both neighbors without crossing a live allocation.</summary>
    [Test]
    public void AdjacentFreeSlicesMergeWhileAnotherTrailIsAlive()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 128, 4);
        using var first = CreateTrail(system, 32);
        using var middle = CreateTrail(system, 32);
        using var third = CreateTrail(system, 32);
        using var retained = CreateTrail(system, 32);
        first.Dispose();
        third.Dispose();
        Assert.That(system.TryCreateInstance(new TrailEffect2D { ExpectedPoints = 64 }, Vector2.Zero, out _), Is.False,
            "Non-adjacent free ranges must not be joined through the live middle trail.");

        middle.Dispose();
        using var large = CreateTrail(system, 64);
        using var remainder = CreateTrail(system, 32);
        Assert.That(retained.IsEmitting, Is.True);
        Assert.That(system.TryCreateInstance(new TrailEffect2D { ExpectedPoints = 32 }, Vector2.Zero, out _), Is.False);
    }

    /// <summary>Repeated size changes and natural expiry do not strand the fixed point budget.</summary>
    [Test]
    public void MixedCapacitiesRecycleAfterExpiry()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 256, 2);
        int[] sizes = [256, 32, 128, 64];
        for (int iteration = 0; iteration < 32; iteration++)
        {
            foreach (int size in sizes)
            {
                using var trail = CreateTrail(system, size);
                trail.ExtendTo(Vector2.UnitX, 0f);
                trail.Stop();
                system.Update(1f);
                using var replacement = CreateTrail(system, 256);
                trail.Dispose();
                Assert.That(replacement.IsEmitting, Is.True, "A stale handle affected the recycled slot.");
            }
        }
    }

    /// <summary>Very large requests clamp to the documented maximum before power-of-two rounding.</summary>
    [Test]
    public void ExpectedPointLimitDoesNotOverflow()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 1024, 2);
        using var trail = CreateTrail(system, int.MaxValue);
        Assert.That(system.TryCreateInstance(new TrailEffect2D { ExpectedPoints = 32 }, Vector2.Zero, out _), Is.False);
    }

    /// <summary>Both built-in surfaces compose with the 2D trail pass and resource layout.</summary>
    [TestCase("TrailSurfaceDefault")]
    [TestCase("TrailSurfaceSmoke")]
    public void BuiltInSurfaceComposes(string surfaceName)
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 32, 1);
        var effect = new TrailEffect2D
        {
            ExpectedPoints = 32,
            Material = new MaterialAsset { Surface = surfaceName },
        };
        Assert.That(system.TryCreateInstance(effect, Vector2.Zero, out TrailEffectInstance2D trail), Is.True);
        using (trail)
        {
            trail.ExtendTo(Vector2.UnitX, 0f);
            Assert.That(trail.IsAlive, Is.True);
        }
    }

    /// <summary>A warm cache lookup creates neither a closure nor boxed graphics states.</summary>
    [Test]
    public void MaterialCacheHitDoesNotAllocate()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 32, 1);
        Func<TrailEffect2D, object> lookup = GetMaterialLookup(system);
        var effect = new TrailEffect2D();
        for (int i = 0; i < 1000; i++) lookup(effect);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) lookup(effect);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, $"Material cache hits allocated {allocated} B over 2000 calls.");
    }

    /// <summary>The material cache separates assets and blend modes, while draw planning separates depth-stencil states.</summary>
    [Test]
    public void MaterialCacheSeparatesAssetsAndRenderStates()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 256, 8);
        Func<TrailEffect2D, object> lookup = GetMaterialLookup(system);
        var asset = new MaterialAsset();
        var baseline = new TrailEffect2D { ExpectedPoints = 16, Material = asset };
        object cachedBaseline = lookup(baseline);
        Assert.That(lookup(new TrailEffect2D { Material = asset }), Is.SameAs(cachedBaseline));

        TrailEffect2D[] distinctMaterials =
        [
            new() { Material = new MaterialAsset() },
            new() { Material = asset, Blend = BlendState.Additive },
        ];
        foreach (TrailEffect2D effect in distinctMaterials)
        {
            object other = lookup(effect);
            Assert.That(other, Is.Not.SameAs(cachedBaseline));
            Assert.That(lookup(effect), Is.SameAs(other));
        }

        // Different DepthStencilStates share the same compiled Material but split into separate DrawBatches.
        Assert.That(system.TryCreateInstance(new TrailEffect2D
        {
            ExpectedPoints = 16,
            Material = asset,
            Depth = DepthStencilState.None,
        }, Vector2.Zero, out var t0), Is.True);
        Assert.That(system.TryCreateInstance(new TrailEffect2D
        {
            ExpectedPoints = 16,
            Material = asset,
            Depth = DepthStencilState.Read with { StencilReadMask = 1 },
        }, Vector2.Zero, out var t1), Is.True);
        Assert.That(system.TryCreateInstance(new TrailEffect2D
        {
            ExpectedPoints = 16,
            Material = asset,
            Depth = DepthStencilState.Read with { StencilWriteMask = 1 },
        }, Vector2.Zero, out var t2), Is.True);

        using (t0)
        using (t1)
        using (t2)
        {
            t0.ExtendTo(Vector2.UnitX, 0f);
            t1.ExtendTo(Vector2.UnitX, 0f);
            t2.ExtendTo(Vector2.UnitX, 0f);
            var context = new RenderContext(host.RenderingSystem);
            try
            {
                context.BeginFrame();
                system.PrepareRender(context);
                context.DiscardFrame();
                Assert.That(system.PlannedBatchCount, Is.EqualTo(3));
            }
            finally
            {
                context.Destroy();
            }
        }
    }

    /// <summary>Emission, uploads, draw planning, and rendering stay allocation-free in steady state.</summary>
    [Test]
    public void SteadyFrameDoesNotAllocate()
    {
        using var host = new EffectsTestHost(GraphicsBackend.NoGPU);
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 32, 1);
        using var trail = CreateTrail(system, 32);
        Texture target = host.CreateColorTarget(32, 32);
        var context = new RenderContext(host.RenderingSystem);
        var passSig = new RenderPassSignature([Format.Rgba8Unorm]);
        Matrix4x4 vp = Matrix4x4.Identity;
        try
        {
            int tick = 0;
            void Frame()
            {
                system.Update(1f / 60f);
                trail.ExtendTo(new Vector2(++tick * 0.25f, 0f), 0f);
                context.BeginFrame();
                system.PrepareRender(context);
                var passDesc = new RenderingDesc
                {
                    Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
                };
                using (var render = context.BeginRenderPass(passDesc, passSig))
                {
                    render.SetViewport(new Viewport { Width = 32, Height = 32 });
                    render.SetScissor(new Scissor { Width = 32, Height = 32 });
                    system.Render(context, render, in vp);
                }
                context.Submit().Wait();
            }

            for (int i = 0; i < 128; i++) Frame();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) Frame();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, $"Steady rendering allocated {allocated} B over 500 frames.");
        }
        finally
        {
            context.Destroy();
            target.Destroy();
        }
    }

    /// <summary>Real GPU pixels cover every live segment before and after one or multiple ring wraps.</summary>
    [TestCase(2, false)]
    [TestCase(31, false)]
    [TestCase(32, false)]
    [TestCase(34, false)]
    [TestCase(64, false)]
    [TestCase(66, true)]
    [Category("AlcoGpu")]
    public void RingRendersContinuousLiveWindow(int writtenPoints, bool offsetSlice)
    {
        using EffectsTestHost host = EffectsTestHost.CreateGpu();
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 64, 2);
        using var reserved = offsetSlice ? CreateTrail(system, 32) : null;
        const float spacing = 0.03125f;
        const float last = 0.3125f;
        var effect = new TrailEffect2D
        {
            ExpectedPoints = 32, Spacing = spacing, Life = 10f,
            Width0 = 0.15f, Width1 = 0.15f, Opacity = 1f,
            FadeIn = 0f, FadeOut = 0f, Depth = DepthStencilState.None,
        };
        Assert.That(system.TryCreateInstance(effect, new Vector2(last - writtenPoints * spacing, 0f), out var trail), Is.True);

        Texture target = host.CreateColorTarget(256, 64);
        var context = new RenderContext(host.RenderingSystem);
        try
        {
            trail.ExtendTo(new Vector2(last, 0f), 0f);
            system.Update(0.1f);

            context.BeginFrame();
            system.PrepareRender(context);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc, new RenderPassSignature([Format.Rgba8Unorm])))
            {
                render.SetViewport(new Viewport { Width = 256, Height = 64 });
                render.SetScissor(new Scissor { Width = 256, Height = 64 });
                var vp = Matrix4x4.Identity;
                system.Render(context, render, in vp);
            }
            byte[] pixels = host.ReadBackRgba8AndSubmit(context, target, 256, 64);

            float first = last - (Math.Min(writtenPoints, 32) - 1) * spacing;
            for (int x = 0; x < 256; x++)
            {
                float worldX = (x + 0.5f) / 128f - 1f;
                byte alpha = pixels[(32 * 256 + x) * 4 + 3];
                if (worldX > first && worldX < last)
                    Assert.That(alpha, Is.GreaterThan(200), $"Missing live segment at x={worldX}, written={writtenPoints}.");
                else
                    Assert.That(alpha, Is.Zero, $"Stale geometry outside the live window at x={worldX}.");
            }
        }
        finally
        {
            trail.Dispose();
            context.Destroy();
            target.Destroy();
        }
    }

    /// <summary>The rendered envelope eases into and out of visibility while zero-duration ramps stay fully visible.</summary>
    [TestCase(0.05f, 0.2f, 0.5f, 40)]
    [TestCase(0.15f, 0.2f, 0.5f, 215)]
    [TestCase(0.625f, 0.2f, 0.5f, 215)]
    [TestCase(0.875f, 0.2f, 0.5f, 40)]
    [TestCase(0f, 0f, 0f, 255)]
    [TestCase(1f, 0f, 0f, 255)]
    [Category("AlcoGpu")]
    public void FadeEnvelopeRendersSmoothEndpoints(float age, float fadeIn, float fadeOut, int expectedAlpha)
    {
        using EffectsTestHost host = EffectsTestHost.CreateGpu();
        using var system = new GpuTrailSystem2D(host.RenderingSystem, host.Shaders, 32, 1);
        var effect = new TrailEffect2D
        {
            ExpectedPoints = 32, Spacing = 0.25f, Life = 1f,
            Width0 = 0.15f, Width1 = 0.15f, Opacity = 1f,
            FadeIn = fadeIn, FadeOut = fadeOut, Depth = DepthStencilState.None,
        };
        Assert.That(system.TryCreateInstance(effect, new Vector2(-0.75f, 0f), out var trail), Is.True);

        Texture target = host.CreateColorTarget(64, 65);
        var context = new RenderContext(host.RenderingSystem);
        try
        {
            trail.ExtendTo(new Vector2(0.5f, 0f), 0f);
            system.Update(age);

            context.BeginFrame();
            system.PrepareRender(context);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc, new RenderPassSignature([Format.Rgba8Unorm])))
            {
                render.SetViewport(new Viewport { Width = 64, Height = 65 });
                render.SetScissor(new Scissor { Width = 64, Height = 65 });
                var vp = Matrix4x4.Identity;
                system.Render(context, render, in vp);
            }
            byte[] pixels = host.ReadBackRgba8AndSubmit(context, target, 64, 65);

            Assert.That(pixels[(32 * 64 + 32) * 4 + 3], Is.EqualTo(expectedAlpha).Within(1));
        }
        finally
        {
            trail.Dispose();
            context.Destroy();
            target.Destroy();
        }
    }

    private static TrailEffectInstance2D CreateTrail(GpuTrailSystem2D system, int points)
    {
        Assert.That(system.TryCreateInstance(new TrailEffect2D { ExpectedPoints = points, Depth = DepthStencilState.None },
            Vector2.Zero, out TrailEffectInstance2D trail), Is.True, $"Could not allocate {points} points.");
        return trail;
    }

    private static Func<TrailEffect2D, object> GetMaterialLookup(GpuTrailSystem2D system)
    {
        return (Func<TrailEffect2D, object>)typeof(GpuTrailSystem2D)
            .GetMethod("GetOrCreateMaterial", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate(typeof(Func<TrailEffect2D, object>), system);
    }
}

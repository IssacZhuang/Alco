using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.Effects.Test;

/// <summary>
/// End-to-end GPU readback tests verifying 2D and 3D particle simulation,
/// indirect draw execution, row-major transforms, and bindless cross-texture batching.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("AlcoGpu")]
public sealed class TestParticleGpuReadback
{
    /// <summary>2D particle compute emit/simulate + indirect draw renders at the transformed world position.</summary>
    [Test]
    public void Particle2DSimulateAndRenderTransformsSpriteOnGpu()
    {
        using EffectsTestHost host = EffectsTestHost.CreateGpu();
        using Image red = host.CreateSolidImage(255, 0, 0, 255);
        using var system = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 256, emitterSlots: 8);

        var effect = new ParticleEffect2D
        {
            Name = "gpu2d-translate",
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = "Dot",
                    MaxParticles = 16,
                    Looping = true,
                    EmissionRate = 600f,
                    Lifetime = new ParticleRange(2f, 2f),
                    Speed = new ParticleRange(0f, 0f),
                    Size = new ParticleVector2Range(new Vector2(2f, 2f)),
                    StartColor = new ParticleColorRange(ColorFloat.White),
                    EndColor = ColorFloat.White,
                    FadeIn = 0f,
                    FadeOut = 0f,
                    Texture = red,
                    Blend = BlendState.Opaque,
                    Depth = DepthStencilState.None,
                },
            ],
        };

        // Emitter sits at x=+2, y=0 inside an [-4, 4] orthographic view -> rendered center at column 48/64.
        using ParticleEffectInstance2D instance = system.CreateInstance(effect, new Transform2D(new Vector2(2f, 0f)), height: 0f, seed: 42);
        var camera = new CameraData2D { Size = new Vector2(8f, 8f), Near = -10f, Far = 10f };

        Texture target = host.CreateColorTarget(64, 64);
        var context = new RenderContext(host.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.Submit().Wait();

            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc, new RenderPassSignature([Format.Rgba8Unorm])))
            {
                render.SetViewport(new Viewport { Width = 64, Height = 64 });
                render.SetScissor(new Scissor { Width = 64, Height = 64 });
                system.Render(context, render, camera);
            }
            byte[] pixels = host.ReadBackRgba8AndSubmit(context, target, 64, 64);

            int rightIdx = (32 * 64 + 48) * 4;
            int leftIdx = (32 * 64 + 16) * 4;
            Assert.Multiple(() =>
            {
                Assert.That(pixels[rightIdx + 0], Is.GreaterThan((byte)200), "Transformed particle must paint red at x=+2.");
                Assert.That(pixels[rightIdx + 1], Is.EqualTo((byte)0));
                Assert.That(pixels[rightIdx + 3], Is.EqualTo((byte)255));
                Assert.That(pixels[leftIdx + 3], Is.EqualTo((byte)0), "Left half must stay clear.");
            });
        }
        finally
        {
            context.Destroy();
            target.Destroy();
        }
    }

    /// <summary>Two 2D emitters with different textures merge into 1 DrawBatch and draw distinct colors in one DrawIndirect.</summary>
    [Test]
    public void BindlessCrossTextureBatchRendersDistinctColorsInSingleDraw()
    {
        using EffectsTestHost host = EffectsTestHost.CreateGpu();
        using Image red = host.CreateSolidImage(255, 0, 0, 255);
        using Image green = host.CreateSolidImage(0, 255, 0, 255);
        using var system = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 256, emitterSlots: 8);

        static ParticleEffect2D MakeQuad(string name, Image img) => new()
        {
            Name = name,
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = name,
                    MaxParticles = 16,
                    Looping = true,
                    EmissionRate = 600f,
                    Lifetime = new ParticleRange(2f, 2f),
                    Speed = new ParticleRange(0f, 0f),
                    Size = new ParticleVector2Range(new Vector2(2f, 2f)),
                    StartColor = new ParticleColorRange(ColorFloat.White),
                    EndColor = ColorFloat.White,
                    FadeIn = 0f,
                    FadeOut = 0f,
                    Texture = img,
                    Blend = BlendState.Opaque,
                    Depth = DepthStencilState.None,
                },
            ],
        };

        using ParticleEffectInstance2D leftInst = system.CreateInstance(MakeQuad("left", red), new Transform2D(new Vector2(-2f, 0f)), seed: 1);
        using ParticleEffectInstance2D rightInst = system.CreateInstance(MakeQuad("right", green), new Transform2D(new Vector2(2f, 0f)), seed: 2);
        var camera = new CameraData2D { Size = new Vector2(8f, 8f), Near = -10f, Far = 10f };

        Texture target = host.CreateColorTarget(64, 64);
        var context = new RenderContext(host.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.Submit().Wait();

            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            Assert.That(system.PlannedDrawBatchCount, Is.EqualTo(1), "Distinct textures using the default surface must share 1 DrawBatch.");

            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc, new RenderPassSignature([Format.Rgba8Unorm])))
            {
                render.SetViewport(new Viewport { Width = 64, Height = 64 });
                render.SetScissor(new Scissor { Width = 64, Height = 64 });
                system.Render(context, render, camera);
            }
            byte[] pixels = host.ReadBackRgba8AndSubmit(context, target, 64, 64);

            int leftIdx = (32 * 64 + 16) * 4;
            int rightIdx = (32 * 64 + 48) * 4;
            Assert.Multiple(() =>
            {
                Assert.That(pixels[leftIdx + 0], Is.GreaterThan((byte)200), "Left sprite must sample red texture.");
                Assert.That(pixels[leftIdx + 1], Is.EqualTo((byte)0));
                Assert.That(pixels[rightIdx + 1], Is.GreaterThan((byte)200), "Right sprite must sample green texture.");
                Assert.That(pixels[rightIdx + 0], Is.EqualTo((byte)0));
            });
        }
        finally
        {
            context.Destroy();
            target.Destroy();
        }
    }

    /// <summary>3D billboard particle simulation and indirect draw render centered through CameraDataPerspective.</summary>
    [Test]
    public void Particle3DSimulateAndRenderBillboardWithPerspectiveCameraOnGpu()
    {
        using EffectsTestHost host = EffectsTestHost.CreateGpu();
        using Image blue = host.CreateSolidImage(0, 0, 255, 255);
        using var system = new GpuParticleSystem3D(host.RenderingSystem, host.Shaders, particleCapacity: 256, emitterSlots: 8)
        {
            DepthStencilState = DepthStencilState.None,
        };

        var effect = new ParticleEffect3D
        {
            Name = "gpu3d-billboard",
            Groups =
            [
                new ParticleGroup3D
                {
                    Name = "Billboard",
                    MaxParticles = 16,
                    Looping = true,
                    EmissionRate = 600f,
                    Lifetime = new ParticleRange(2f, 2f),
                    Speed = new ParticleRange(0f, 0f),
                    Size = new ParticleRange(2f, 2f),
                    StartColor = new ParticleColorRange(ColorFloat.White),
                    EndColor = ColorFloat.White,
                    FadeIn = 0f,
                    FadeOut = 0f,
                    Texture = blue,
                    Blend = BlendState.Opaque,
                    Depth = DepthStencilState.None,
                },
            ],
        };

        // Engine 3D camera at (-4, 0, 0) looking along +X (identity rotation) sees emitter at (0, 0, 0) dead-center.
        using ParticleEffectInstance3D instance = system.CreateInstance(effect, new Transform3D(Vector3.Zero), seed: 9);
        var camera = new CameraDataPerspective
        {
            Transform = new Transform3D(new Vector3(-4f, 0f, 0f)),
            Fov = MathF.PI / 2f,
            AspectRatio = 1f,
            Near = 0.1f,
            Far = 50f,
        };

        Texture target = host.CreateColorTarget(64, 64);
        var context = new RenderContext(host.RenderingSystem);
        try
        {
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.Submit().Wait();

            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc, new RenderPassSignature([Format.Rgba8Unorm])))
            {
                render.SetViewport(new Viewport { Width = 64, Height = 64 });
                render.SetScissor(new Scissor { Width = 64, Height = 64 });
                system.Render(context, render, camera);
            }
            byte[] pixels = host.ReadBackRgba8AndSubmit(context, target, 64, 64);

            int centerIdx = (32 * 64 + 32) * 4;
            int cornerIdx = (4 * 64 + 4) * 4;
            Assert.Multiple(() =>
            {
                Assert.That(pixels[centerIdx + 2], Is.GreaterThan((byte)200), "Center pixel must render blue 3D billboard.");
                Assert.That(pixels[centerIdx + 0], Is.EqualTo((byte)0));
                Assert.That(pixels[centerIdx + 3], Is.EqualTo((byte)255));
                Assert.That(pixels[cornerIdx + 3], Is.EqualTo((byte)0), "Corner must stay clear.");
            });
        }
        finally
        {
            context.Destroy();
            target.Destroy();
        }
    }

    /// <summary>Pool growth between RecordSimulation and Render preserves indirect draw args and particle data on GPU.</summary>
    [Test]
    public void PoolGrowthBetweenRecordSimulationAndRenderRendersOnGpu()
    {
        using EffectsTestHost host = EffectsTestHost.CreateGpu();
        using Image red = host.CreateSolidImage(255, 0, 0, 255);
        using Image green = host.CreateSolidImage(0, 255, 0, 255);
        using var system = new GpuParticleSystem2D(host.RenderingSystem, host.Shaders, particleCapacity: 64, emitterSlots: 2);

        static ParticleEffect2D MakeQuad(string name, int maxParticles, Image img) => new()
        {
            Name = name,
            Groups =
            [
                new ParticleGroup2D
                {
                    Name = name,
                    MaxParticles = maxParticles,
                    Looping = true,
                    EmissionRate = 600f,
                    Lifetime = new ParticleRange(2f, 2f),
                    Speed = new ParticleRange(0f, 0f),
                    Size = new ParticleVector2Range(new Vector2(2f, 2f)),
                    StartColor = new ParticleColorRange(ColorFloat.White),
                    EndColor = ColorFloat.White,
                    FadeIn = 0f,
                    FadeOut = 0f,
                    Texture = img,
                    Blend = BlendState.Opaque,
                    Depth = DepthStencilState.None,
                },
            ],
        };

        using ParticleEffectInstance2D first = system.CreateInstance(MakeQuad("left-red", 64, red), new Transform2D(new Vector2(-2f, 0f)), seed: 1);
        ParticleEffectInstance2D? second = null;
        var camera = new Camera2D { Size = new Vector2(8f, 8f), Near = -10f, Far = 10f };

        Texture target = host.CreateColorTarget(64, 64);
        var context = new RenderContext(host.RenderingSystem);
        try
        {
            // Warm up one submitted frame so particles are live before mid-frame pool growth.
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            context.Submit().Wait();

            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);

            // Force particle pool AND slot growth before Render().
            second = system.CreateInstance(MakeQuad("right-green", 256, green), new Transform2D(new Vector2(2f, 0f)), seed: 2);

            var passDesc = new RenderingDesc
            {
                Colors = [new ColorAttachment { RenderView = target.RenderView, Load = LoadOp.Clear, Store = StoreOp.Store, Clear = Vector4.Zero }],
            };
            using (var render = context.BeginRenderPass(passDesc))
            {
                render.SetViewport(new Viewport { Width = 64, Height = 64 });
                render.SetScissor(new Scissor { Width = 64, Height = 64 });
                system.Render(context, render, camera);
            }
            byte[] pixels = host.ReadBackRgba8AndSubmit(context, target, 64, 64);
            int leftIdx = (32 * 64 + 16) * 4;
            Assert.That(pixels[leftIdx + 0], Is.GreaterThan((byte)200), "First frame after mid-frame growth must still render red quad.");

            // Next frame completes migration and renders both emitters.
            context.BeginFrame();
            system.RecordSimulation(context, 0.05f);
            using (var render = context.BeginRenderPass(passDesc))
            {
                render.SetViewport(new Viewport { Width = 64, Height = 64 });
                render.SetScissor(new Scissor { Width = 64, Height = 64 });
                system.Render(context, render, camera);
            }
            pixels = host.ReadBackRgba8AndSubmit(context, target, 64, 64);
            int rightIdx = (32 * 64 + 48) * 4;
            Assert.Multiple(() =>
            {
                Assert.That(pixels[leftIdx + 0], Is.GreaterThan((byte)200), "Migrated red emitter must keep rendering.");
                Assert.That(pixels[rightIdx + 1], Is.GreaterThan((byte)200), "Newly grown green emitter must render.");
            });
        }
        finally
        {
            second?.Dispose();
            context.Destroy();
            target.Destroy();
        }
    }
}

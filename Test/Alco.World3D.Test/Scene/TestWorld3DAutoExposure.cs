using System.Numerics;
using Alco.Graphics;
using Alco.Graphics.LowLevel;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>Verifies <see cref="World3DScene"/> automatic exposure integration, debug-view bypass, reset, and VRAM cleanup.</summary>
[TestFixture]
public sealed class TestWorld3DAutoExposure
{
    private static readonly World3DCamera s_camera = new(default, Quaternion.Identity, MathF.PI / 2f, 0.1f);

    /// <summary>Verifies default enabled state and automatic bypass when a lighting debug view is active.</summary>
    [Test]
    public void NoGpu_DefaultSettings_And_DebugViewDisable()
    {
        GPUDevice device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.NoGPU, Headless = true });
        try
        {
            using RenderingSystem system = new(device, textureHeapByteCount: 2 * 1024 * 1024, bufferHeapByteCount: 2 * 1024 * 1024, uploadStagingByteCount: 64 * 1024);
            using RenderContext context = new(system);
            using World3DScene scene = new(system);
            using Texture output = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(64, 64, 1),
                Format = Format.Rgba8Srgb,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            });

            Assert.That(scene.AutoExposure.Enabled, Is.True);

            context.BeginFrame();
            Assert.That(scene.TryRecordFrame(context, in s_camera, new Extent2D(64, 64), out _, RenderGraphTarget.FromTexture(output), RGUsage.TransferRead), Is.True);
            context.SubmitWithCompletion().Wait();
            Assert.That(scene.LastRecordedAutoExposure.Enabled, Is.True);

            scene.LightingDebugView = World3DLightingDebugView.ShadowFactor;
            context.BeginFrame();
            Assert.That(scene.TryRecordFrame(context, in s_camera, new Extent2D(64, 64), out _, RenderGraphTarget.FromTexture(output), RGUsage.TransferRead), Is.True);
            context.SubmitWithCompletion().Wait();
            Assert.That(scene.LastRecordedAutoExposure.Enabled, Is.False);
        }
        finally
        {
            device.Destroy();
        }
    }

    /// <summary>Verifies bright-vs-dim adaptation, immediate reset, telemetry delivery, and zero-leak disposal on VulkanCompact.</summary>
    [Test]
    [NonParallelizable]
    [Category("AlcoGpu")]
    [Category("VulkanCompact")]
    public void VulkanCompact_BrightVsDimLighting_Reset_And_ZeroLeakDispose()
    {
        GPUDevice device;
        try
        {
            device = Gpu.CreateDevice(new DeviceDesc { Backend = GraphicsBackend.VulkanCompact, Headless = true });
        }
        catch (GPUException ex) when (ex.Error == Error.Unsupported)
        {
            Assert.Ignore($"VulkanCompact is unsupported on this host: {ex.Message}");
            return;
        }

        RenderingSystem system = new(device, textureHeapByteCount: 32 * 1024 * 1024, bufferHeapByteCount: 32 * 1024 * 1024, uploadStagingByteCount: 512 * 1024);
        RenderContext context = new(system, frameDataByteCount: 2 * 1024 * 1024);
        try
        {
            RenderingMemoryStats preStats = system.GetMemoryStats();

            using (Texture output = system.CreateTexture(new TextureDesc
            {
                Extent = new Extent3D(128, 128, 1),
                Format = Format.Rgba8Srgb,
                Usage = TextureUsage.ColorAttachment | TextureUsage.TransferSource,
            }))
            using (World3DScene scene = new(system))
            {
                scene.Shadows.Enabled = false;
                scene.Occlusion.Enabled = false;
                scene.VoxelGi.Enabled = false;
                scene.Forward.Enabled = false;
                scene.VolumetricClouds.Enabled = false;
                scene.VolumetricLight.Enabled = false;
                scene.Ssr.Enabled = false;
                scene.AutoExposure.TelemetryEnabled = true;

                // Bright outdoor sky.
                scene.Lighting = new World3DLighting
                {
                    SunIntensity = 20f,
                    SkyExposure = 2.0f,
                    AmbientFloor = 1.5f,
                };
                SubmitFrames(scene, context, output, 3);
                Assert.That(scene.TryReadAutoExposureState(out AutoExposureState brightState), Is.True);
                Assert.That(scene.LastRecordedAutoExposure.HasTelemetry, Is.True);
                Assert.That(scene.LastAutoExposureHistogram.Length, Is.EqualTo(RGNode_AutoExposure.BinCount));

                // Dim indoor/night lighting + ResetAutoExposure snaps cleanly.
                scene.Lighting = new World3DLighting
                {
                    SunIntensity = 0.05f,
                    SkyExposure = 0.04f,
                    NightFloor = 0.01f,
                    AmbientFloor = 0.02f,
                    SunDiscBrightness = 0f,
                };
                scene.ResetAutoExposure();
                SubmitFrames(scene, context, output, 2);
                Assert.That(scene.TryReadAutoExposureState(out AutoExposureState dimState), Is.True);

                Assert.That(dimState.Exposure, Is.GreaterThan(brightState.Exposure * 2.0f));
                Assert.That(dimState.AdaptedEV, Is.LessThan(brightState.AdaptedEV));
            }

            device.WaitIdle();
            system.Tick();
            RenderingMemoryStats postStats = system.GetMemoryStats();
            Assert.That(postStats.LiveBuffers, Is.EqualTo(preStats.LiveBuffers));
            Assert.That(postStats.GpuBufferPool.UsedBytes, Is.EqualTo(preStats.GpuBufferPool.UsedBytes));
        }
        finally
        {
            device.WaitIdle();
            context.Dispose();
            system.Dispose();
            device.Destroy();
        }
    }

    private static void SubmitFrames(World3DScene scene, RenderContext context, Texture output, int count)
    {
        for (int i = 0; i < count; ++i)
        {
            context.BeginFrame();
            Assert.That(scene.TryRecordFrame(
                context,
                in s_camera,
                new Extent2D(output.Extent.X, output.Extent.Y),
                out _,
                RenderGraphTarget.FromTexture(output),
                RGUsage.TransferRead,
                renderWorld: true,
                deltaTime: 1f / 30f), Is.True);
            context.SubmitWithCompletion().Wait();
        }
    }
}

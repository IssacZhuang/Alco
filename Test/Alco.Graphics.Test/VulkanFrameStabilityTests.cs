using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Graphics.Test;

/// <summary>
/// Runs a headless Vulkan frame loop for many frames and verifies the per-frame
/// time stays flat. Regresses slow leaks (unbounded lists, leaked native
/// objects) that show up as an ever-degrading frame rate.
/// </summary>
[TestFixture]
public sealed class VulkanFrameStabilityTests
{
    private sealed class HeadlessHost : IGPUDeviceHost
    {
        public event Action? EndFrame;
        public event Action? Disposing;

        event Action IGPUDeviceHost.OnEndFrame
        {
            add => EndFrame += value;
            remove => EndFrame -= value;
        }

        event Action IGPUDeviceHost.OnDispose
        {
            add => Disposing += value;
            remove => Disposing -= value;
        }

        public int InfoCount;
        public int WarningCount;
        public int ErrorCount;

        public void FireEndFrame() => EndFrame?.Invoke();
        public void FireDispose() => Disposing?.Invoke();

        public void LogInfo(ReadOnlySpan<char> message) => InfoCount++;
        public void LogWarning(ReadOnlySpan<char> message) => WarningCount++;
        public void LogError(ReadOnlySpan<char> message) => ErrorCount++;
        public void LogSuccess(ReadOnlySpan<char> message) { }
    }

    [Test(Description = "Per-frame time of an idle render loop must not grow over time")]
    [Category("Vulkan")]
    public void FrameTimeRemainsFlatAcrossManyFrames()
        => RunBatchedLoop("idle", null);

    [Test(Description = "Per-frame buffer uploads (uniform writes) must not degrade frame time")]
    [Category("Vulkan")]
    public void FrameTimeRemainsFlatWithPerFrameBufferWrites()
    {
        GPUBuffer? hostVisible = null;
        GPUBuffer? deviceLocal = null;
        try
        {
            RunBatchedLoop("buffer_writes", device =>
            {
                hostVisible = device.CreateBuffer(new BufferDescriptor(
                    256, BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.MapWrite, "stability_uniform_host"));
                deviceLocal = device.CreateBuffer(new BufferDescriptor(
                    256, BufferUsage.Uniform | BufferUsage.CopyDst, "stability_uniform_device"));
            }, device =>
            {
                // host-visible path (persistent mapping) and device-local path
                // (frame arena staging -> deferred one-shot submit)
                device.WriteBuffer(hostVisible!, 0, new byte[128]);
                device.WriteBuffer(deviceLocal!, 0, new byte[128]);
            });
        }
        finally
        {
            hostVisible?.Dispose();
            deviceLocal?.Dispose();
        }
    }

    [Test(Description = "Per-frame time with a real swapchain acquire/present cycle must not grow")]
    [Category("Vulkan")]
    [Platform("Win", Reason = "Requires a Win32 window for the Vulkan surface")]
    public void FrameTimeRemainsFlatWithSwapchainPresent()
    {
        IntPtr hinstance = GetModuleHandleW(null);
        IntPtr hwnd = CreateWindowExW(
            0, "STATIC", "vulkan frame stability", WS_OVERLAPPEDWINDOW,
            0, 0, 256, 256, IntPtr.Zero, IntPtr.Zero, hinstance, IntPtr.Zero);
        Assert.That(hwnd, Is.Not.EqualTo(IntPtr.Zero), "failed to create the test window");

        HeadlessHost host = new();
        GPUDevice device;
        try
        {
            device = GraphicsDeviceFactory.CreateVulkanDevice(
                new DeviceDescriptor(host, GraphicsBackend.NativeVulkan, name: "frame_stability_present"));
        }
        catch (Exception e)
        {
            DestroyWindow(hwnd);
            Assert.Ignore($"Vulkan device unavailable: {e.Message}");
            return;
        }

        try
        {
            Win32SurfaceSource source = new(hwnd, hinstance);
            GPUSwapchain swapchain = device.CreateSwapchain(new SwapchainDescriptor(
                source, PixelFormat.BGRA8Unorm, null,
                new System.Numerics.Vector4(0, 0, 0, 1), 256, 256,
                isVSyncEnabled: false, "stability_swapchain"));
            GPUCommandBuffer commandBuffer = device.CreateCommandBuffer("stability_command_buffer");
            bool mainSubmit = Environment.GetEnvironmentVariable("STRESS_NOSUBMIT") != "1";
            System.Numerics.Vector4 clearColor = new(0, 0, 0, 1);

            const int batchCount = 12;
            const int framesPerBatch = 1000;
            double[] batchMs = new double[batchCount];
            Stopwatch stopwatch = Stopwatch.StartNew();
            int skipped = 0;

            for (int batch = 0; batch < batchCount; batch++)
            {
                stopwatch.Restart();
                for (int frame = 0; frame < framesPerBatch; frame++)
                {
                    if (!swapchain.RequestSurfaceTexture())
                    {
                        skipped++;
                        host.FireEndFrame();
                        continue;
                    }
                    if (mainSubmit)
                    {
                        commandBuffer.Begin();
                        using (commandBuffer.BeginRender(swapchain.FrameBuffer!, clearColor))
                        {
                        }
                        commandBuffer.End();
                        device.Submit(commandBuffer);
                    }
                    swapchain.Present();
                    host.FireEndFrame();
                }
                batchMs[batch] = stopwatch.Elapsed.TotalMilliseconds / framesPerBatch;
            }

            TestContext.Out.WriteLine($"[present] frame time per batch (ms), skipped={skipped}:");
            for (int batch = 0; batch < batchCount; batch++)
            {
                TestContext.Out.WriteLine($"  batch {batch,2}: {batchMs[batch]:F4}");
            }
            TestContext.Out.WriteLine(
                $"host log calls: info={host.InfoCount}, warning={host.WarningCount}, error={host.ErrorCount}");

            Assert.That(
                batchMs[^1],
                Is.LessThan(batchMs[0] * 1.5 + 0.05),
                $"frame time degraded: first batch {batchMs[0]:F4} ms, last batch {batchMs[^1]:F4} ms");
        }
        finally
        {
            host.FireDispose();
            DestroyWindow(hwnd);
        }
    }

    [Test(Description = "Two command buffers alternating submissions (overlay + main) must not degrade frame time")]
    [Category("Vulkan")]
    public void FrameTimeRemainsFlatWithTwoInterleavedCommandBuffers()
        => RunBatchedLoop("two_buffers", null, null, twoCommandBuffers: true);

    private static void RunBatchedLoop(
        string label,
        Action<GPUDevice>? setup,
        Action<GPUDevice>? extraPerFrame = null,
        bool twoCommandBuffers = false)
    {
        HeadlessHost host = new();
        GPUDevice device;
        try
        {
            device = GraphicsDeviceFactory.CreateVulkanDevice(
                new DeviceDescriptor(host, GraphicsBackend.NativeVulkan, name: $"frame_stability_{label}"));
        }
        catch (Exception e)
        {
            Assert.Ignore($"Vulkan device unavailable: {e.Message}");
            return;
        }

        try
        {
            GPUAttachmentLayout layout = device.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
                new ColorAttachment[] { new(PixelFormat.BGRA8Unorm) }, null, "stability_layout"));
            GPUFrameBuffer frameBuffer = device.CreateFrameBuffer(
                new FrameBufferDescriptor(layout, 256, 256, "stability_frame_buffer"));
            GPUCommandBuffer commandBuffer = device.CreateCommandBuffer("stability_command_buffer");
            GPUCommandBuffer? overlayBuffer = twoCommandBuffers
                ? device.CreateCommandBuffer("stability_overlay_command_buffer")
                : null;
            setup?.Invoke(device);

            const int batchCount = 12;
            const int framesPerBatch = 1000;
            double[] batchMs = new double[batchCount];
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int batch = 0; batch < batchCount; batch++)
            {
                stopwatch.Restart();
                for (int frame = 0; frame < framesPerBatch; frame++)
                {
                    if (overlayBuffer != null)
                    {
                        // overlay context records and submits first, then the
                        // main buffer records and submits into the same target
                        overlayBuffer.Begin();
                        using (overlayBuffer.BeginRender(frameBuffer, new System.Numerics.Vector4(0, 0, 0, 1)))
                        {
                        }
                        overlayBuffer.End();
                        device.Submit(overlayBuffer);
                    }
                    commandBuffer.Begin();
                    using (commandBuffer.BeginRender(frameBuffer, new System.Numerics.Vector4(0, 0, 0, 1)))
                    {
                    }
                    commandBuffer.End();
                    device.Submit(commandBuffer);
                    extraPerFrame?.Invoke(device);
                    host.FireEndFrame();
                }
                batchMs[batch] = stopwatch.Elapsed.TotalMilliseconds / framesPerBatch;
            }

            TestContext.Out.WriteLine($"[{label}] frame time per batch (ms):");
            for (int batch = 0; batch < batchCount; batch++)
            {
                TestContext.Out.WriteLine($"  batch {batch,2}: {batchMs[batch]:F4}");
            }

            Assert.That(
                batchMs[^1],
                Is.LessThan(batchMs[0] * 1.5 + 0.05),
                $"frame time degraded: first batch {batchMs[0]:F4} ms, last batch {batchMs[^1]:F4} ms");
        }
        finally
        {
            host.FireDispose();
        }
    }

    #region Win32 interop

    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);

    #endregion
}

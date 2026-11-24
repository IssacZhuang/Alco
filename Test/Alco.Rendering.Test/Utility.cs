using Alco.Graphics;
using Alco.Graphics.LowLevel;

namespace Alco.Rendering.Test;

internal sealed class RenderingTestHost : IDisposable
{
    public GPUDevice Device { get; }
    public RenderingSystem RenderingSystem { get; }

    public RenderingTestHost(GraphicsBackend backend = GraphicsBackend.NoGPU)
    {
        Device = Gpu.CreateDevice(new DeviceDesc { Backend = backend, Headless = true });
        RenderingSystem = new RenderingSystem(Device, textureHeapByteCount: 16 * 1024 * 1024);
    }

    public void Dispose()
    {
        Device.WaitIdle();
        RenderingSystem.Dispose();
        Device.Destroy();
    }
}

internal static class Utility
{
    internal static RenderingTestHost CreateRenderingSystem(GraphicsBackend backend = GraphicsBackend.NoGPU)
    {
        return new RenderingTestHost(backend);
    }
}

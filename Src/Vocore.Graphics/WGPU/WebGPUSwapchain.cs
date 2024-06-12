using System.Runtime.CompilerServices;
using WebGPU;
using static WebGPU.WebGPU;
using System.Runtime.InteropServices;

namespace Vocore.Graphics.WebGPU;

internal class WebGPUSwapchain : GPUSwapcahin
{
    private readonly WGPUSurface _surface;
    public WebGPUSwapchain(WebGPUDevice device, SwapcahinDescriptor descriptor)
    {
        _surface = device.Instance.CreateSurface(descriptor.Source);
    }

    public override GPUFrameBuffer FrameBuffer => throw new NotImplementedException();

    public override SurfaceSource SurfaceSource => throw new NotImplementedException();

    public override bool IsVSyncEnabled { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public override string Name => throw new NotImplementedException();

    protected override GPUDevice Device => throw new NotImplementedException();

    public override void Present()
    {
        throw new NotImplementedException();
    }

    public override void Resize(uint width, uint height)
    {
        throw new NotImplementedException();
    }

    protected override void Dispose(bool disposing)
    {
        throw new NotImplementedException();
    }
}
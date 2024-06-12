namespace Vocore.Graphics.NoGPU;

internal class NoSwapchain : GPUSwapchain
{
    public override string Name => "no_gpu_swapchain";

    public override GPUFrameBuffer FrameBuffer => NoDevice.noFrameBuffer;

    public override bool IsVSyncEnabled { get; set; }

    protected override GPUDevice Device => NoDevice.noDevice;

    public override void Present()
    {
        
    }

    public override void Resize(uint width, uint height)
    {
        
    }

    protected override void Dispose(bool disposing)
    {

    }
}
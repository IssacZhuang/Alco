namespace Vocore.Graphics.NoGPU;

internal class NoSwapchain : GPUSwapchain
{
    public override string Name => "no_gpu_swapchain";

    public override GPUFrameBuffer FrameBuffer { get; }

    public override bool IsVSyncEnabled { get; set; }

    protected override GPUDevice Device => NoDevice.noDevice;

    public NoSwapchain(in SwapchainDescriptor descriptor)
    {
        GPURenderPass renderPass = new NoRenderPass(new RenderPassDescriptor(
            [new ColorAttachment(descriptor.ColorFormat)],
            descriptor.DepthFormat is not null ? new DepthAttachment(descriptor.DepthFormat.Value) : null,
            descriptor.Name
        ));
        FrameBuffer = new NoFrameBuffer(new FrameBufferDescriptor(
            renderPass,
            descriptor.Width,
            descriptor.Height,
            descriptor.Name
        ));
    }

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
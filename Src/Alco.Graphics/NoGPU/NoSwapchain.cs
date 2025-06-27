namespace Alco.Graphics.NoGPU;

internal class NoSwapchain : GPUSwapchain
{

    public override GPUFrameBuffer FrameBuffer { get; }

    public override bool IsVSyncEnabled { get; set; }

    protected override GPUDevice Device => NoDevice.noDevice;

    public NoSwapchain(in SwapchainDescriptor descriptor): base(descriptor)
    {
        GPUAttachmentLayout attachmentLayout = new NoAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(descriptor.ColorFormat)],
            descriptor.DepthFormat is not null ? new DepthAttachment(descriptor.DepthFormat.Value) : null,
            descriptor.Name
        ));
        FrameBuffer = new NoFrameBuffer(new FrameBufferDescriptor(
            attachmentLayout,
            descriptor.Width,
            descriptor.Height,
            descriptor.Name
        ));
    }

    public override bool RequestSurfaceTexture()
    {
        return true;
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
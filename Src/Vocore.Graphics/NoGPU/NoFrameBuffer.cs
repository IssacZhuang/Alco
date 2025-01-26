
namespace Vocore.Graphics.NoGPU;

internal class NoFrameBuffer : GPUFrameBuffer
{

    protected override GPUDevice Device => NoDevice.noDevice;
    private readonly NoTexture[] NoColors;
    private readonly NoTextureView[] NoColorViews;
    public override GPURenderPass RenderPass { get; }

    public override ReadOnlySpan<GPUTexture> Colors => NoColors; // at least one element to prevent out of range exception

    public override GPUTexture? DepthStencil { get; }

    public override GPUTextureView? DepthStencilView { get; }

    public override GPUTextureView? DepthView { get; }

    public override GPUTextureView? StencilView { get; }

    public override uint Width { get; }

    public override uint Height { get; }

    public override ReadOnlySpan<GPUTextureView> ColorViews => NoColorViews; // at least one element to prevent out of range exception

    public NoFrameBuffer(in FrameBufferDescriptor descriptor): base("no_gpu_frame_buffer")
    {
        RenderPass = descriptor.RenderPass;
        Width = descriptor.Width;
        Height = descriptor.Height;

        NoTexture texture = new(new TextureDescriptor(
            TextureDimension.Texture2D,
            PixelFormat.RGBA8Unorm,
            Width,
            Height,
            1,
            1,
            ColorAttachmentUsage,
            1,
            "no_gpu_frame_buffer_color_texture"
        )); 

        NoColors = [texture];

        NoColorViews = [new(new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D))];

        if (RenderPass.Depth != null)
        {
            NoTexture depthTexture = new(new TextureDescriptor(
                TextureDimension.Texture2D,
                PixelFormat.Depth32Float,
                Width,
                Height,
                1,
                1,
                DepthAttachmentUsage,
                1,
                "no_gpu_frame_buffer_depth_texture"
            ));

            DepthStencil = depthTexture;

            DepthStencilView = new NoTextureView(new TextureViewDescriptor(
                depthTexture,
                TextureViewDimension.Texture2D
                ));

            DepthView = new NoTextureView(new TextureViewDescriptor(
                depthTexture,
                TextureViewDimension.Texture2D,
                aspect: TextureAspect.DepthOnly
                ));

            StencilView = new NoTextureView(new TextureViewDescriptor(
                depthTexture,
                TextureViewDimension.Texture2D,
                aspect: TextureAspect.StencilOnly
                ));
        }
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
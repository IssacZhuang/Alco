
namespace Alco.Graphics.NoGPU;

internal class NoFrameBuffer : GPUFrameBuffer
{

    protected override GPUDevice Device => NoDevice.noDevice;
    private readonly GPUTexture[] NoColors;
    private readonly GPUTextureView[] NoColorViews;
    public override GPUAttachmentLayout AttachmentLayout { get; }

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
        AttachmentLayout = descriptor.AttachmentLayout;
        Width = descriptor.Width;
        Height = descriptor.Height;

        // One stub texture per color attachment of the layout (at least one element
        // to prevent out of range exceptions on color-less layouts).
        int colorCount = Math.Max(AttachmentLayout.Colors.Length, 1);
        NoColors = new GPUTexture[colorCount];
        NoColorViews = new GPUTextureView[colorCount];
        for (int i = 0; i < colorCount; i++)
        {
            PixelFormat format = i < AttachmentLayout.Colors.Length
                ? AttachmentLayout.Colors[i].Format
                : PixelFormat.RGBA8Unorm;
            NoTexture texture = new(new TextureDescriptor(
                TextureDimension.Texture2D,
                format,
                Width,
                Height,
                1,
                1,
                ColorAttachmentUsage,
                1,
                "no_gpu_frame_buffer_color_texture"
            ));
            NoColors[i] = texture;
            NoColorViews[i] = new NoTextureView(new TextureViewDescriptor(
                texture,
                TextureViewDimension.Texture2D));
        }

        if (AttachmentLayout.Depth != null)
        {
            NoTexture depthTexture = new(new TextureDescriptor(
                TextureDimension.Texture2D,
                AttachmentLayout.Depth.Value.Format,
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

    public NoFrameBuffer(in ExternalFrameBufferDescriptor descriptor): base(descriptor.Name)
    {
        AttachmentLayout = descriptor.AttachmentLayout;
        Width = descriptor.Width;
        Height = descriptor.Height;

        NoColors = descriptor.Colors;
        NoColorViews = descriptor.ColorViews;
        DepthStencil = descriptor.DepthStencil;
        DepthStencilView = descriptor.DepthStencilView;
        DepthView = descriptor.DepthView;
        StencilView = descriptor.StencilView;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
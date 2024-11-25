
namespace Vocore.Graphics.NoGPU;

internal class NoFrameBuffer : GPUFrameBuffer
{
    private readonly NoTexture[] NoColors;
    private readonly NoTextureView[] NoColorViews;
    protected override GPUDevice Device => NoDevice.noDevice;
    public override GPURenderPass RenderPass { get; }

    public override ReadOnlySpan<GPUTexture> Colors => NoColors; // at least one element to prevent out of range exception

    public override GPUTexture? Depth => null;

    public override uint Width { get; }

    public override uint Height { get; }

    public override ReadOnlySpan<GPUTextureView> ColorViews => NoColorViews; // at least one element to prevent out of range exception

    public override GPUTextureView? DepthView => null;

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
            TextureUsage.TextureBinding | TextureUsage.Write,
            1,
            "no_gpu_frame_buffer_color_texture"
        )); 

        NoColors = [texture];

        NoColorViews = [new(new TextureViewDescriptor(
            texture,
            TextureViewDimension.Texture2D,
            0,
            1,
            0,
            1
            ))];
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
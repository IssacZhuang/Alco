
namespace Vocore.Graphics.NoGPU;

internal class NoFrameBuffer : GPUFrameBuffer
{
    private static readonly NoTexture[] NoColors = new NoTexture[1] { new() };
    private readonly NoTextureView[] NoColorViews;
    public override string Name => "no_gpu_frame_buffer";
    protected override GPUDevice Device => NoDevice.noDevice;
    public override GPURenderPass RenderPass { get; }

    public override ReadOnlySpan<GPUTexture> Colors => NoColors; // at least one element to prevent out of range exception

    public override GPUTexture? Depth => null;

    public override uint Width { get; }

    public override uint Height { get; }

    public override ReadOnlySpan<GPUTextureView> ColorViews => NoColorViews; // at least one element to prevent out of range exception

    public override GPUTextureView? DepthView => null;

    public NoFrameBuffer(in FrameBufferDescriptor descriptor)
    {
        RenderPass = descriptor.RenderPass;
        Width = descriptor.Width;
        Height = descriptor.Height;

        NoColorViews = [new(new TextureViewDescriptor(
            NoColors[0],
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
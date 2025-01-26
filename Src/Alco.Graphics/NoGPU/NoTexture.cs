namespace Alco.Graphics.NoGPU;

internal class NoTexture : GPUTexture
{
    public override uint Width { get; }

    public override uint Height { get; }
    public override uint Depth { get; }

    public override uint MipLevelCount { get; }

    public override PixelFormat PixelFormat { get; }

    protected override GPUDevice Device => NoDevice.noDevice;

    public NoTexture(in TextureDescriptor descriptor): base(descriptor)
    {
        Width = descriptor.Width;
        Height = descriptor.Height;
        Depth = descriptor.DepthOrArrayLayer;
        MipLevelCount = descriptor.MipLevels;
        PixelFormat = descriptor.Format;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
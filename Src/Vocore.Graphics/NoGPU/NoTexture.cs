namespace Vocore.Graphics.NoGPU;

internal class NoTexture : GPUTexture
{
    public override uint Width => 0;

    public override uint Height => 0;

    public override uint Depth => 0;

    public override uint MipLevelCount => 1;

    public override PixelFormat PixelFormat => PixelFormat.RGBA8Unorm;

    protected override GPUDevice Device => NoDevice.noDevice;

    public NoTexture(in TextureDescriptor descriptor): base(descriptor)
    {
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
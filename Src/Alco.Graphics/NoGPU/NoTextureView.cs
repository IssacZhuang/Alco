namespace Alco.Graphics.NoGPU;

internal class NoTextureView : GPUTextureView
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public override GPUTexture Texture { get; }

    public NoTextureView(in TextureViewDescriptor descriptor): base(descriptor)
    {
        Texture = descriptor.Texture;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}

namespace Vocore.Graphics.NoGPU;

internal class NoTextureView : GPUTextureView
{

    public override string Name => "no_gpu_texture_view";
    protected override GPUDevice Device => NoDevice.noDevice;

    public override GPUTexture Texture { get; }

    public NoTextureView(in TextureViewDescriptor descriptor)
    {
        Texture = descriptor.Texture;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}

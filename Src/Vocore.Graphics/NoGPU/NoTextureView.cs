namespace Vocore.Graphics.NoGPU;

internal class NoTextureView : GPUTextureView
{
    private static readonly NoTexture noTexture = new NoTexture();
    public override GPUTexture Texture => noTexture;

    public override TextureViewDimension Dimension => TextureViewDimension.Undefined;

    public override string Name => "no_gpu_texture_view";
    protected override GPUDevice Device => NoDevice.noDevice;
    protected override void Dispose(bool disposing)
    {
        
    }
}

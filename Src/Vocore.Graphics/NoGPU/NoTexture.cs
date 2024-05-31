namespace Vocore.Graphics.NoGPU;

internal class NoTexture : GPUTexture
{
    public override uint Width => 0;

    public override uint Height => 0;

    public override uint Depth => 0;

    public override uint MipLevelCount => 1;

    public override string Name => "no_gpu_texture";
    protected override GPUDevice Device => NoDevice.noDevice;
    protected override void Dispose(bool disposing)
    {
        
    }
}
namespace Vocore.Graphics.NoGPU;

internal class NoSampler : GPUSampler
{
    public override string Name => "no_gpu_sampler";
    protected override GPUDevice Device => NoDevice.noDevice;
    protected override void Dispose(bool disposing)
    {
        
    }
}
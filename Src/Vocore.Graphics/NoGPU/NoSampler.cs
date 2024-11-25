namespace Vocore.Graphics.NoGPU;

internal class NoSampler : GPUSampler
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoSampler(in SamplerDescriptor descriptor): base(descriptor){
        
    }
    protected override void Dispose(bool disposing)
    {
        
    }
}
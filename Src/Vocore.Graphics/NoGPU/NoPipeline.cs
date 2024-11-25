namespace Vocore.Graphics.NoGPU;

internal class NoPipeline : GPUPipeline
{
    protected override GPUDevice Device => NoDevice.noDevice;

    public NoPipeline(in GraphicsPipelineDescriptor descriptor): base(descriptor)
    {
    }

    public NoPipeline(in ComputePipelineDescriptor descriptor): base(descriptor)
    {
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
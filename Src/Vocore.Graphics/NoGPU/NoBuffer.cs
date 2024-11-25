


namespace Vocore.Graphics.NoGPU;

internal class NoBuffer : GPUBuffer
{
    public NoBuffer(in BufferDescriptor descriptor) : base(descriptor)
    {
    }

    public override string Name => "no_gpu_buffer";

    protected override GPUDevice Device => NoDevice.noDevice;


    protected override void Dispose(bool disposing)
    {
        
    }
}
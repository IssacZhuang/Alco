


namespace Alco.Graphics.NoGPU;

internal class NoBuffer : GPUBuffer
{
    public NoBuffer(in BufferDescriptor descriptor) : base(descriptor)
    {
    }

    protected override GPUDevice Device => NoDevice.noDevice;


    protected override void Dispose(bool disposing)
    {
        
    }
}
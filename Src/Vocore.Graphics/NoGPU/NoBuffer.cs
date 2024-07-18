


namespace Vocore.Graphics.NoGPU;

internal class NoBuffer : GPUBuffer
{
    public override uint Size => 0;

    public override BufferUsage Usage => BufferUsage.None;

    public override BindableResourceType ResourceType => BindableResourceType.Buffer;

    public override string Name => "no_gpu_buffer";

    protected override GPUDevice Device => NoDevice.noDevice;

    public override unsafe void GetData(void* dest, uint offset, uint size)
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
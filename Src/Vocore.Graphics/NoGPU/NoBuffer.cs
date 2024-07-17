


namespace Vocore.Graphics.NoGPU;

internal class NoBuffer : GPUBuffer
{
    public override uint Size => 0;

    public override BufferUsage Usage => BufferUsage.None;

    public override BindableResourceType ResourceType => BindableResourceType.Buffer;

    public override string Name => "no_gpu_buffer";

    protected override GPUDevice Device => NoDevice.noDevice;

    public override unsafe bool TryGetMappedDataPointer(out void* ptr)
    {
        ptr = null;
        return false;
    }

    public override bool TryMapAsync(uint offset, uint size)
    {
        return false;
    }

    public override bool TryUnmap()
    {
        return false;
    }

    public override void WaitForMapCompletion()
    {
        
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
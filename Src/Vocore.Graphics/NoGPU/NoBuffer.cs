

namespace Vocore.Graphics.NoGPU;

internal class NoBuffer : GPUBuffer
{
    public override uint Size => 0;

    public override BufferUsage Usage => BufferUsage.None;

    public override BindableResourceType ResourceType => BindableResourceType.Buffer;

    public override string Name => "no_gpu_buffer";

    protected override GPUDevice Device => NoDevice.noDevice;

    public override Span<byte> GetData(uint offset, uint size)
    {
        return Span<byte>.Empty;
    }

    public override void GetDataAsync(uint offset, uint size, AsyncReadBufferCallback callback)
    {
        callback.Invoke(Span<byte>.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}
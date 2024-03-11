namespace Vocore.Graphics.NoGPU;

internal class NoBuffer : GPUBuffer
{
    public override uint Size => 0;

    public override BufferUsage Usage => BufferUsage.None;

    public override BindableResourceType ResourceType => BindableResourceType.Buffer;

    public override string Name => "no_gpu_buffer";

    protected override void Dispose(bool disposing)
    {
        
    }
}
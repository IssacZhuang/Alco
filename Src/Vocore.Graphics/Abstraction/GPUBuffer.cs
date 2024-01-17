namespace Vocore.Graphics;

public abstract class GPUBuffer: BaseGPUObject
{
    public abstract uint Size { get; }
    public abstract BufferUsage Usage { get; }
}
namespace Vocore.Graphics;


/// <summary>
/// The buffer in the VRAM
/// </summary> 
public abstract class GPUBuffer: BaseGPUObject, IGPUBindableResource
{
    public abstract uint Size { get; }
    public abstract BufferUsage Usage { get; }
    public abstract BindableResourceType ResourceType { get; }
}
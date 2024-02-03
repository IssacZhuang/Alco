namespace Vocore.Graphics;

public abstract class GPUTexture : BaseGPUObject, IGPUBindableResource
{
    public abstract uint Width { get; }
    public abstract uint Height { get; }
    public abstract uint Depth { get; }
    public abstract BindableResourceType ResourceType { get; }
}
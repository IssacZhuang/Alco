namespace Vocore.Graphics;

public abstract class GpuBindingGroup : BaseGPUObject
{
    public abstract IReadOnlyList<IGPUBindableResource> Resources { get; init; }
}
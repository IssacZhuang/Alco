namespace Alco.Graphics;

/// <summary>
/// The group to bind resources to a shader.
/// </summary>
public abstract class GPUResourceGroup : BaseGPUObject
{
    public abstract IReadOnlyList<IGPUBindableResource> Resources { get; }

    protected GPUResourceGroup(in ResourceGroupDescriptor descriptor): base(descriptor.Name)
    {
    }
}
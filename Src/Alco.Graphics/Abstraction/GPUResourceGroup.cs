namespace Alco.Graphics;

/// <summary>
/// The group to bind resources to a shader.
/// </summary>
public abstract class GPUResourceGroup : BaseGPUObject
{
    /// <summary>Gets the resources bound in this group, in binding order.</summary>
    public abstract IReadOnlyList<IGPUBindableResource> Resources { get; }

    protected GPUResourceGroup(in ResourceGroupDescriptor descriptor): base(descriptor.Name)
    {
    }
}
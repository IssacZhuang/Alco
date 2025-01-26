namespace Alco.Graphics;

/// <summary>
/// The group to describe the binding of resources to a shader.
/// </summary>
public abstract class GPUBindGroup : BaseGPUObject
{
    public abstract IReadOnlyList<BindGroupEntry> Bindings { get; }

    public GPUBindGroup(in BindGroupDescriptor descriptor) : base(descriptor.Name)
    {
    }
}
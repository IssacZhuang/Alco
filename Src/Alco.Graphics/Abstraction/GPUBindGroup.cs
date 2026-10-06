namespace Alco.Graphics;

/// <summary>
/// Binds a set of resources (buffers, texture views, samplers) to a shader's bind group.
/// </summary>
public abstract class GPUBindGroup : BaseGPUObject
{
    /// <summary>Gets the entries bound in this group, one per shader binding.</summary>
    public abstract IReadOnlyList<BindGroupEntry> Bindings { get; }

    public GPUBindGroup(in BindGroupDescriptor descriptor) : base(descriptor.Name)
    {
    }
}
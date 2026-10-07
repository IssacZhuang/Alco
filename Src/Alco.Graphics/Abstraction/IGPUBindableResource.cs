namespace Alco.Graphics;

/// <summary>
/// Implemented by resources that can be bound into a <see cref="GPUBindGroup"/> entry.
/// </summary>
public interface IGPUBindableResource
{
    /// <summary>
    /// Gets the discriminator telling the binding API which kind of resource implements this interface.
    /// </summary>
    BindableResourceType ResourceType { get; }
}
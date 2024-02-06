namespace Vocore.Graphics;

/// <summary>
/// The group to bind resources to a shader.
/// </summary>
public abstract class GPUResourceGroup : BaseGPUObject
{
    public abstract IReadOnlyList<IGPUBindableResource> Resources { get; }
}
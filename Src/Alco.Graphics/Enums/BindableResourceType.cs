namespace Alco.Graphics;

/// <summary>
/// The concrete GPU object kind an <see cref="IGPUBindableResource"/> wraps when
/// bound into a resource group.
/// </summary>
public enum BindableResourceType
{
    /// <summary>A GPU buffer.</summary>
    Buffer,
    /// <summary>A texture view.</summary>
    TextureView,
    /// <summary>A texture sampler.</summary>
    Sampler,
}
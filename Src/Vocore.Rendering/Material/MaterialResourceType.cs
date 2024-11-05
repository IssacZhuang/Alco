namespace Vocore.Rendering;

/// <summary>
/// Specifies the type of material resource.
/// </summary>
public enum MaterialResourceType
{
    /// <summary>
    /// Not available for material usage.
    /// </summary>
    Unavailable,

    /// <summary>
    /// Texture at binding 0 and sampler at binding 1. Both resources are required and in the same group.
    /// </summary>
    TextureWithSampler,

    /// <summary>
    /// Buffer at binding 0.
    /// </summary>
    Buffer
}

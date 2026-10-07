namespace Alco.Graphics;

/// <summary>
/// The kind of resource a bind-group entry declares, as the shader sees it.
/// </summary>
public enum BindingType
{
    /// <summary>
    /// No binding type declared.
    /// </summary>
    Undefined = 0,
    /// <summary>
    /// Read-only uniform buffer.
    /// </summary>
    UniformBuffer = 1,
    /// <summary>
    /// Read-write storage buffer.
    /// </summary>
    StorageBuffer = 2,
    /// <summary>
    /// Texture sampler (filtering state).
    /// </summary>
    Sampler = 3,
    /// <summary>
    /// Sampled (read-only) texture.
    /// </summary>
    Texture = 4,
    /// <summary>
    /// The texture for random access
    /// </summary>
    StorageTexture = 5,
    /// <summary>
    /// The comparison sampler for depth comparison sampling (e.g. shadow map PCF)
    /// </summary>
    SamplerComparison = 6,
}
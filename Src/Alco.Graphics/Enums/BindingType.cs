namespace Alco.Graphics;

/// <summary>
/// The binding type for the shader
/// </summary>
public enum BindingType
{
    Undefined = 0,
    /// <summary>
    /// The uniform buffer
    /// </summary>
    UniformBuffer = 1,
    /// <summary>
    /// The storage buffer
    /// </summary>
    StorageBuffer = 2,
    /// <summary>
    /// The sampler for sampling textures
    /// </summary>
    Sampler = 3,
    /// <summary>
    /// The texture for sampling
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
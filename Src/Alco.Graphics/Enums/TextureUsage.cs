namespace Alco.Graphics;

/// <summary>
/// How a texture may be used by the GPU. Usage must be declared when the texture
/// is created; the backend validates every later operation against these bits.
/// </summary>
public enum TextureUsage
{
    /// <summary>
    /// No usage; the texture cannot be used in any GPU operation.
    /// </summary>
    None = 0,
    /// <summary>
    /// The texture can be the source of a copy command (CopySrc).
    /// </summary>
    Read = 1 << 0,
    /// <summary>
    /// The texture can be the destination of a copy or clear (CopyDst).
    /// </summary>
    Write = 1 << 1,
    /// <summary>
    /// The texture can be bound as a sampled texture in a bind group.
    /// </summary>
    TextureBinding = 1 << 2,
    /// <summary>
    /// The texture can be bound as a read-write storage texture in a bind group.
    /// </summary>
    StorageBinding = 1 << 3,
    /// <summary>
    /// The texture can be used as a color render attachment.
    /// </summary>
    ColorAttachment = 1 << 4,
    /// <summary>
    /// The texture can be used as a depth/stencil render attachment.
    /// </summary>
    DepthAttachment = 1 << 5,
    /// <summary>
    /// Common combination for textures that are copied and sampled.
    /// </summary>
    Standard = Read | Write | TextureBinding,
    /// <summary>
    /// General combination for textures compute shaders read and write
    /// (copy source/destination plus sampled and storage binding).
    /// </summary>
    ComputeWrite = Read | Write | TextureBinding | StorageBinding,
}
namespace Vocore.Graphics;

public enum TextureUsage
{
    None = 0,
    /// <summary>
    /// Used for sampling operations or compute shader reads.
    /// </summary>
    Read = 1 << 0,
    /// <summary>
    /// Used for storage operations or compute shader writes.
    /// </summary>
    Write = 1 << 1,
    /// <summary>
    /// Used for sampling operations or compute shader reads.
    /// </summary>
    TextureBinding = 1 << 2,
    /// <summary>
    /// Used for storage operations or compute shader writes.
    /// </summary>
    StorageBinding = 1 << 3,
    /// <summary>
    /// Used as a render target, color attachment or depth stencil attachment.
    /// </summary>
    RenderAttachment = 1 << 4,
    /// <summary>
    /// Read | Write | TextureBinding
    /// </summary>
    Standard = Read | Write | TextureBinding,
    /// <summary>
    /// Write | TextureBinding | RenderAttachment
    /// </summary>
    ComputeWrite = Write | TextureBinding | StorageBinding,
}
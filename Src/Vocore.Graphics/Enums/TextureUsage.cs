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
    /// Used as a render target or color attachment.
    /// </summary>
    RenderTarget = 1 << 2,
    /// <summary>
    /// Used as a depth stencil target or depth stencil attachment.
    /// </summary>
    DepthStencil = 1 << 3,
}
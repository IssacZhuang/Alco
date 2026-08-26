namespace Alco.Graphics;

/// <summary>
/// The store operation of a render pass attachment at the end of a render pass.
/// </summary>
public enum AttachmentStoreOp : byte
{
    /// <summary>Stores the rendered contents back to the attachment.</summary>
    Store = 0,
    /// <summary>Discards the rendered contents; the attachment becomes undefined after the pass.</summary>
    Discard = 1
}

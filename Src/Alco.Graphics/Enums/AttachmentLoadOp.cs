namespace Alco.Graphics;

/// <summary>
/// The load operation of a render pass attachment at the beginning of a render pass.
/// </summary>
public enum AttachmentLoadOp : byte
{
    /// <summary>Preserves the existing contents of the attachment.</summary>
    Load = 0,
    /// <summary>Clears the attachment to its clear value.</summary>
    Clear = 1
}

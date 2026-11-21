using Alco.Graphics;

namespace Alco.GUI;

/// <summary>
/// A UI node that uses its texture as a stencil mask for its subtree.
/// </summary>
public class UIMask : UINode, IUIMask
{
    /// <summary>
    /// The mask GPU texture; a white quad is used when it is null.
    /// </summary>
    public Texture? Texture { get; set; } = null;

    Texture? IUIMask.MaskTexture => Texture;

    Transform2D IUIMask.MaskTransform => RenderTransform;

    Rect IUIMask.MaskTextureUvRect => Rect.One;
}

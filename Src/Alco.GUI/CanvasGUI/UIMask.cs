
using System.Numerics;
using Alco.Rendering;

namespace Alco.GUI;

public class UIMask : UINode, IUIMask
{
    /// <summary>
    /// The texture of the sprite. The white quad will be rendered if it is null.
    /// </summary>
    /// <value></value>
    public Texture2D? Texture { get; set; } = null;

    Texture2D? IUIMask.MaskTexture => Texture;

    Transform2D IUIMask.MaskTransform => RenderTransform;

    Rect IUIMask.MaskTextureUvRect => Rect.One;
}

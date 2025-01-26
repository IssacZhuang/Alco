using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

/// <summary>
/// The UI node to draw a sprite.
/// </summary>
public class UISprite : UINode
{
    public static readonly Vector2 DefaultSize = new Vector2(100, 100);
    /// <summary>
    /// The texture of the sprite. The white quad will be rendered if it is null.
    /// </summary>
    /// <value></value>
    public Texture2D? Texture { get; set; } = null;

    /// <summary>
    /// The color of the sprite.
    /// </summary>
    /// <returns></returns>
    public ColorFloat Color { get; set; } = new ColorFloat(1, 1, 1, 1);

    public UISprite()
    {
        Size = DefaultSize;
    }

    /// <summary>
    /// Set the size of the sprite to the size of the texture.
    /// </summary>
    public void SetNativeSize()
    {
        if (Texture != null)
        {
            Size = new Vector2(Texture.Width, Texture.Height);
        }
        else
        {
            Size = DefaultSize;
        }
    }

    protected override void OnTick(Canvas canvas, float delta)
    {

    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        BoundingBox2D mask = Mask;
        if (!HasMask)
        {
            mask = canvas.Bound;
        }
        if (Texture != null)
        {
            canvas.Renderer.DrawSprite(Texture, RenderTransform.Matrix, Color, mask);
        }
        else
        {
            canvas.Renderer.DrawQuad(RenderTransform.Matrix, Color, mask);
        }
    }
}
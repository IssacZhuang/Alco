using System.Numerics;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

/// <summary>
/// The UI node to draw a sprite.
/// </summary>
public class UISprite : UINode
{
    public static readonly Vector2 DefaultSize = new Vector2(100, 100);
    public Texture2D? Texture { get; set; } = null;
    public ColorFloat Color { get; set; } = new ColorFloat(1, 1, 1, 1);

    public UISprite()
    {
        Size = DefaultSize;
    }

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

    protected override void OnTick(float delta)
    {

    }

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        if (Texture != null)
        {
            canvas.Renderer.DrawSprite(Texture, RenderTransform.Matrix, Color);
        }
        else
        {
            canvas.Renderer.DrawQuad(RenderTransform.Matrix, Color);
        }
    }
}
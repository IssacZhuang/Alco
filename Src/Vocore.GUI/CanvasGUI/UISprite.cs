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
    private readonly SpriteRenderer _renderer;
    public Texture2D? Texture { get; set; } = null;
    public ColorFloat Color { get; set; } = new ColorFloat(1, 1, 1, 1);

    public UISprite(SpriteRenderer renderer)
    {
        _renderer = renderer;
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

    protected override void OnUpdate(float delta)
    {
        if (Texture != null)
        {
            _renderer.Draw(Texture, RenderTransform.Matrix, Color);
        }
    }
}

using Alco.Rendering;

namespace Alco.GUI;

public class UIMask : UINode
{
    /// <summary>
    /// The texture of the sprite. The white quad will be rendered if it is null.
    /// </summary>
    /// <value></value>
    public Texture2D? Texture { get; set; } = null;

    protected override void OnUpdate(Canvas canvas, float delta)
    {
        base.OnUpdate(canvas, delta);
        canvas.DrawMask(Texture, RenderTransform.Matrix, Rect.One);
    }
}

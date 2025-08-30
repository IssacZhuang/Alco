using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.GUI;

/// <summary>
/// The UI node that can be received the UI event.
/// </summary>
public class UISelectable : UINode
{

    public bool Interactable { get; set; } = true;


    protected override void OnTick(Canvas canvas, float delta)
    {
        base.OnTick(canvas, delta);
        if (Interactable)
        {
            AddSelfForCollision(canvas);
        }
    }

    /// <summary>
    /// Add self for collision.
    /// </summary>
    /// <param name="canvas">The canvas that handle the collision world.</param>
    protected void AddSelfForCollision(Canvas canvas)
    {
        Transform2D transform = RenderTransform;
        ShapeBox2D shape = new ShapeBox2D(transform.Position, transform.Scale);
        canvas.AddClickReciever(this, shape);
    }


}
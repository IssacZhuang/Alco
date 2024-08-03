using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore.GUI;

/// <summary>
/// The UI node that can be received the UI event.
/// </summary>
public class UISelectable : UINode
{

    public bool Interactable { get; set; } = true;


    protected override void OnUpdate(Canvas canvas, float delta)
    {
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
        ShapeBox2D shape = new ShapeBox2D(transform.position, transform.scale);
        canvas.AddClickReciever(this, shape);
    }


}
using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

/// <summary>A pooled cube entity with a transform, color, and box collision shape.</summary>
public class Cube
{
    public Transform2D Transform;
    public ColorFloat Color;
    public bool PendingDestroy;

    /// <summary>Box collider derived from the cube's current transform.</summary>
    public ShapeBox2D Shape
    {
        get => new ShapeBox2D(Transform.Position, Transform.Scale, Transform.Rotation);
    }

    /// <summary>Initializes a cube with identity transform and unit-scale size.</summary>
    public Cube()
    {
        Transform = Transform2D.Identity;
        Transform.Scale = Vector2.One * 80f;
    }


    /// <summary>Marks a hit droplet for destruction.</summary>
    /// <param name="hitObject">Object that collided with this cube.</param>
    public void OnHit(object hitObject)
    {
        if(hitObject is Droplet droplet)
        {
            droplet.pendingDestroy = true;
        }
    }
}
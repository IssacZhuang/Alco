using System.Diagnostics;
using System.Numerics;
using Alco;
using Alco.Graphics;

/// <summary>A pooled droplet entity with a transform, color, and box collision shape.</summary>
public class Droplet
{
    public Transform2D transform;
    public ColorFloat color;
    public bool pendingDestroy;

    /// <summary>Box collider derived from the droplet's current transform.</summary>
    public ShapeBox2D Shape
    {
        get => new ShapeBox2D(transform.Position, transform.Scale, transform.Rotation);
    }

    /// <summary>Initializes a droplet with a default transform, color, and live state.</summary>
    public Droplet()
    {
        transform = new Transform2D();
        transform.Scale = Vector2.One * 10f;
        color = new ColorFloat(1, 1, 1, 1);
        pendingDestroy = false;
    }

}
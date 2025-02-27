using System.Diagnostics;
using System.Numerics;
using Alco;
using Alco.Graphics;

public class Droplet
{
    public Transform2D transform;
    public ColorFloat color;
    public bool pendingDestroy;

    public ShapeBox2D Shape
    {
        get => new ShapeBox2D(transform.Position, transform.Scale, transform.Rotation);
    }

    public Droplet()
    {
        transform = new Transform2D();
        transform.Scale = Vector2.One * 10f;
        color = new ColorFloat(1, 1, 1, 1);
        pendingDestroy = false;
    }

}
using System.Diagnostics;
using System.Numerics;
using Vocore;
using Vocore.Graphics;

public class Droplet
{
    public Transform2D transform;
    public ColorFloat color;
    public bool pendingDestroy;

    public ShapeBox2D Shape
    {
        get => new ShapeBox2D(transform.position, transform.scale, transform.rotation);
    }

    public Droplet()
    {
        transform = new Transform2D();
        transform.scale = Vector2.One * 10f;
        color = new ColorFloat(1, 1, 1, 1);
        pendingDestroy = false;
    }

}
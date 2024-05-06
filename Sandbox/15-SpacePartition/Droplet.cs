using System.Diagnostics;
using System.Numerics;
using Vocore;
using Vocore.Graphics;

public class Droplet
{
    public Transform3D transform;
    public ColorFloat color;
    public bool pendingDestroy;

    public ShapeBox3D Shape
    {
        get => new ShapeBox3D(transform.position, transform.scale, transform.rotation);
    }

    public Droplet()
    {
        transform = new Transform3D();
        transform.scale = Vector3.One * 10f;
        color = new ColorFloat(1, 1, 1, 1);
        pendingDestroy = false;
    }

}
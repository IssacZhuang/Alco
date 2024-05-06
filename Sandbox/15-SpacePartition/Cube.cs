using System.Numerics;
using Vocore;
using Vocore.Graphics;
using Vocore.Rendering;

public class Cube:ICollisionCaster
{
    public Transform3D transform;
    public ColorFloat color;
    public bool pendingDestroy;

    public ShapeBox3D Shape
    {
        get => new ShapeBox3D(transform.position, transform.scale, transform.rotation);
    }

    public Cube()
    {
        transform = Transform3D.Default;
        transform.scale = Vector3.One * 40f;
    }


    public void OnHit(object hitObject)
    {
        if(hitObject is Droplet droplet)
        {
            droplet.pendingDestroy = true;
        }
    }
}
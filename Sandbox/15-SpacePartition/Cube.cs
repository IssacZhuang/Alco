using System.Numerics;
using Vocore;
using Vocore.Graphics;
using Vocore.Rendering;

public class Cube:ICollisionCaster
{
    public Transform2D transform;
    public ColorFloat color;
    public bool pendingDestroy;

    public ShapeBox2D Shape
    {
        get => new ShapeBox2D(transform.position, transform.scale, transform.rotation);
    }

    public Cube()
    {
        transform = Transform2D.Identity;
        transform.scale = Vector2.One * 80f;
    }


    public void OnHit(object hitObject, int userData)
    {
        if(hitObject is Droplet droplet)
        {
            droplet.pendingDestroy = true;
        }
    }
}
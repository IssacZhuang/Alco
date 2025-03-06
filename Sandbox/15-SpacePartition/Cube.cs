using System.Numerics;
using Alco;
using Alco.Graphics;
using Alco.Rendering;

public class Cube:ICollisionCaster
{
    public Transform2D Transform;
    public ColorFloat Color;
    public bool PendingDestroy;

    public ShapeBox2D Shape
    {
        get => new ShapeBox2D(Transform.Position, Transform.Scale, Transform.Rotation);
    }

    public Cube()
    {
        Transform = Transform2D.Identity;
        Transform.Scale = Vector2.One * 80f;
    }


    public void OnHit(object hitObject, int userData)
    {
        if(hitObject is Droplet droplet)
        {
            droplet.pendingDestroy = true;
        }
    }
}
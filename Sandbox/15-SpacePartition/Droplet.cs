using System.Diagnostics;
using Vocore;
using Vocore.Graphics;

public class Droplet
{
    public Transform3D transform;
    public ColorFloat color;

    public Droplet()
    {
        transform = new Transform3D();
        color = new ColorFloat(1, 1, 1, 1);
    }

}
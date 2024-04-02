using System;
using System.Numerics;
using Vocore.Unsafe;

namespace Vocore.Rendering;

public unsafe struct Vertex3D
{
    public static readonly int SizeInBytes = sizeof(Vertex3D);

    public Vector3 position;
    public Vector2 uv;
    public Vector4 color;

    public Vertex3D(Vector3 position, Vector2 uv, Vector4 color)
    {
        this.position = position;
        this.uv = uv;
        this.color = color;
    }

}



using System;
using System.Numerics;
using Alco;

namespace Alco.Rendering;

public unsafe struct Vertex
{
    public static readonly int SizeInBytes = sizeof(Vertex);

    public Vector3 position;
    public Vector2 uv;

    public Vertex(Vector3 position, Vector2 uv)
    {
        this.position = position;
        this.uv = uv;
    }

}



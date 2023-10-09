using System;
using System.Numerics;

namespace Vocore.Engine
{
    public static class MeshPool
    {
        public static Mesh TestQuad = new Mesh(new Vertex[]
        {
            new Vertex(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0), new Vector4(1, 0, 0, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0), new Vector4(0, 1, 0, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1), new Vector4(0, 0, 1, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1), new Vector4(1, 1, 1, 1)),
        }, new ushort[]
        {
            0, 1, 2,
            2, 3, 0
        });
    }
}
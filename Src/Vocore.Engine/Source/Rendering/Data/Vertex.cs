using System;
using System.Numerics;
using Vocore.Unsafe;

namespace Vocore.Engine
{
    public struct Vertex
    {
        public static readonly uint SizeInBytes = (uint)UtilsMemory.SizeOf<Vertex>();

        public Vector3 position;
        public Vector2 uv;
        public Vector4 color;

        public Vertex(Vector3 position, Vector2 uv, Vector4 color)
        {
            this.position = position;
            this.uv = uv;
            this.color = color;
        }

    }
}


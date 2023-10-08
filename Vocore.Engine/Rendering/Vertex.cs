using System;
using System.Numerics;
using Vocore.Unsafe;
using Veldrid;

namespace Vocore.Engine
{
    public struct Vertex
    {
        public static readonly uint SizeInBytes = (uint)UtilsMemory.SizeOf<Vertex>();
        public static readonly VertexLayoutDescription Layout = new VertexLayoutDescription(
            new VertexElementDescription("position", VertexElementSemantic.Position, VertexElementFormat.Float3),
            new VertexElementDescription("uv", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("color", VertexElementSemantic.Color, VertexElementFormat.Float4)
        );
        public Vector3 position;
        public Vector2 uv;
        public Vector4 color;

    }
}


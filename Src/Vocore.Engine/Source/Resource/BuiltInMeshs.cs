using System;
using System.Numerics;

namespace Vocore.Engine
{
    public static class BuiltInMeshs
    {
        public static Vertex[] TestVertices = new Vertex[]
        {
            new Vertex(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0), new Vector4(1, 0, 0, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0), new Vector4(0, 1, 0, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1), new Vector4(0, 0, 1, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1), new Vector4(1, 1, 1, 1)),
        };

        public static ushort[] TestIndices = new ushort[]
        {
            0, 1, 2,
            2, 3, 0
        };


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

        public static Mesh Quad = new Mesh(new Vertex[]
        {
            new Vertex(new Vector3(-0.5f, 0.5f, 0), new Vector2(0, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, 0), new Vector2(1, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, 0), new Vector2(1, 1), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, 0), new Vector2(0, 1), new Vector4(1, 1, 1, 1)),
        }, new ushort[]
        {
            0, 1, 2,
            2, 3, 0
        });

        public static Mesh Cube = new Mesh(new Vertex[]
        {
            // Front face
            new Vertex(new Vector3(-0.5f, 0.5f, 0.5f), new Vector2(0, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, 0.5f), new Vector2(1, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, 0.5f), new Vector2(1, 1), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, 0.5f), new Vector2(0, 1), new Vector4(1, 1, 1, 1)),

            // Back face
            new Vertex(new Vector3(-0.5f, 0.5f, -0.5f), new Vector2(1, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, -0.5f), new Vector2(0, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, -0.5f), new Vector2(0, 1), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(1, 1), new Vector4(1, 1, 1, 1)),
        }, new ushort[]
        {
            // Front face
            0, 1, 2,
            2, 3, 0,

            // Right face
            1, 5, 6,
            6, 2, 1,

            // Back face
            5, 4, 7,
            7, 6, 5,

            // Left face
            4, 0, 3,
            3, 7, 4,

            // Top face
            4, 5, 1,
            1, 0, 4,

            // Bottom face
            3, 2, 6,
            6, 7, 3
        });

        public static Mesh TestCube = new Mesh(new Vertex[]
        {
            // Front face
            new Vertex(new Vector3(-0.5f, 0.5f, 0.5f), new Vector2(0, 0), new Vector4(0, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, 0.5f), new Vector2(1, 0), new Vector4(1, 0, 1, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, 0.5f), new Vector2(1, 1), new Vector4(1, 1, 0, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, 0.5f), new Vector2(0, 1), new Vector4(1, 1, 1, 1)),

            // Back face
            new Vertex(new Vector3(-0.5f, 0.5f, -0.5f), new Vector2(1, 0), new Vector4(1, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, 0.5f, -0.5f), new Vector2(0, 0), new Vector4(0, 1, 1, 1)),
            new Vertex(new Vector3(0.5f, -0.5f, -0.5f), new Vector2(0, 1), new Vector4(1, 0, 1, 1)),
            new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), new Vector2(1, 1), new Vector4(1, 1, 0, 1)),
        }, new ushort[]
        {
            // Front face
            0, 1, 2,
            2, 3, 0,

            // Right face
            1, 5, 6,
            6, 2, 1,

            // Back face
            5, 4, 7,
            7, 6, 5,

            // Left face
            4, 0, 3,
            3, 7, 4,

            // Top face
            4, 5, 1,
            1, 0, 4,

            // Bottom face
            3, 2, 6,
            6, 7, 3
        });
    }
}
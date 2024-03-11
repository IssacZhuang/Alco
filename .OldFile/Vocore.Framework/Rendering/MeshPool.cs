using System;

using UnityEngine;

namespace Vocore
{
    public static class MeshPool
    {
        public static Mesh Quad = CreateQuad();
        public static Mesh CreateQuad()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3(0, 0, 0),
                new Vector3(0, 1, 0),
                new Vector3(1, 1, 0),
                new Vector3(1, 0, 0),
            };
            mesh.triangles = new int[]
            {
                0, 1, 2,
                0, 2, 3,
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(0, 1),
                new Vector2(1, 1),
                new Vector2(1, 0),
            };
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
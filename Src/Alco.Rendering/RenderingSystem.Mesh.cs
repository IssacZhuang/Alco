using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

// mesh factory

public partial class RenderingSystem
{
    public const uint DefaultVertexBufferSize = 64;
    public const uint DefaultIndexBufferSize = 16;

    /// <summary>
    /// Make a 9 slice mesh data.
    /// </summary>
    /// <param name="width">The width of the mesh.</param>
    /// <param name="height">The height of the mesh.</param>
    /// <param name="top">The top border.</param>
    /// <param name="bottom">The bottom border.</param>
    /// <param name="left">The left border.</param>
    /// <param name="right">The right border.</param>
    /// <returns>The mesh data.</returns>
    public (Vertex[], uint[]) Make9SliceMeshData(float width, float height, float top, float bottom, float left, float right)
    {
        //position, uv
        Vertex[] vertices = new Vertex[16];
        uint[] indices = new uint[54];

        //center point is 0,0

        // Calculate vertex positions
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        ReadOnlySpan<float> xPos = stackalloc float[4] {
            -halfWidth,
            -halfWidth + left,
            halfWidth - right,
            halfWidth
        };

        ReadOnlySpan<float> yPos = stackalloc float[4] {
            -halfHeight,
            -halfHeight + bottom,
            halfHeight - top,
            halfHeight
        };

        // Calculate UV coordinates
        float leftUV = left / width;
        float rightUV = right / width;
        float bottomUV = bottom / height;
        float topUV = top / height;

        ReadOnlySpan<float> uvX = stackalloc float[4] { 0, leftUV, 1 - rightUV, 1 };
        ReadOnlySpan<float> uvY = stackalloc float[4] { 0, bottomUV, 1 - topUV, 1 };

        // Generate vertices
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                vertices[y * 4 + x] = new Vertex(
                    new Vector3(xPos[x], yPos[y], 0),
                    new Vector2(uvX[x], uvY[y])
                );
            }
        }

        // Generate indices for 9 quads
        int idx = 0;
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 3; x++) 
            {
                int baseVertex = y * 4 + x;
                indices[idx++] = (uint)baseVertex;
                indices[idx++] = (uint)(baseVertex + 1);
                indices[idx++] = (uint)(baseVertex + 4);
                indices[idx++] = (uint)(baseVertex + 1);
                indices[idx++] = (uint)(baseVertex + 5);
                indices[idx++] = (uint)(baseVertex + 4);
            }
        }

        return (vertices, indices);
    }

    /// <summary>
    /// Create a mesh.
    /// </summary>
    /// <param name="vertices">The vertices of the mesh.</param>
    /// <param name="indices">The indices of the mesh.</param>
    /// <param name="name">The name of the mesh.</param>
    /// <typeparam name="TVertex">The type of the vertices.</typeparam>
    /// <returns>The created mesh.</returns>
    public unsafe PrimitiveMesh CreatePrimitiveMesh<TVertex>(TVertex[] vertices, uint[] indices, string name = "mesh") where TVertex : unmanaged
    {
        PrimitiveMesh mesh = new PrimitiveMesh(_device, (uint)vertices.Length * (uint)sizeof(TVertex), (uint)indices.Length, IndexFormat.UInt16, name);
        mesh.SetVertex<TVertex>(vertices);
        mesh.SetIndices(indices);
        return mesh;
    }

    /// <summary>
    /// Create a primitive mesh with a vertex buffer size and an index buffer size.
    /// </summary>
    /// <param name="vertexBufferSize">The size of the vertex buffer.</param>
    /// <param name="indexBufferSize">The size of the index buffer.</param>
    /// <param name="name">The name of the mesh.</param>
    /// <returns>The created mesh.</returns>
    public unsafe PrimitiveMesh CreatePrimitiveMesh(uint vertexBufferSize, uint indexBufferSize, string name = "mesh")
    {
        return new PrimitiveMesh(_device, vertexBufferSize, indexBufferSize, IndexFormat.UInt16, name);
    }

    /// <summary>
    /// Create a mesh.
    /// </summary>
    /// <param name="vertices">The vertices of the mesh.</param>
    /// <param name="indices">The indices of the mesh.</param>
    /// <param name="name">The name of the mesh.</param>
    /// <typeparam name="TVertex">The type of the vertices.</typeparam>
    /// <returns></returns>
    public unsafe PrimitiveMesh CreatePrimitiveMesh<TVertex>(TVertex[] vertices, ushort[] indices, string name = "mesh") where TVertex : unmanaged
    {

        PrimitiveMesh mesh = new PrimitiveMesh(_device, (uint)vertices.Length * (uint)sizeof(TVertex), (uint)indices.Length, IndexFormat.UInt16, name);
        mesh.SetVertex<TVertex>(vertices);
        mesh.SetIndices(indices);
        return mesh;
    }

    public DynamicMesh CreateDynamicMesh(string name = "dynamic_mesh")
    {
        return new DynamicMesh(_device, DefaultVertexBufferSize, DefaultIndexBufferSize, name);
    }
}
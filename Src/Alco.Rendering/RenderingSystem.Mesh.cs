using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

// mesh factory

public partial class RenderingSystem
{
    public const uint DefaultVertexBufferSize = 64;
    public const uint DefaultIndexBufferSize = 16;

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
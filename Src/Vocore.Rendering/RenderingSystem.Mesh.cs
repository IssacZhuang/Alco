using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public const uint DefaultVertexBufferSize = 64;
    public const uint DefaultIndexBufferSize = 16;

    /// <summary>
    /// Create a mesh.
    /// </summary>
    /// <param name="name">The name of the mesh.</param>
    /// <returns>The created mesh.</returns>
    public unsafe Mesh CreateMesh(string name = "mesh")
    {
        return new Mesh(_device, DefaultVertexBufferSize, DefaultVertexBufferSize, DefaultIndexBufferSize, IndexFormat.Uint32, name);
    }
    

    /// <summary>
    /// Create a mesh.
    /// </summary>
    /// <param name="vertices">The vertices of the mesh.</param>
    /// <param name="indices">The indices of the mesh.</param>
    /// <param name="name">The name of the mesh.</param>
    /// <typeparam name="TVertex">The type of the vertices.</typeparam>
    /// <returns>The created mesh.</returns>
    public unsafe Mesh CreateMesh<TVertex>(TVertex[] vertices, uint[] indices, string name = "mesh") where TVertex : unmanaged
    {
        fixed (void* vertexData = vertices)
        fixed (void* indexData = indices)
        {
            Mesh mesh = new Mesh(_device, (uint)vertices.Length, (uint)sizeof(TVertex), (uint)indices.Length, IndexFormat.Uint32, name);
            mesh.UpdateVertex(vertexData, (uint)(vertices.Length * sizeof(TVertex)));
            mesh.UpdateIndex(indexData, (uint)(indices.Length * sizeof(uint)));
            return mesh;
        }
    }

    /// <summary>
    /// Create a mesh.
    /// </summary>
    /// <param name="vertices">The vertices of the mesh.</param>
    /// <param name="indices">The indices of the mesh.</param>
    /// <param name="name">The name of the mesh.</param>
    /// <typeparam name="TVertex">The type of the vertices.</typeparam>
    /// <returns></returns>
    public unsafe Mesh CreateMesh<TVertex>(TVertex[] vertices, ushort[] indices, string name = "mesh") where TVertex : unmanaged
    {
        fixed (void* vertexData = vertices)
        fixed (void* indexData = indices)
        {
            Mesh mesh = new Mesh(_device, (uint)vertices.Length, (uint)sizeof(TVertex), (uint)indices.Length, IndexFormat.Uint16, name);
            mesh.UpdateVertex(vertexData, (uint)(vertices.Length * sizeof(TVertex)));
            mesh.UpdateIndex(indexData, (uint)(indices.Length * sizeof(ushort)));
            return mesh;
        }
    }
}
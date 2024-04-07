using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class RenderingSystem
{
    public unsafe Mesh Create<TVertex>(TVertex[] vertices, uint[] indices, string name = "mesh") where TVertex : unmanaged
    {
        fixed (void* vertexData = vertices)
        fixed (void* indexData = indices)
        {
            Mesh mesh = new Mesh(_device, (uint)(vertices.Length * sizeof(TVertex)), (uint)indices.Length, IndexFormat.Uint32, name);
            mesh.UpdateVertex(vertexData, (uint)(vertices.Length * sizeof(TVertex)));
            mesh.UpdateIndex(indexData, (uint)(indices.Length * sizeof(uint)));
            return mesh;
        }
    }

    public unsafe Mesh Create<TVertex>(TVertex[] vertices, ushort[] indices, string name = "mesh") where TVertex : unmanaged
    {
        fixed (void* vertexData = vertices)
        fixed (void* indexData = indices)
        {
            Mesh mesh = new Mesh(_device, (uint)(vertices.Length * sizeof(TVertex)), (uint)indices.Length, IndexFormat.Uint16, name);
            mesh.UpdateVertex(vertexData, (uint)(vertices.Length * sizeof(TVertex)));
            mesh.UpdateIndex(indexData, (uint)(indices.Length * sizeof(ushort)));
            return mesh;
        }
    }
}
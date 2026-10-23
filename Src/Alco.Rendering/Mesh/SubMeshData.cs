using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The draw range and buffer layout of one sub-mesh.
/// </summary>
public struct SubMeshData
{
    public int Index;
    public ulong VertexOffset;
    public ulong VertexSize;
    public ulong IndexOffset;
    public ulong IndexSize;
    public uint IndexCount;
    public IndexFormat IndexFormat;
}

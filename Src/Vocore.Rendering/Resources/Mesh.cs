using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public unsafe class Mesh : ShaderResource, IMesh
{
    protected GPUBuffer _vertexBuffer;
    protected GPUBuffer _indexBuffer;
    protected IndexFormat _indexFormat;
    protected uint _indexCount;
    public string Name { get; }

    public GPUBuffer VertexBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _vertexBuffer;
    }

    public GPUBuffer IndexBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexBuffer;
    }

    public IndexFormat IndexFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexFormat;
    }

    public uint IndexCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexCount;
    }

    public uint SubMeshCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    public Mesh(void* vertexData, uint vertexSize, void* indexData, uint indexSize, IndexFormat indexFormat, uint indexCount, string name = "mesh")
    {
        GPUDevice device = GetDevice();
        _vertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = vertexSize,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Name = "vertex_buffer"
        });
        device.WriteBuffer(_vertexBuffer, (byte*)vertexData, vertexSize);

        _indexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = indexSize,
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
            Name = "index_buffer"
        });
        device.WriteBuffer(_indexBuffer, (byte*)indexData, indexSize);

        _indexFormat = indexFormat;
        _indexCount = indexCount;

        Name = name;
    }

    public SubMeshData GetSubMesh(int index)
    {
        //no submeshes,, return hole mesh
        return new SubMeshData
        {
            VertexOffset = 0,
            VertexSize = _vertexBuffer.Size,
            IndexOffset = 0,
            IndexSize = _indexBuffer.Size,
            IndexCount = _indexCount
        };
    }

    protected override void Dispose(bool disposing)
    {
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
    }

    #region Creation

    public static Mesh Create<TVertex>(TVertex[] vertices, uint[] indices, string name = "mesh") where TVertex : unmanaged
    {
        fixed (void* vertexData = vertices)
        fixed (void* indexData = indices)
        {
            return new Mesh(vertexData, (uint)(vertices.Length * sizeof(TVertex)), indexData, (uint)(indices.Length * sizeof(uint)), IndexFormat.Uint32, (uint)indices.Length, name);
        }
    }

    public static Mesh Create<TVertex>(TVertex[] vertices, ushort[] indices, string name = "mesh") where TVertex : unmanaged
    {
        fixed (void* vertexData = vertices)
        fixed (void* indexData = indices)
        {
            return new Mesh(vertexData, (uint)(vertices.Length * sizeof(TVertex)), indexData, (uint)(indices.Length * sizeof(ushort)), IndexFormat.Uint16, (uint)indices.Length, name);
        }
    }

    #endregion
}

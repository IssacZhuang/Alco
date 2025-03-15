using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed unsafe class StaticMesh : Mesh 
{
    private SubMeshData _defaultSubMesh;

    public override uint SubMeshCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    internal StaticMesh(GPUDevice device, uint vertexCount, uint vertexStride, uint indexCount, IndexFormat indexFormat, string name = "mesh")
    : base(device, vertexCount, vertexStride, indexCount, indexFormat, name)
    {
        _defaultSubMesh = new SubMeshData
        {
            VertexOffset = 0,
            VertexSize = VertexCount * VertexStride,
            IndexOffset = 0,
            IndexSize = IndexCount * GetIndexSize(IndexFormat),
            IndexCount = IndexCount
        };
    }

    public void SetVertex<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        ResizeVertextBufferSoft((uint)data.Length, (uint)sizeof(T));
        fixed (void* ptr = data)
        {
            UpdateVertexUnsafe(ptr, (uint)(data.Length * sizeof(T)), 0);
        }
        RefreshSubMeshData();
    }

    public void UpdateVertexUnsafe(void* data, uint size, uint offset = 0)
    {
        ValidateVertexBufferSize(offset, size);
        _device.WriteBuffer(VertexBuffer, offset, (byte*)data, size);
    }



    public void SetIndices(ReadOnlySpan<uint> indices)
    {
        ResizeIndexBufferSoft((uint)indices.Length, IndexFormat.UInt32);
        fixed (void* ptr = indices)
        {
            UpdateIndicesUnsafe(ptr, (uint)(indices.Length * sizeof(uint)), 0);
        }
        RefreshSubMeshData();
    }

    public void SetIndices(ReadOnlySpan<ushort> indices)
    {
        ResizeIndexBufferSoft((uint)indices.Length, IndexFormat.UInt16);
        fixed (void* ptr = indices)
        {
            UpdateIndicesUnsafe(ptr, (uint)(indices.Length * sizeof(ushort)), 0);
        }
        RefreshSubMeshData();
    }


    public void UpdateIndicesUnsafe(void* data, uint size, uint offset)
    {
        ValidateIndexBufferSize(offset, size);
        _device.WriteBuffer(IndexBuffer, offset, (byte*)data, size);
    }

    public override SubMeshData GetSubMesh(int index)
    {
        //no submeshes,, return hole mesh
        return _defaultSubMesh;
    }


    private void ValidateVertexBufferSize(uint offset, uint size)
    {
        if (offset + size > VertexBuffer.Size)
        {
            throw new InvalidOperationException($"The data offset with size is out of range of vertex buffer. offset: {offset}, size: {size}, vertex buffer size: {VertexBuffer.Size}");
        }
    }

    private void ValidateIndexBufferSize(uint offset, uint size)
    {
        if (offset + size > IndexBuffer.Size)
        {
            throw new InvalidOperationException($"The data offset with size is out of range of index buffer. offset: {offset}, size: {size}, index buffer size: {IndexBuffer.Size}");
        }
    }

    private void RefreshSubMeshData()
    {
        _defaultSubMesh.VertexOffset = 0;
        _defaultSubMesh.VertexSize = VertexCount * VertexStride;
        _defaultSubMesh.IndexOffset = 0;
        _defaultSubMesh.IndexSize = IndexCount * GetIndexSize(IndexFormat);
    }
}

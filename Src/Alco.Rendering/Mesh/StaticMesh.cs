using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed unsafe class StaticMesh : Mesh 
{
    private readonly SubMeshData _defaultSubMesh;

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
            VertexSize = VertexBuffer.Size,
            IndexOffset = 0,
            IndexSize = IndexBuffer.Size,
            IndexCount = IndexCount
        };
    }

    public void UpdateVertex<T>(ReadOnlySpan<T> data, uint offset = 0) where T : unmanaged
    {
        fixed (void* ptr = data)
        {
            UpdateVertex(ptr, (uint)(data.Length * sizeof(T)), offset);
        }
    }

    public void UpdateVertex(void* data, uint size, uint offset = 0)
    {
        ValidateVertexBufferSize(offset, size);
        _device.WriteBuffer(VertexBuffer, offset, (byte*)data, size);
    }



    public void UpdateIndex(ReadOnlySpan<uint> indices, uint offset = 0)
    {
        if(IndexFormat != IndexFormat.UInt32)
        {
            throw new InvalidOperationException("trying to update index buffer with uint data, but index format is not UInt32.");
        }
        fixed (void* ptr = indices)
        {
            UpdateIndex(ptr, (uint)(indices.Length * sizeof(uint)), offset, (uint)indices.Length);
        }
    }

    public void UpdateIndex(ReadOnlySpan<ushort> indices, uint offset = 0)
    {
        if(IndexFormat != IndexFormat.UInt16)
        {
            throw new InvalidOperationException("trying to update index buffer with ushort data, but index format is not UInt16.");
        }
        fixed (void* ptr = indices)
        {
            UpdateIndex(ptr, (uint)(indices.Length * sizeof(ushort)), offset, (uint)indices.Length);
        }
    }


    public void UpdateIndex(void* data, uint size, uint offset, uint indexCount)
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

    
}

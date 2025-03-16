using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The mesh that has only one submesh.
/// </summary>
public sealed unsafe class PrimitiveMesh : Mesh
{
    private SubMeshData _defaultSubMesh;

    public override int SubMeshCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    internal PrimitiveMesh(GPUDevice device, uint vertexBufferSize, uint indexCount, IndexFormat indexFormat, string name = "mesh")
    : base(device, vertexBufferSize, indexCount * GetIndexSize(indexFormat), name)
    {
        _defaultSubMesh = new SubMeshData
        {
            Index = 0,
            VertexOffset = 0,
            VertexSize = VertexBuffer.Size,
            IndexOffset = 0,
            IndexSize = IndexBuffer.Size,
            IndexCount = indexCount,
            IndexFormat = indexFormat
        };
    }

    /// <summary>
    /// Set the vertices with unmanaged array.
    /// This method will update the entire vertex buffer with the provided data.
    /// </summary>
    /// <typeparam name="T">The type of the vertices.</typeparam>
    /// <param name="data">The data array.</param>
    public void SetVertex<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        EnsureVertexBufferSize((uint)(data.Length * sizeof(T)));
        fixed (void* ptr = data)
        {
            UpdateVertexUnsafe(ptr, (uint)(data.Length * sizeof(T)), 0);
        }

        //_defaultSubMesh.VertexOffset = 0;//readonly, no need to set
        _defaultSubMesh.VertexSize = (uint)(data.Length * sizeof(T));
        IncrementVersion();
    }

    /// <summary>
    /// Update the vertices by unmanaged pointer.
    /// </summary>
    /// <param name="data">The data pointer.</param>
    /// <param name="size">The size of the data.</param>
    /// <param name="offset">The offset of the data.</param>
    public void UpdateVertexUnsafe(void* data, uint size, uint offset = 0)
    {
        ValidateVertexBufferSize(offset, size);
        _device.WriteBuffer(VertexBuffer, offset, (byte*)data, size);
    }

    /// <summary>
    /// Set the indices with uint array. 
    /// This method will change the <see cref="SubMeshData.IndexFormat"/> to <see cref="IndexFormat.UInt32"/>.
    /// </summary>
    /// <param name="indices">The indices array.</param>
    public void SetIndices(ReadOnlySpan<uint> indices)
    {
        EnsureIndexBufferSize((uint)(indices.Length * sizeof(uint)));
        fixed (void* ptr = indices)
        {
            UpdateIndicesUnsafe(ptr, (uint)(indices.Length * sizeof(uint)), 0);
        }

        //_defaultSubMesh.IndexOffset = 0;//readonly, no need to set
        _defaultSubMesh.IndexSize = (uint)(indices.Length * sizeof(uint));
        _defaultSubMesh.IndexFormat = IndexFormat.UInt32;
        IncrementVersion();
    }

    /// <summary>
    /// Set the indices with ushort array.
    /// This method will change the <see cref="SubMeshData.IndexFormat"/> to <see cref="IndexFormat.UInt16"/>.
    /// </summary>
    /// <param name="indices">The indices array.</param>
    public void SetIndices(ReadOnlySpan<ushort> indices)
    {
        EnsureIndexBufferSize((uint)(indices.Length * sizeof(ushort)));
        fixed (void* ptr = indices)
        {
            UpdateIndicesUnsafe(ptr, (uint)(indices.Length * sizeof(ushort)), 0);
        }

        //_defaultSubMesh.IndexOffset = 0;//readonly, no need to set
        _defaultSubMesh.IndexSize = (uint)(indices.Length * sizeof(ushort));
        _defaultSubMesh.IndexFormat = IndexFormat.UInt16;
        IncrementVersion();
    }

    /// <summary>
    /// Update the indices by unmanaged pointer.
    /// </summary>
    /// <param name="data">The data pointer.</param>
    /// <param name="size">The size of the data.</param>
    /// <param name="offset">The offset of the data.</param>
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
}

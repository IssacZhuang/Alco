using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Unsafe;

namespace Alco.Rendering;

/// <summary>
/// The mesh that can add submeshes dynamically.
/// </summary>
public sealed unsafe class DynamicMesh : Mesh
{
    private readonly List<SubMeshData> _subMeshes;
    private NativeBuffer<byte> _vertexBufferCpu;
    private uint _vertexBufferCpuSize;
    private NativeBuffer<byte> _indexBufferCpu;
    private uint _indexBufferCpuSize;

    /// <summary>
    /// Gets the number of sub-meshes in this dynamic mesh.
    /// </summary>
    public override int SubMeshCount => _subMeshes.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="DynamicMesh"/> class.
    /// </summary>
    /// <param name="device">The GPU device.</param>
    /// <param name="vertexBufferSize">The size of the vertex buffer in bytes.</param>
    /// <param name="indexBufferSize">The size of the index buffer in bytes.</param>
    /// <param name="name">The name of the mesh.</param>
    public DynamicMesh(GPUDevice device, uint vertexBufferSize, uint indexBufferSize, string name = "mesh") : 
    base(device, vertexBufferSize, indexBufferSize, name)
    {
        _subMeshes = new List<SubMeshData>();
        _vertexBufferCpu = new NativeBuffer<byte>(vertexBufferSize);
        _indexBufferCpu = new NativeBuffer<byte>(indexBufferSize);
    }



    /// <summary>
    /// Adds a sub-mesh with 32-bit indices to this dynamic mesh.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices of the sub-mesh.</param>
    /// <param name="indices">The 32-bit indices of the sub-mesh.</param>
    /// <returns>The sub-mesh data.</returns>
    public SubMeshData AddSubMesh<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<uint> indices) where TVertex : unmanaged
    {
        fixed (void* pVertices = vertices)
        {
            fixed (void* pIndices = indices)
            {
                return AddSubMeshCore((byte*)pVertices, (uint)(vertices.Length * sizeof(TVertex)), (byte*)pIndices, (uint)indices.Length, IndexFormat.UInt32);
            }
        }
    }

    /// <summary>
    /// Adds a sub-mesh with 16-bit indices to this dynamic mesh.
    /// </summary>
    /// <typeparam name="TVertex">The vertex type.</typeparam>
    /// <param name="vertices">The vertices of the sub-mesh.</param>
    /// <param name="indices">The 16-bit indices of the sub-mesh.</param>
    /// <returns>The sub-mesh data.</returns>
    public SubMeshData AddSubMesh<TVertex>(ReadOnlySpan<TVertex> vertices, ReadOnlySpan<ushort> indices) where TVertex : unmanaged
    {
        fixed (void* pVertices = vertices)
        {
            fixed (void* pIndices = indices)
            {
                return AddSubMeshCore((byte*)pVertices, (uint)(vertices.Length * sizeof(TVertex)), (byte*)pIndices, (uint)indices.Length, IndexFormat.UInt16);
            }
        }
    }

    /// <summary>
    /// Clears all sub-meshes from this dynamic mesh.
    /// </summary>
    public void Clear()
    {
        _subMeshes.Clear();
        _vertexBufferCpuSize = 0;
        _indexBufferCpuSize = 0;
    }

    /// <summary>
    /// Updates the vertex and index buffers on the GPU with the current CPU data.
    /// </summary>
    public void UpdateBufferToGPU()
    {
        EnsureIndexBufferSize(_indexBufferCpuSize);
        EnsureVertexBufferSize(_vertexBufferCpuSize);

        _device.WriteBuffer(VertexBuffer, 0, _vertexBufferCpu.UnsafePointer, _vertexBufferCpuSize);
        _device.WriteBuffer(IndexBuffer, 0, _indexBufferCpu.UnsafePointer, _indexBufferCpuSize);
    }

    private unsafe SubMeshData AddSubMeshCore(byte* vertexPtr, uint verticesSize, byte* indexPtr, uint indexCount, IndexFormat indexFormat)
    {
        uint indicesSize = indexCount * GetIndexSize(indexFormat);
        _vertexBufferCpu.EnsureSize((int)(_vertexBufferCpuSize + verticesSize));
        _indexBufferCpu.EnsureSize((int)(_indexBufferCpuSize + indicesSize));

        byte* pVertexBuffer = _vertexBufferCpu.UnsafePointer + _vertexBufferCpuSize;
        byte* pIndexBuffer = _indexBufferCpu.UnsafePointer + _indexBufferCpuSize;

        UtilsMemory.MemCopy(pVertexBuffer, vertexPtr, verticesSize);
        UtilsMemory.MemCopy(pIndexBuffer, indexPtr, indicesSize);

        SubMeshData subMeshData = new SubMeshData
        {
            VertexOffset = _vertexBufferCpuSize,
            VertexSize = verticesSize,
            IndexOffset = _indexBufferCpuSize,
            IndexSize = indicesSize,
            IndexCount = indexCount,
            IndexFormat = indexFormat,
        };

        _subMeshes.Add(subMeshData);

        _vertexBufferCpuSize += verticesSize;
        _indexBufferCpuSize += indicesSize;

        return subMeshData;
    }

    /// <summary>
    /// Gets the sub-mesh data at the specified index.
    /// </summary>
    /// <param name="index">The index of the sub-mesh.</param>
    /// <returns>The sub-mesh data.</returns>
    public override SubMeshData GetSubMesh(int index)
    {
        return _subMeshes[index];
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _vertexBufferCpu.Dispose();
        _indexBufferCpu.Dispose();
    }
}
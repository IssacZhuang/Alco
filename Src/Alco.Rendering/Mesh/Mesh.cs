using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The class to manage the vertex and index buffer of the mesh
/// </summary>
public abstract class Mesh : AutoDisposable
{
    protected readonly GPUDevice _device;

    private GPUBuffer _vertexBuffer;
    private GPUBuffer _indexBuffer;
    private IndexFormat _indexFormat;
    private uint _indexCount;

    private uint _vertexCount;
    private uint _vertexStride;

    private uint _version;//it will increase when the mesh is updated

    /// <summary>
    /// Gets the name of the mesh.
    /// </summary>
    public string Name { get; }

    //high frequency access, use AggressiveInlining to optimize

    /// <summary>
    /// Gets the vertex buffer of the mesh.
    /// </summary>
    public GPUBuffer VertexBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _vertexBuffer;
    }

    /// <summary>
    /// Gets the index buffer of the mesh.
    /// </summary>
    public GPUBuffer IndexBuffer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexBuffer;
    }

    /// <summary>
    /// Gets the index format used by the mesh.
    /// </summary>
    public IndexFormat IndexFormat {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexFormat;
    }

    /// <summary>
    /// Gets the number of indices in the mesh.
    /// </summary>
    public uint IndexCount {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexCount;
    }

    /// <summary>
    /// Gets the version of the mesh, which increases when the mesh is updated.
    /// </summary>
    public uint Version
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _version;
    }

    /// <summary>
    /// Gets the number of vertices in the mesh.
    /// </summary>
    public uint VertexCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _vertexCount;
    }

    /// <summary>
    /// Gets the stride (size in bytes) of each vertex in the mesh.
    /// </summary>
    public uint VertexStride
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _vertexStride;
    }

    /// <summary>
    /// Initializes a new instance of the Mesh class.
    /// </summary>
    /// <param name="device">The GPU device used to create buffers.</param>
    /// <param name="vertexCount">The number of vertices in the mesh.</param>
    /// <param name="vertexStride">The stride (size in bytes) of each vertex.</param>
    /// <param name="indexCount">The number of indices in the mesh.</param>
    /// <param name="indexFormat">The format of indices (UInt16 or UInt32).</param>
    /// <param name="name">The name of the mesh. Default is "mesh".</param>
    protected Mesh(GPUDevice device, uint vertexCount, uint vertexStride, uint indexCount, IndexFormat indexFormat, string name = "mesh")
    {
        _device = device;
        Name = name;

        _vertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = vertexCount * vertexStride,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        });

        _indexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = indexCount * GetIndexSize(indexFormat),
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
        });

        _indexFormat = indexFormat;
        _indexCount = indexCount;

        _vertexCount = vertexCount;
        _vertexStride = vertexStride;
    }

    /// <summary>
    /// Gets the number of sub-meshes in this mesh.
    /// </summary>
    public abstract uint SubMeshCount { get; }

    /// <summary>
    /// Gets the sub-mesh data at the specified index.
    /// </summary>
    /// <param name="index">The index of the sub-mesh to retrieve.</param>
    /// <returns>The sub-mesh data.</returns>
    public abstract SubMeshData GetSubMesh(int index);

    /// <summary>
    /// Gets the size in bytes of the specified index format.
    /// </summary>
    /// <param name="format">The index format.</param>
    /// <returns>The size in bytes of the index format.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the index format is invalid.</exception>
    public static uint GetIndexSize(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => sizeof(ushort),
            IndexFormat.UInt32 => sizeof(uint),
            _ => throw new InvalidOperationException("Invalid index format.")
        };
    }

    /// <summary>
    /// Disposes the resources used by the mesh.
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false if called from finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
        }
    }

    /// <summary>
    /// Resizes the vertex buffer to accommodate the specified number of vertices.
    /// It will recreate the vertex buffer and dispose the old one.
    /// </summary>
    /// <param name="vertexCount">The new number of vertices.</param>
    /// <param name="vertexStride">The stride (size in bytes) of each vertex.</param>
    protected void ResizeVertextBuffer(uint vertexCount, uint vertexStride)
    {
        _vertexBuffer.Dispose();
        _vertexBuffer = _device.CreateBuffer(new BufferDescriptor
        {
            Size = vertexCount * vertexStride,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
        });
        IncrementVersion();
    }

    /// <summary>
    /// Resizes the vertex buffer only if the new size is larger than the current size use <see cref="ResizeVertextBuffer"/> to resize the vertex buffer.
    /// Otherwise, it will just update the <see cref="VertexCount"/> and <see cref="VertexStride"/>.
    /// </summary>
    /// <param name="vertexCount">The new number of vertices.</param>
    /// <param name="vertexStride">The stride (size in bytes) of each vertex.</param>
    protected void ResizeVertextBufferSoft(uint vertexCount, uint vertexStride)
    {
        if (vertexCount * vertexStride > _vertexBuffer.Size)
        {
            ResizeVertextBuffer(vertexCount, vertexStride);
        }
        else
        {
            _vertexCount = vertexCount;
            _vertexStride = vertexStride;
            IncrementVersion();
        }
    }

    /// <summary>
    /// Resizes the index buffer to accommodate the specified number of indices.
    /// It will recreate the index buffer and dispose the old one.
    /// </summary>
    /// <param name="indexCount">The new number of indices.</param>
    /// <param name="indexFormat">The format of indices (UInt16 or UInt32).</param>
    protected void ResizeIndexBuffer(uint indexCount, IndexFormat indexFormat)
    {
        _indexBuffer.Dispose();
        _indexBuffer = _device.CreateBuffer(new BufferDescriptor
        {
            Size = indexCount * GetIndexSize(indexFormat),
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
        });
        _indexFormat = indexFormat;
        _indexCount = indexCount;
        IncrementVersion();
    }

    /// <summary>
    /// Resizes the index buffer only if the new size is larger than the current size use <see cref="ResizeIndexBuffer"/> to resize the index buffer.
    /// Otherwise, it will just update the <see cref="IndexCount"/> and <see cref="IndexFormat"/>.
    /// </summary>
    /// <param name="indexCount">The new number of indices.</param>
    /// <param name="indexFormat">The format of indices (UInt16 or UInt32).</param>
    protected void ResizeIndexBufferSoft(uint indexCount, IndexFormat indexFormat)
    {
        if (indexCount * GetIndexSize(indexFormat) > _indexBuffer.Size)
        {
            ResizeIndexBuffer(indexCount, indexFormat);
        }
        else
        {
            _indexCount = indexCount;
            _indexFormat = indexFormat;
            IncrementVersion();
        }
    }

    /// <summary>
    /// Increments the version number of the mesh.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void IncrementVersion()
    {
        unchecked
        {
            _version++;
        }
    }
}


using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public unsafe class Mesh : ShaderResource, IMesh
{
    private readonly string VertexBufferName;
    private readonly string IndexBufferName;
    protected GPUDevice _device;
    protected GPUBuffer _vertexBuffer;
    protected GPUBuffer _indexBuffer;
    protected IndexFormat _indexFormat;
    protected uint _indexCount;
    protected uint _vertexCount;
    protected uint _vertexStride;
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

    public virtual uint SubMeshCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => 1;
    }

    internal Mesh(GPUDevice device, uint vertexCount, uint vertexStride, uint indexCount, IndexFormat indexFormat, string name = "mesh")
    {
        _device = device;

        VertexBufferName = $"{name}_vertex_buffer";
        IndexBufferName = $"{name}_index_buffer";

        _vertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = vertexCount * vertexStride,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Name = VertexBufferName
        });

        _indexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = indexCount * GetIndexSize(indexFormat),
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
            Name = IndexBufferName
        });

        _indexFormat = indexFormat;
        _indexCount = indexCount;

        _vertexCount = vertexCount;
        _vertexStride = vertexStride;

        Name = name;
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
        EncureVertexBufferSize(size);
        _device.WriteBuffer(_vertexBuffer, offset, (byte*)data, size);
    }



    public void UpdateIndex(ReadOnlySpan<uint> indices, uint offset = 0)
    {
        fixed (void* ptr = indices)
        {
            UpdateIndex(ptr, (uint)(indices.Length * sizeof(uint)), offset, (uint)indices.Length, IndexFormat.UInt32);
        }
    }

    public void UpdateIndex(ReadOnlySpan<ushort> indices, uint offset = 0)
    {
        fixed (void* ptr = indices)
        {
            UpdateIndex(ptr, (uint)(indices.Length * sizeof(ushort)), offset, (uint)indices.Length, IndexFormat.UInt16);
        }
    }


    public void UpdateIndex(void* data, uint size, uint offset, uint indexCount, IndexFormat indexFormat)
    {
        EncureIndexBufferSize(size);
        _indexFormat = indexFormat;
        _indexCount = indexCount;

        _device.WriteBuffer(_indexBuffer, offset, (byte*)data, size);
    }

    public virtual SubMeshData GetSubMesh(int index)
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
        if (disposing)
        {
            //dispose non-private managed resources
            _indexBuffer.Dispose();
            _vertexBuffer.Dispose();
        }
    }

    protected void EncureVertexBufferSize(uint size)
    {
        if (_vertexBuffer.Size < size)
        {
            _vertexBuffer.Dispose();
            _vertexBuffer = _device.CreateBuffer(new BufferDescriptor
            {
                Size = size,
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Name = VertexBufferName
            });
        }
    }

    protected void EncureIndexBufferSize(uint size)
    {
        if (_indexBuffer.Size < size)
        {
            _indexBuffer.Dispose();
            _indexBuffer = _device.CreateBuffer(new BufferDescriptor
            {
                Size = size,
                Usage = BufferUsage.Index | BufferUsage.CopyDst,
                Name = IndexBufferName
            });
        }
    }

    protected static uint GetIndexSize(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => sizeof(ushort),
            IndexFormat.UInt32 => sizeof(uint),
            _ => throw new InvalidOperationException("Invalid index format.")
        };
    }
}

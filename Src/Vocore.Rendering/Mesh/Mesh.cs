using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public unsafe class Mesh : ShaderResource, IMesh
{
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
        _vertexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = vertexCount * vertexStride,
            Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
            Name = $"{name}_vertex_buffer"
        });

        _indexBuffer = device.CreateBuffer(new BufferDescriptor
        {
            Size = indexCount * GetIndexSize(indexFormat),
            Usage = BufferUsage.Index | BufferUsage.CopyDst,
            Name = $"{name}_index_buffer"
        });

        _indexFormat = indexFormat;
        _indexCount = indexCount;

        _vertexCount = vertexCount;
        _vertexStride = vertexStride;

        Name = name;
    }

    public void UpdateVertex(void* data, uint size, uint offset = 0)
    {
        _device.WriteBuffer(_vertexBuffer, offset, (byte*)data, size);
    }

    public void UpdateVertex<T>(T[] data, uint offset = 0) where T : unmanaged
    {
        fixed (void* ptr = data)
        {
            UpdateVertex(ptr, (uint)(data.Length * sizeof(T)), offset);
        }
    }

    public void UpdateIndex(void* data, uint size, uint offset = 0)
    {
        _device.WriteBuffer(_indexBuffer, offset, (byte*)data, size);
    }


    public void UpdateIndex(uint[] indices, uint offset = 0)
    {
        if (_indexFormat != IndexFormat.Uint32)
        {
            throw new InvalidOperationException($"Index format mismatch. Required: Uint32(uint), Actual: {_indexFormat}");
        }

        fixed (void* ptr = indices)
        {
            UpdateIndex(ptr, (uint)(indices.Length * sizeof(uint)), offset);
        }
    }

    public void UpdateIndex(ushort[] indices, uint offset = 0)
    {
        if (_indexFormat != IndexFormat.Uint16)
        {
            throw new InvalidOperationException("Index format mismatch. Required: Uint16(ushort), Actual: " + _indexFormat);
        }

        fixed (void* ptr = indices)
        {
            UpdateIndex(ptr, (uint)(indices.Length * sizeof(ushort)), offset);
        }
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
        _indexBuffer.Dispose();
        _vertexBuffer.Dispose();
    }

    private static uint GetIndexSize(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.Uint16 => sizeof(ushort),
            IndexFormat.Uint32 => sizeof(uint),
            _ => throw new InvalidOperationException("Invalid index format.")
        };
    }
}

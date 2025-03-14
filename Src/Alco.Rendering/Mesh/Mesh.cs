using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public abstract class Mesh : AutoDisposable
{
    private readonly GPUBuffer _vertexBuffer;
    private readonly GPUBuffer _indexBuffer;
    private readonly IndexFormat _indexFormat;
    private readonly uint _indexCount;

    protected readonly GPUDevice _device;

    public string Name { get; }

    //high frequency access, use AggressiveInlining to optimize

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
    public IndexFormat IndexFormat {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexFormat;
    }
    public uint IndexCount {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _indexCount;
    }

    public uint VertexCount { get; }
    public uint VertexStride { get; }

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

        VertexCount = vertexCount;
        VertexStride = vertexStride;
    }

    public abstract uint SubMeshCount { get; }
    public abstract SubMeshData GetSubMesh(int index);

    public static uint GetIndexSize(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => sizeof(ushort),
            IndexFormat.UInt32 => sizeof(uint),
            _ => throw new InvalidOperationException("Invalid index format.")
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
        }
    }
}


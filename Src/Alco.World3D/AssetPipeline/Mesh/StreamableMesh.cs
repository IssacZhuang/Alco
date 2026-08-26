using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// A read-only multi-submesh mesh whose vertex/index buffers were streamed from a mesh asset
/// asset (.amsh). Submeshes are byte ranges derived from the asset's submesh table. Constructed
/// by <c>MeshAsset</c> once a LOD's payload has been read; upload via the
/// <c>Upload</c> methods and finalize with <see cref="MarkReady"/>. Consumed by the renderer
/// like any other <see cref="Mesh"/>.
/// </summary>
public sealed unsafe class StreamableMesh : Mesh
{
    private SubMeshData[] _subMeshes = Array.Empty<SubMeshData>();
    private bool _isReady;

    /// <summary>
    /// Gets the LOD level this residency was loaded from.
    /// </summary>
    public int LodIndex { get; }

    /// <summary>
    /// Gets the interleaved vertex stride in bytes.
    /// </summary>
    public uint VertexStride { get; }

    /// <summary>
    /// Gets a value indicating whether the upload finished and the mesh is drawable.
    /// </summary>
    public bool IsReady
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _isReady;
    }

    /// <summary>
    /// Initializes a new mesh asset with the given buffer sizes. Buffers are created empty;
    /// fill them with <see cref="UploadVertex"/> / <see cref="UploadIndices"/>.
    /// </summary>
    /// <param name="device">The GPU device used to create the buffers.</param>
    /// <param name="vertexBufferSize">Vertex payload size in bytes.</param>
    /// <param name="indexBufferSize">Index payload size in bytes.</param>
    /// <param name="vertexStride">Interleaved vertex stride in bytes.</param>
    /// <param name="lodIndex">The LOD level this mesh was loaded from.</param>
    /// <param name="name">The mesh name.</param>
    public StreamableMesh(GPUDevice device, uint vertexBufferSize, uint indexBufferSize, uint vertexStride, int lodIndex, string name = "streamable_mesh")
        : base(device, vertexBufferSize, indexBufferSize, name)
    {
        VertexStride = vertexStride;
        LodIndex = lodIndex;
    }

    /// <summary>
    /// Upload a range of the vertex payload from unmanaged memory.
    /// Must be called on the thread that owns the GPU device.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="size">The number of bytes to write.</param>
    /// <param name="offset">Byte offset inside the vertex buffer.</param>
    public void UploadVertex(void* data, uint size, uint offset = 0)
    {
        if (offset + size > VertexBuffer.Size)
        {
            throw new InvalidOperationException(
                $"Vertex upload out of range. offset: {offset}, size: {size}, buffer size: {VertexBuffer.Size}");
        }

        _device.WriteBuffer(VertexBuffer, offset, (byte*)data, size);
    }

    /// <summary>
    /// Upload a range of the vertex payload from a span.
    /// Must be called on the thread that owns the GPU device.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="offset">Byte offset inside the vertex buffer.</param>
    public void UploadVertex(ReadOnlySpan<byte> data, uint offset = 0)
    {
        fixed (void* ptr = data)
        {
            UploadVertex(ptr, (uint)data.Length, offset);
        }
    }

    /// <summary>
    /// Upload a range of the index payload from unmanaged memory. The trailing 1-3 bytes of an
    /// unaligned write are padded to 4 bytes as required by queue writes.
    /// Must be called on the thread that owns the GPU device.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="size">The number of bytes to write.</param>
    /// <param name="offset">Byte offset inside the index buffer.</param>
    public void UploadIndices(void* data, uint size, uint offset = 0)
    {
        WriteIndexDataAligned(data, size, offset);
        IncrementVersion();
    }

    /// <summary>
    /// Upload a range of the index payload from a span.
    /// Must be called on the thread that owns the GPU device.
    /// </summary>
    /// <param name="data">The payload bytes.</param>
    /// <param name="offset">Byte offset inside the index buffer.</param>
    public void UploadIndices(ReadOnlySpan<byte> data, uint offset = 0)
    {
        fixed (void* ptr = data)
        {
            UploadIndices(ptr, (uint)data.Length, offset);
        }
    }

    /// <summary>
    /// Set the submesh table. Ranges are validated against the current buffer sizes.
    /// </summary>
    /// <param name="subMeshes">The submesh byte ranges.</param>
    public void SetSubMeshes(ReadOnlySpan<SubMeshData> subMeshes)
    {
        SubMeshData[] copy = subMeshes.ToArray();
        for (int i = 0; i < copy.Length; i++)
        {
            ref SubMeshData subMesh = ref copy[i];
            if (subMesh.VertexOffset + subMesh.VertexSize > VertexBuffer.Size ||
                subMesh.IndexOffset + subMesh.IndexSize > IndexBuffer.Size)
            {
                throw new InvalidOperationException(
                    $"Submesh {i} out of range. Vertex: {subMesh.VertexOffset}+{subMesh.VertexSize}/{VertexBuffer.Size}, " +
                    $"Index: {subMesh.IndexOffset}+{subMesh.IndexSize}/{IndexBuffer.Size}");
            }
        }

        _subMeshes = copy;
        IncrementVersion();
    }

    /// <summary>
    /// Finalize the mesh after all uploads finished. Bumps the version so cached bindings refresh.
    /// </summary>
    public void MarkReady()
    {
        _isReady = true;
        IncrementVersion();
    }

    /// <inheritdoc />
    public override int SubMeshCount => _subMeshes.Length;

    /// <inheritdoc />
    public override SubMeshData GetSubMesh(int index)
    {
        return _subMeshes[index];
    }
}

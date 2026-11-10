using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Stores immutable model variants in one shared pair of vertex and index buffers.</summary>
public sealed unsafe class PackedMesh : Mesh
{
    private readonly SubMeshData[] _subMeshes;
    private readonly SubMeshData _wholeBuffer;

    /// <inheritdoc />
    public override int SubMeshCount => _subMeshes.Length;

    /// <summary>Uploads concatenated model geometry once, retaining each model's local indices.</summary>
    /// <param name="device">The device owning the shared buffers.</param>
    /// <param name="vertices">The concatenated vertices, without position or normal changes.</param>
    /// <param name="indices">The concatenated model-local indices.</param>
    /// <param name="subMeshes">The byte slices identifying each model's geometry.</param>
    /// <param name="name">The mesh's debugging name.</param>
    public PackedMesh(GPUDevice device, ReadOnlySpan<VertexPBR> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<SubMeshData> subMeshes, string name = "packed_mesh")
        : base(device, checked((uint)vertices.Length * (uint)sizeof(VertexPBR)),
            checked((uint)indices.Length * sizeof(uint)), name)
    {
        _subMeshes = subMeshes.ToArray();
        _wholeBuffer = new SubMeshData
        {
            Index = -1,
            VertexSize = checked((uint)vertices.Length * (uint)sizeof(VertexPBR)),
            IndexSize = checked((uint)indices.Length * sizeof(uint)),
            IndexCount = (uint)indices.Length,
            IndexFormat = IndexFormat.UInt32,
        };
        LocalBoundsMin = new Vector3(float.MaxValue);
        LocalBoundsMax = new Vector3(float.MinValue);
        for (int i = 0; i < vertices.Length; i++)
        {
            LocalBoundsMin = Vector3.Min(LocalBoundsMin, vertices[i].Position);
            LocalBoundsMax = Vector3.Max(LocalBoundsMax, vertices[i].Position);
        }

        try
        {
            fixed (VertexPBR* vertexPointer = vertices)
            fixed (uint* indexPointer = indices)
            {
                device.WriteBuffer(VertexBuffer, 0, (byte*)vertexPointer, (uint)_wholeBuffer.VertexSize);
                device.WriteBuffer(IndexBuffer, 0, (byte*)indexPointer, (uint)_wholeBuffer.IndexSize);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Gets a model slice, or the whole-buffer binding for indirect draws when the index is -1.</summary>
    /// <param name="index">The variant index, or -1 to bind both complete buffers.</param>
    /// <returns>The binding slice; whole-buffer bindings require variant-specific draw arguments.</returns>
    public override SubMeshData GetSubMesh(int index) => index == -1 ? _wholeBuffer : _subMeshes[index];

    /// <summary>Gets a variant's draw arguments relative to the whole-buffer binding.</summary>
    /// <param name="index">The variant index.</param>
    /// <returns>The index count, first index, and base vertex for one instance.</returns>
    public IndexedIndirectData GetDrawRange(int index)
    {
        SubMeshData subMesh = _subMeshes[index];
        return new IndexedIndirectData(subMesh.IndexCount, 1,
            checked((uint)(subMesh.IndexOffset / sizeof(uint))),
            checked((uint)(subMesh.VertexOffset / (uint)sizeof(VertexPBR))), 0);
    }
}

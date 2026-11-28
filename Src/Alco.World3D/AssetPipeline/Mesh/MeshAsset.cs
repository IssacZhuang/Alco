using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Descriptor of one LOD level of a <see cref="MeshAsset"/>.
/// </summary>
public readonly struct MeshAssetLod
{
    /// <summary>Number of vertices of the LOD.</summary>
    public uint VertexCount { get; }

    /// <summary>Number of indices of the LOD.</summary>
    public uint IndexCount { get; }

    /// <summary>Maximum geometric error of the LOD relative to the source.</summary>
    public float MaxError { get; }

    /// <summary>LOD bounds.</summary>
    public BoundingBox3D Bounds { get; }

    /// <summary>Content entry name of the vertex payload.</summary>
    public string VertexEntry { get; }

    /// <summary>Content entry name of the index payload.</summary>
    public string IndexEntry { get; }

    /// <summary>Content entry name of the optional M3 cluster table, or empty when absent.</summary>
    public string ClusterEntry { get; }

    /// <summary>Creates a LOD descriptor.</summary>
    /// <param name="vertexCount">Vertex count.</param>
    /// <param name="indexCount">Index count.</param>
    /// <param name="maxError">Maximum geometric error.</param>
    /// <param name="bounds">LOD bounds.</param>
    /// <param name="vertexEntry">Vertex payload entry name.</param>
    /// <param name="indexEntry">Index payload entry name.</param>
    /// <param name="clusterEntry">Optional M3 cluster table entry name.</param>
    public MeshAssetLod(uint vertexCount, uint indexCount, float maxError, in BoundingBox3D bounds,
        string vertexEntry, string indexEntry, string clusterEntry = "")
    {
        VertexCount = vertexCount;
        IndexCount = indexCount;
        MaxError = maxError;
        Bounds = bounds;
        VertexEntry = vertexEntry;
        IndexEntry = indexEntry;
        ClusterEntry = clusterEntry;
    }
}

/// <summary>
/// Descriptor of one submesh (material slot) of a <see cref="MeshAsset"/>: a named index range.
/// </summary>
public readonly struct MeshAssetSubMesh
{
    /// <summary>The slot name (typically the source material name).</summary>
    public string Name { get; }

    /// <summary>First index in the owning LOD's index buffer.</summary>
    public uint FirstIndex { get; }

    /// <summary>Number of indices of the submesh.</summary>
    public uint IndexCount { get; }

    /// <summary>Creates a submesh descriptor.</summary>
    /// <param name="name">The slot name.</param>
    /// <param name="firstIndex">First index in the owning LOD's index buffer.</param>
    /// <param name="indexCount">Number of indices.</param>
    public MeshAssetSubMesh(string name, uint firstIndex, uint indexCount)
    {
        Name = name;
        FirstIndex = firstIndex;
        IndexCount = indexCount;
    }
}

/// <summary>
/// Pure CPU representation of one decoded mesh LOD read from a <see cref="MeshAsset"/>.
/// </summary>
public sealed class MeshLodData
{
    private readonly MeshVertexStream[] _streams;
    private readonly MeshAssetSubMesh[] _subMeshes;

    internal MeshLodData(
        int lodIndex,
        byte[] vertexData,
        byte[] indexData,
        IndexType indexType,
        uint vertexStride,
        uint vertexCount,
        uint indexCount,
        MeshVertexStream[] streams,
        MeshAssetSubMesh[] subMeshes)
    {
        LodIndex = lodIndex;
        VertexData = vertexData;
        IndexData = indexData;
        IndexType = indexType;
        VertexStride = vertexStride;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        _streams = streams;
        _subMeshes = subMeshes;
    }

    /// <summary>Zero-based LOD index.</summary>
    public int LodIndex { get; }

    /// <summary>Hash-verified interleaved vertex bytes.</summary>
    public byte[] VertexData { get; }

    /// <summary>Hash-verified index bytes.</summary>
    public byte[] IndexData { get; }

    /// <summary>Element format stored in <see cref="IndexData"/>.</summary>
    public IndexType IndexType { get; }

    /// <summary>Byte stride per vertex in <see cref="VertexData"/>.</summary>
    public uint VertexStride { get; }

    /// <summary>Number of vertices in <see cref="VertexData"/>.</summary>
    public uint VertexCount { get; }

    /// <summary>Number of indices in <see cref="IndexData"/>.</summary>
    public uint IndexCount { get; }

    /// <summary>Stream descriptors describing the vertex layout.</summary>
    public IReadOnlyList<MeshVertexStream> Streams => _streams;

    /// <summary>Submesh index ranges within this LOD.</summary>
    public IReadOnlyList<MeshAssetSubMesh> SubMeshes => _subMeshes;

    /// <summary>
    /// Extracts compacted <see cref="VertexPBR"/> vertices, zero-based 32-bit triangle indices,
    /// and tight local bounds for a single submesh in this LOD.
    /// </summary>
    /// <param name="submeshIndex">Zero-based submesh index within <see cref="SubMeshes"/>.</param>
    /// <param name="vertices">Receives the compacted PBR vertices referenced by the submesh.</param>
    /// <param name="indices">Receives submesh-local 32-bit triangle indices.</param>
    /// <param name="boundsMin">Receives the tight local minimum bound.</param>
    /// <param name="boundsMax">Receives the tight local maximum bound.</param>
    /// <returns><see langword="true"/> when the submesh and stream formats were valid for World3D extraction.</returns>
    public bool TryExtractWorld3DGeometry(
        int submeshIndex,
        out VertexPBR[] vertices,
        out uint[] indices,
        out Vector3 boundsMin,
        out Vector3 boundsMax)
    {
        vertices = [];
        indices = [];
        boundsMin = default;
        boundsMax = default;

        if ((uint)submeshIndex >= (uint)_subMeshes.Length || VertexStride == 0 || VertexCount == 0)
            return false;

        MeshAssetSubMesh subMesh = _subMeshes[submeshIndex];
        if (subMesh.IndexCount == 0 || subMesh.IndexCount % 3 != 0 ||
            (ulong)subMesh.FirstIndex + subMesh.IndexCount > IndexCount)
            return false;

        if (!TryFindStream(MeshStreamSemantic.Position, out MeshVertexStream posStream) ||
            posStream.Format is not (MeshVertexFormat.R32G32B32Float or MeshVertexFormat.R32G32B32A32Float) ||
            posStream.Offset + MeshVertexLayout.GetFormatSize(posStream.Format) > VertexStride)
            return false;

        bool hasNormal = TryFindStream(MeshStreamSemantic.Normal, out MeshVertexStream normalStream) &&
            IsValidNormalFormat(normalStream.Format) &&
            normalStream.Offset + MeshVertexLayout.GetFormatSize(normalStream.Format) <= VertexStride;
        bool hasUv = TryFindStream(MeshStreamSemantic.TexCoord0, out MeshVertexStream uvStream) &&
            IsValidUvFormat(uvStream.Format) &&
            uvStream.Offset + MeshVertexLayout.GetFormatSize(uvStream.Format) <= VertexStride;
        bool hasTangent = TryFindStream(MeshStreamSemantic.Tangent, out MeshVertexStream tangentStream) &&
            IsValidTangentFormat(tangentStream.Format) &&
            tangentStream.Offset + MeshVertexLayout.GetFormatSize(tangentStream.Format) <= VertexStride;

        int indexCount = checked((int)subMesh.IndexCount);
        int vertexCountInt = checked((int)VertexCount);
        int[] remap = ArrayPool<int>.Shared.Rent(vertexCountInt);
        try
        {
            remap.AsSpan(0, vertexCountInt).Fill(-1);
            indices = new uint[indexCount];
            int compactCount = 0;

            ReadOnlySpan<byte> indexBytes = IndexData;
            for (int i = 0; i < indexCount; ++i)
            {
                uint srcIndex = ReadIndex(indexBytes, subMesh.FirstIndex + (uint)i, IndexType);
                if (srcIndex >= VertexCount)
                {
                    vertices = [];
                    indices = [];
                    return false;
                }

                int mapped = remap[(int)srcIndex];
                if (mapped < 0)
                {
                    mapped = compactCount++;
                    remap[(int)srcIndex] = mapped;
                }
                indices[i] = (uint)mapped;
            }

            vertices = new VertexPBR[compactCount];
            ReadOnlySpan<byte> vertexBytes = VertexData;
            int strideInt = checked((int)VertexStride);
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);

            for (int i = 0; i < indexCount; ++i)
            {
                uint srcVertex = ReadIndex(indexBytes, subMesh.FirstIndex + (uint)i, IndexType);
                int dstVertex = (int)indices[i];
                if (remap[(int)srcVertex] == int.MaxValue)
                    continue;
                remap[(int)srcVertex] = int.MaxValue;

                int baseOffset = checked((int)srcVertex * strideInt);
                Vector3 position = ReadVector3(vertexBytes.Slice(baseOffset + (int)posStream.Offset));
                if (!IsFinite(position))
                {
                    vertices = [];
                    indices = [];
                    return false;
                }

                Vector3 normal = hasNormal
                    ? ReadNormal(vertexBytes.Slice(baseOffset + (int)normalStream.Offset), normalStream.Format)
                    : Vector3.UnitZ;
                Vector2 uv = hasUv
                    ? ReadUv(vertexBytes.Slice(baseOffset + (int)uvStream.Offset), uvStream.Format)
                    : Vector2.Zero;
                Vector4 tangent = hasTangent
                    ? ReadTangent(vertexBytes.Slice(baseOffset + (int)tangentStream.Offset), tangentStream.Format)
                    : new Vector4(1f, 0f, 0f, 1f);

                vertices[dstVertex] = new VertexPBR(position, normal, uv, tangent);
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            boundsMin = min;
            boundsMax = max;
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(remap);
        }
    }

    private bool TryFindStream(MeshStreamSemantic semantic, out MeshVertexStream stream)
    {
        foreach (MeshVertexStream candidate in _streams)
        {
            if (candidate.Semantic == semantic)
            {
                stream = candidate;
                return true;
            }
        }
        stream = default;
        return false;
    }

    private static uint ReadIndex(ReadOnlySpan<byte> indexBytes, uint indexSlot, IndexType indexType)
    {
        return indexType switch
        {
            IndexType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(indexBytes.Slice(checked((int)(indexSlot * 2u)), 2)),
            IndexType.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(indexBytes.Slice(checked((int)(indexSlot * 4u)), 4)),
            _ => throw new NotSupportedException($"Unsupported mesh index type {indexType}."),
        };
    }

    private static bool IsValidNormalFormat(MeshVertexFormat format) => format is
        MeshVertexFormat.R32G32B32Float or MeshVertexFormat.R32G32B32A32Float or
        MeshVertexFormat.R16G16B16A16Float or MeshVertexFormat.R8G8B8A8Unorm;

    private static bool IsValidUvFormat(MeshVertexFormat format) => format is
        MeshVertexFormat.R32G32Float or MeshVertexFormat.R16G16Float;

    private static bool IsValidTangentFormat(MeshVertexFormat format) => format is
        MeshVertexFormat.R32G32B32A32Float or MeshVertexFormat.R16G16B16A16Float;

    private static Vector3 ReadVector3(ReadOnlySpan<byte> src) => new(
        BinaryPrimitives.ReadSingleLittleEndian(src),
        BinaryPrimitives.ReadSingleLittleEndian(src[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(src[8..]));

    private static Vector3 ReadNormal(ReadOnlySpan<byte> src, MeshVertexFormat format)
    {
        return format switch
        {
            MeshVertexFormat.R32G32B32Float or MeshVertexFormat.R32G32B32A32Float => ReadVector3(src),
            MeshVertexFormat.R16G16B16A16Float => new Vector3(
                (float)BinaryPrimitives.ReadHalfLittleEndian(src),
                (float)BinaryPrimitives.ReadHalfLittleEndian(src[2..]),
                (float)BinaryPrimitives.ReadHalfLittleEndian(src[4..])),
            MeshVertexFormat.R8G8B8A8Unorm => new Vector3(
                src[0] * (2f / 255f) - 1f,
                src[1] * (2f / 255f) - 1f,
                src[2] * (2f / 255f) - 1f),
            _ => Vector3.UnitZ,
        };
    }

    private static Vector2 ReadUv(ReadOnlySpan<byte> src, MeshVertexFormat format)
    {
        return format switch
        {
            MeshVertexFormat.R32G32Float => new Vector2(
                BinaryPrimitives.ReadSingleLittleEndian(src),
                BinaryPrimitives.ReadSingleLittleEndian(src[4..])),
            MeshVertexFormat.R16G16Float => new Vector2(
                (float)BinaryPrimitives.ReadHalfLittleEndian(src),
                (float)BinaryPrimitives.ReadHalfLittleEndian(src[2..])),
            _ => Vector2.Zero,
        };
    }

    private static Vector4 ReadTangent(ReadOnlySpan<byte> src, MeshVertexFormat format)
    {
        return format switch
        {
            MeshVertexFormat.R32G32B32A32Float => new Vector4(
                BinaryPrimitives.ReadSingleLittleEndian(src),
                BinaryPrimitives.ReadSingleLittleEndian(src[4..]),
                BinaryPrimitives.ReadSingleLittleEndian(src[8..]),
                BinaryPrimitives.ReadSingleLittleEndian(src[12..])),
            MeshVertexFormat.R16G16B16A16Float => new Vector4(
                (float)BinaryPrimitives.ReadHalfLittleEndian(src),
                (float)BinaryPrimitives.ReadHalfLittleEndian(src[2..]),
                (float)BinaryPrimitives.ReadHalfLittleEndian(src[4..]),
                (float)BinaryPrimitives.ReadHalfLittleEndian(src[6..])),
            _ => new Vector4(1f, 0f, 0f, 1f),
        };
    }

    private static bool IsFinite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

/// <summary>
/// Pure CPU/IO handle to a mesh asset package (<c>.amsh</c>): parsed headers, bounds, M3 cluster/page
/// metadata, and hash-verified positional LOD reads. Contains no GPU dependency.
/// </summary>
public sealed class MeshAsset : AutoDisposable
{
    // Interleaved LOD reads remain valid alongside M3 cluster/page metadata chunks.
    private const MeshAssetFlags SupportedInterleavedFlags =
        MeshAssetFlags.Interleaved | MeshAssetFlags.HasLods | MeshAssetFlags.HasClusters | MeshAssetFlags.Paged;

    private readonly MeshAssetReader _reader;
    private readonly MeshVertexStream[] _streams;
    private readonly MeshAssetLod[] _lods;
    private readonly MeshAssetSubMesh[] _subMeshes;
    private readonly uint[] _lodSubMeshFirst;
    private readonly uint[] _lodSubMeshCount;
    private readonly string _pageTableEntry;
    private readonly string _pageDataEntry;
    private readonly Lock _lock = new();

    /// <summary>Creates a mesh asset handle over an opened <see cref="MeshAssetReader"/>.</summary>
    /// <param name="reader">The opened reader; ownership transfers to this asset.</param>
    internal MeshAsset(MeshAssetReader reader)
    {
        _reader = reader;
        MeshAssetMeta meta = reader.Meta;

        Name = meta.Name;
        Bounds = meta.Bounds;
        Flags = meta.Flags;
        IndexType = meta.IndexFormat;
        LodCount = meta.Lods.Count;
        HasClusters = Flags.HasFlag(MeshAssetFlags.HasClusters);
        IsPaged = Flags.HasFlag(MeshAssetFlags.Paged);
        PageSize = meta.PageSize;
        _pageTableEntry = meta.PageTableEntry;
        _pageDataEntry = meta.PageDataEntry;

        _streams = new MeshVertexStream[meta.Streams.Count];
        for (int i = 0; i < _streams.Length; i++)
        {
            VertexStreamMeta stream = meta.Streams[i];
            _streams[i] = new MeshVertexStream
            {
                Semantic = stream.Semantic,
                Format = stream.Format,
                Offset = stream.Offset,
                Stride = stream.Stride,
                QuantBounds = stream.QuantBounds,
            };
        }

        _lods = new MeshAssetLod[meta.Lods.Count];
        _lodSubMeshFirst = new uint[meta.Lods.Count];
        _lodSubMeshCount = new uint[meta.Lods.Count];
        for (int i = 0; i < _lods.Length; i++)
        {
            MeshLodMeta lod = meta.Lods[i];
            _lods[i] = new MeshAssetLod(lod.VertexCount, lod.IndexCount, lod.MaxError, lod.Bounds,
                lod.VertexEntry, lod.IndexEntry, lod.ClusterEntry);
            _lodSubMeshFirst[i] = lod.SubMeshFirst;
            _lodSubMeshCount[i] = lod.SubMeshCount;

            if (lod.SubMeshFirst + lod.SubMeshCount > (uint)meta.SubMeshes.Count)
            {
                throw new InvalidDataException(
                    $"Mesh asset '{meta.Name}' LOD {i} submesh range [{lod.SubMeshFirst}, {lod.SubMeshFirst + lod.SubMeshCount}) exceeds the submesh table ({meta.SubMeshes.Count} entries).");
            }
        }

        _subMeshes = new MeshAssetSubMesh[meta.SubMeshes.Count];
        for (int i = 0; i < _subMeshes.Length; i++)
        {
            MeshSubMeshMeta subMesh = meta.SubMeshes[i];
            _subMeshes[i] = new MeshAssetSubMesh(subMesh.Name, subMesh.FirstIndex, subMesh.IndexCount);
        }
    }

    /// <summary>Gets the mesh name.</summary>
    public string Name { get; }

    /// <summary>Whole-mesh bounds, available before any payload load.</summary>
    public BoundingBox3D Bounds { get; }

    /// <summary>Feature flags of the file.</summary>
    public MeshAssetFlags Flags { get; }

    /// <summary>Global index element format.</summary>
    public IndexType IndexType { get; }

    /// <summary>Number of LOD levels in the file.</summary>
    public int LodCount { get; }

    /// <summary>Whether the file carries M3 cluster tables.</summary>
    public bool HasClusters { get; }

    /// <summary>Whether the file carries M3 fixed-size page tables and data.</summary>
    public bool IsPaged { get; }

    /// <summary>Fixed byte size per M3 page, or zero when unpaged.</summary>
    public uint PageSize { get; }

    /// <summary>Vertex stream descriptors of the interleaved payload.</summary>
    public ReadOnlySpan<MeshVertexStream> Streams => _streams;

    /// <summary>Opens a mesh asset over a seekable stream, taking stream ownership.</summary>
    /// <param name="stream">The seekable mesh asset stream.</param>
    /// <returns>The opened mesh asset.</returns>
    public static MeshAsset FromStream(Stream stream) => new(MeshAssetReader.Open(stream, string.Empty));

    /// <summary>Opens a mesh asset over an in-memory package byte span.</summary>
    /// <param name="data">The mesh asset package bytes.</param>
    /// <returns>The opened mesh asset.</returns>
    public static MeshAsset FromMemory(ReadOnlySpan<byte> data) => new(MeshAssetReader.OpenMemory(data));

    /// <summary>Gets a LOD descriptor.</summary>
    /// <param name="lodIndex">The zero-based LOD index.</param>
    /// <returns>A read-only reference to the LOD descriptor.</returns>
    public ref readonly MeshAssetLod GetLod(int lodIndex)
    {
        if ((uint)lodIndex >= (uint)_lods.Length)
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        return ref _lods[lodIndex];
    }

    /// <summary>Gets the submesh (material slot) descriptors of one LOD.</summary>
    /// <param name="lodIndex">The zero-based LOD index.</param>
    /// <returns>The LOD's submesh descriptors.</returns>
    public ReadOnlySpan<MeshAssetSubMesh> GetSubMeshes(int lodIndex)
    {
        if ((uint)lodIndex >= (uint)_lods.Length)
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        return _subMeshes.AsSpan((int)_lodSubMeshFirst[lodIndex], (int)_lodSubMeshCount[lodIndex]);
    }

    /// <summary>
    /// Synchronously reads and hash-verifies one LOD payload from the underlying package.
    /// </summary>
    /// <param name="lodIndex">The zero-based LOD index.</param>
    /// <returns>The decoded CPU LOD payload ready for geometry extraction.</returns>
    public MeshLodData ReadLod(int lodIndex)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if ((uint)lodIndex >= (uint)_lods.Length)
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        if ((Flags & ~SupportedInterleavedFlags) != 0)
            throw new NotSupportedException($"Mesh asset '{Name}' uses unsupported interleaved features: {Flags & ~SupportedInterleavedFlags}.");
        if (_streams.Length == 0)
            throw new InvalidDataException($"Mesh asset '{Name}' declares no vertex streams.");

        MeshAssetLod lod = _lods[lodIndex];
        uint stride = _streams[0].Stride;
        int indexElementSize = IndexType == IndexType.UInt16 ? sizeof(ushort) : sizeof(uint);
        byte[] vertexBytes = new byte[checked((int)(lod.VertexCount * (ulong)stride))];
        byte[] indexBytes = new byte[checked((int)(lod.IndexCount * (ulong)indexElementSize))];

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            MeshChunkMeta vertexChunk = _reader.GetChunk(lod.VertexEntry);
            MeshChunkMeta indexChunk = _reader.GetChunk(lod.IndexEntry);
            _reader.ReadChunk(vertexChunk, vertexBytes);
            _reader.ReadChunk(indexChunk, indexBytes);
        }

        MeshAssetSubMesh[] subMeshes = GetSubMeshes(lodIndex).ToArray();
        return new MeshLodData(lodIndex, vertexBytes, indexBytes, IndexType, stride,
            lod.VertexCount, lod.IndexCount, (MeshVertexStream[])_streams.Clone(), subMeshes);
    }

    /// <summary>
    /// Asynchronously reads and hash-verifies one LOD payload on a background worker.
    /// </summary>
    /// <param name="lodIndex">The zero-based LOD index.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A task completing with the decoded <see cref="MeshLodData"/>.</returns>
    public Task<MeshLodData> ReadLodAsync(int lodIndex, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if ((uint)lodIndex >= (uint)_lods.Length)
            throw new ArgumentOutOfRangeException(nameof(lodIndex), lodIndex, "LOD index out of range.");
        return Task.Run(() => ReadLod(lodIndex), cancellationToken);
    }

    /// <summary>Tries to resolve the package entry of one LOD's M3 cluster table.</summary>
    /// <param name="lodIndex">The zero-based LOD index.</param>
    /// <param name="entry">Receives the cluster table entry descriptor.</param>
    /// <returns><see langword="true"/> when the LOD carries a cluster table entry.</returns>
    public bool TryGetClusterTableChunk(int lodIndex, [NotNullWhen(true)] out PackageEntry? entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        entry = null;
        if ((uint)lodIndex >= (uint)_lods.Length)
            return false;
        string name = _lods[lodIndex].ClusterEntry;
        return !string.IsNullOrEmpty(name) && _reader.TryGetEntry(name, out entry);
    }

    /// <summary>Reads and hash-verifies the M3 cluster table records of one LOD.</summary>
    /// <param name="lodIndex">The zero-based LOD index.</param>
    /// <param name="clusters">Receives the decoded cluster records.</param>
    /// <returns><see langword="true"/> when a cluster table existed and was read.</returns>
    public bool TryReadClusters(int lodIndex, [NotNullWhen(true)] out ClusterRecord[]? clusters)
    {
        clusters = null;
        if (!TryGetClusterTableChunk(lodIndex, out PackageEntry? entry))
            return false;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            clusters = _reader.ReadStructChunk<ClusterRecord>(entry);
            return true;
        }
    }

    /// <summary>Tries to resolve the package entry of the M3 page table.</summary>
    /// <param name="entry">Receives the page table entry descriptor.</param>
    /// <returns><see langword="true"/> when the mesh carries a page table.</returns>
    public bool TryGetPageTableChunk([NotNullWhen(true)] out PackageEntry? entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        entry = null;
        return !string.IsNullOrEmpty(_pageTableEntry) && _reader.TryGetEntry(_pageTableEntry, out entry);
    }

    /// <summary>Reads and hash-verifies the M3 page records of a paged mesh asset.</summary>
    /// <param name="pages">Receives the decoded page descriptors.</param>
    /// <returns><see langword="true"/> when a page table existed and was read.</returns>
    public bool TryReadPages([NotNullWhen(true)] out PageRecord[]? pages)
    {
        pages = null;
        if (!TryGetPageTableChunk(out PackageEntry? entry))
            return false;
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            pages = _reader.ReadStructChunk<PageRecord>(entry);
            return true;
        }
    }

    /// <summary>Tries to resolve the package entry containing concatenated M3 cluster pages.</summary>
    /// <param name="entry">Receives the page data entry descriptor.</param>
    /// <returns><see langword="true"/> when the mesh carries page data.</returns>
    public bool TryGetPageDataChunk([NotNullWhen(true)] out PackageEntry? entry)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        entry = null;
        return !string.IsNullOrEmpty(_pageDataEntry) && _reader.TryGetEntry(_pageDataEntry, out entry);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _reader.Dispose();
    }
}

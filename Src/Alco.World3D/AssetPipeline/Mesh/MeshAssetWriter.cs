using System.IO.Hashing;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Input of one mesh asset LOD: an interleaved vertex payload, its index buffer, the geometric
/// error relative to the source, the LOD's submesh slot ranges, and optional M3 clusters.
/// </summary>
public sealed class MeshAssetBuildLod
{
    /// <summary>The interleaved vertex payload.</summary>
    public required byte[] Vertices { get; init; }

    /// <summary>The index buffer (UInt32 indices).</summary>
    public required uint[] Indices { get; init; }

    /// <summary>Maximum geometric error of this LOD relative to the source; 0 for the source LOD.</summary>
    public float MaxError { get; init; }

    /// <summary>Submesh slots, index ranges into <see cref="Indices"/>.</summary>
    public required IReadOnlyList<MeshSubMeshMeta> SubMeshes { get; init; }

    /// <summary>Optional M3 cluster records associated with this LOD.</summary>
    public ReadOnlyMemory<ClusterRecord> Clusters { get; init; }
}

/// <summary>
/// Input of a mesh asset build: the stream descriptors shared by all LODs, the LOD list,
/// and optional M3 paged cluster table data.
/// </summary>
public sealed class MeshAssetBuildData
{
    /// <summary>The mesh name.</summary>
    public required string Name { get; init; }

    /// <summary>xxHash64 of the source asset bytes; 0 when unknown.</summary>
    public ulong SourceHash { get; init; }

    /// <summary>Stream descriptors of the interleaved vertex payload, shared by all LODs.</summary>
    public required MeshVertexStream[] Streams { get; init; }

    /// <summary>The LOD list; entry 0 is the highest-detail LOD.</summary>
    public required IReadOnlyList<MeshAssetBuildLod> Lods { get; init; }

    /// <summary>Fixed page byte size when supplying <see cref="Pages"/> and <see cref="PageData"/>.</summary>
    public uint PageSize { get; init; }

    /// <summary>Optional M3 page descriptors written into the <c>pages/table</c> chunk.</summary>
    public ReadOnlyMemory<PageRecord> Pages { get; init; }

    /// <summary>Optional M3 concatenated page payload written into the <c>pages/data</c> chunk.</summary>
    public ReadOnlyMemory<byte> PageData { get; init; }
}

/// <summary>
/// Writes mesh asset packages (.amsh) with xxHash64 chunk checksums, interleaved LODs,
/// and optional M3 cluster and page chunks.
/// </summary>
public static class MeshAssetWriter
{
    /// <summary>Version of the cooking algorithm; bump to invalidate cook caches.</summary>
    public const uint CookerVersion = 1;

    private const int EntryAlignment = 16;

    /// <summary>
    /// Writes a build to an output stream.
    /// </summary>
    /// <param name="data">The build data.</param>
    /// <param name="output">The output stream.</param>
    public static void Write(MeshAssetBuildData data, Stream output)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        if (data.Streams.Length == 0)
            throw new InvalidDataException("Mesh asset needs at least one vertex stream.");
        if (data.Lods.Count == 0)
            throw new InvalidDataException("Mesh asset needs at least one LOD.");

        uint stride = data.Streams[0].Stride;
        foreach (MeshVertexStream stream in data.Streams)
        {
            if (stream.Stride != stride)
                throw new InvalidDataException("All streams must share the interleaved stride.");
        }

        bool hasClusters = false;
        foreach (MeshAssetBuildLod lod in data.Lods)
        {
            if (lod.Vertices.Length % stride != 0)
                throw new InvalidDataException($"Vertex payload size {lod.Vertices.Length} is not a multiple of stride {stride}.");
            foreach (MeshSubMeshMeta subMesh in lod.SubMeshes)
            {
                if (subMesh.FirstIndex + subMesh.IndexCount > (uint)lod.Indices.Length)
                    throw new InvalidDataException($"Submesh '{subMesh.Name}' range exceeds the LOD's index buffer.");
            }
            if (!lod.Clusters.IsEmpty)
                hasClusters = true;
        }

        bool hasPages = !data.Pages.IsEmpty || !data.PageData.IsEmpty;
        if (hasPages && (data.Pages.IsEmpty || data.PageData.IsEmpty || data.PageSize == 0))
            throw new InvalidDataException("Paged M3 mesh assets must provide positive PageSize, non-empty Pages, and non-empty PageData.");

        MeshAssetFlags flags = MeshAssetFlags.Interleaved;
        if (data.Lods.Count > 1)
            flags |= MeshAssetFlags.HasLods;
        if (hasClusters)
            flags |= MeshAssetFlags.HasClusters;
        if (hasPages)
            flags |= MeshAssetFlags.Paged;

        BoundingBox3D[] lodBounds = new BoundingBox3D[data.Lods.Count];
        BoundingBox3D wholeBounds = default;
        bool hasWholeBounds = false;
        for (int i = 0; i < data.Lods.Count; i++)
        {
            if (!TryComputePositionBounds(data.Streams, data.Lods[i].Vertices, out BoundingBox3D bounds))
                continue;
            lodBounds[i] = bounds;
            Vector3 min = hasWholeBounds ? Vector3.Min(wholeBounds.Min, bounds.Min) : bounds.Min;
            Vector3 max = hasWholeBounds ? Vector3.Max(wholeBounds.Max, bounds.Max) : bounds.Max;
            wholeBounds = new BoundingBox3D(min, max);
            hasWholeBounds = true;
        }

        string pageTableEntry = hasPages ? "pages/table" : string.Empty;
        string pageDataEntry = hasPages ? "pages/data" : string.Empty;
        MeshAssetMeta meta = new()
        {
            Name = data.Name,
            Version = MeshAssetFormatVersion.Current,
            Flags = flags,
            SourceHash = data.SourceHash,
            CookerVersion = CookerVersion,
            IndexFormat = IndexType.UInt32,
            Bounds = wholeBounds,
            PageSize = hasPages ? data.PageSize : 0u,
            PageTableEntry = pageTableEntry,
            PageDataEntry = pageDataEntry,
        };

        foreach (MeshVertexStream stream in data.Streams)
            meta.AddStream(new VertexStreamMeta(stream.Semantic, stream.Format, stream.Offset, stream.Stride, stream.QuantBounds));

        PackageBuilder<MeshAssetMeta> builder = new()
        {
            Meta = meta,
            EntryAlignment = EntryAlignment,
        };

        uint subMeshFirst = 0;
        for (int i = 0; i < data.Lods.Count; i++)
        {
            MeshAssetBuildLod lod = data.Lods[i];
            string vertexEntry = $"lod{i}/vertices";
            string indexEntry = $"lod{i}/indices";
            string clusterEntry = !lod.Clusters.IsEmpty ? $"lod{i}/clusters" : string.Empty;
            byte[] indexPayload = new byte[lod.Indices.Length * sizeof(uint)];
            System.Buffer.BlockCopy(lod.Indices, 0, indexPayload, 0, indexPayload.Length);

            meta.AddChunk(new MeshChunkMeta(vertexEntry, MeshChunkType.VertexData, MeshChunkCodec.None,
                (uint)lod.Vertices.Length, XxHash64.HashToUInt64(lod.Vertices)));
            meta.AddChunk(new MeshChunkMeta(indexEntry, MeshChunkType.IndexData, MeshChunkCodec.None,
                (uint)indexPayload.Length, XxHash64.HashToUInt64(indexPayload)));
            meta.AddLod(new MeshLodMeta((uint)(lod.Vertices.Length / stride), (uint)lod.Indices.Length, lod.MaxError, lodBounds[i],
                vertexEntry, indexEntry, subMeshFirst, (uint)lod.SubMeshes.Count, clusterEntry));

            foreach (MeshSubMeshMeta subMesh in lod.SubMeshes)
                meta.AddSubMesh(subMesh);

            subMeshFirst += (uint)lod.SubMeshes.Count;
            builder.AddOrUpdateFile(vertexEntry, lod.Vertices);
            builder.AddOrUpdateFile(indexEntry, indexPayload);

            if (!lod.Clusters.IsEmpty)
            {
                byte[] clusterPayload = MemoryMarshal.AsBytes(lod.Clusters.Span).ToArray();
                meta.AddChunk(new MeshChunkMeta(clusterEntry, MeshChunkType.ClusterTable, MeshChunkCodec.None,
                    (uint)clusterPayload.Length, XxHash64.HashToUInt64(clusterPayload)));
                builder.AddOrUpdateFile(clusterEntry, clusterPayload);
            }
        }

        if (hasPages)
        {
            byte[] pageTablePayload = MemoryMarshal.AsBytes(data.Pages.Span).ToArray();
            byte[] pageDataPayload = data.PageData.ToArray();
            meta.AddChunk(new MeshChunkMeta(pageTableEntry, MeshChunkType.PageTable, MeshChunkCodec.None,
                (uint)pageTablePayload.Length, XxHash64.HashToUInt64(pageTablePayload)));
            meta.AddChunk(new MeshChunkMeta(pageDataEntry, MeshChunkType.PageData, MeshChunkCodec.None,
                (uint)pageDataPayload.Length, XxHash64.HashToUInt64(pageDataPayload)));
            builder.AddOrUpdateFile(pageTableEntry, pageTablePayload);
            builder.AddOrUpdateFile(pageDataEntry, pageDataPayload);
        }

        builder.Build(output);
    }

    private static bool TryComputePositionBounds(MeshVertexStream[] streams, byte[] vertices, out BoundingBox3D bounds)
    {
        MeshVertexStream position = default;
        bool found = false;
        foreach (MeshVertexStream stream in streams)
        {
            if (stream.Semantic == MeshStreamSemantic.Position)
            {
                position = stream;
                found = true;
                break;
            }
        }

        if (!found || position.Format is not (MeshVertexFormat.R32G32B32Float or MeshVertexFormat.R32G32B32A32Float))
        {
            bounds = default;
            return false;
        }

        if (vertices.Length < position.Offset + 12)
        {
            bounds = default;
            return false;
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        unsafe
        {
            fixed (byte* ptr = vertices)
            {
                for (uint offset = position.Offset; offset + 12 <= vertices.Length; offset += position.Stride)
                {
                    Vector3 p = new(
                        *(float*)(ptr + offset),
                        *(float*)(ptr + offset + 4),
                        *(float*)(ptr + offset + 8));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
        }

        bounds = new BoundingBox3D(min, max);
        return true;
    }
}

using System.IO.Hashing;
using System.Numerics;
using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Input of one cooked mesh LOD: an interleaved vertex payload, its index buffer, the geometric
/// error relative to the source and the LOD's submesh slot ranges. Pure geometry — material
/// binding happens in the composition layer (prefab), never here.
/// </summary>
public sealed class CookedMeshBuildLod
{
    /// <summary>The interleaved vertex payload, directly GPU-consumable.</summary>
    public required byte[] Vertices { get; init; }

    /// <summary>The index buffer (UInt32 indices).</summary>
    public required uint[] Indices { get; init; }

    /// <summary>Maximum geometric error of this LOD relative to the source; 0 for the source LOD.</summary>
    public float MaxError { get; init; }

    /// <summary>Submesh slots, index ranges into <see cref="Indices"/>.</summary>
    public required IReadOnlyList<MeshSubMeshMeta> SubMeshes { get; init; }
}

/// <summary>
/// Input of a cooked mesh build: the stream descriptors shared by all LODs plus the LOD list.
/// Entry 0 is the highest-detail LOD. Keep submesh slot names and order aligned across LODs so
/// material binding by slot name is LOD-stable.
/// </summary>
public sealed class CookedMeshBuildData
{
    /// <summary>The mesh name.</summary>
    public required string Name { get; init; }

    /// <summary>xxHash64 of the source asset bytes; 0 when unknown.</summary>
    public ulong SourceHash { get; init; }

    /// <summary>Stream descriptors of the interleaved vertex payload, shared by all LODs.</summary>
    public required CookedVertexStream[] Streams { get; init; }

    /// <summary>The LOD list; entry 0 is the highest-detail LOD.</summary>
    public required IReadOnlyList<CookedMeshBuildLod> Lods { get; init; }
}

/// <summary>
/// Writes cooked mesh packages (.amsh). M1 emits interleaved LODs with codec None: the payload
/// bytes are the GPU bytes, loads need no parsing. Each LOD gets its own vertex/index entries
/// (<c>lodN/vertices</c>, <c>lodN/indices</c>) and its own submesh slot ranges.
/// </summary>
public static class CookedMeshWriter
{
    /// <summary>Version of the cooking algorithm; bump to invalidate cook caches.</summary>
    public const uint CookerVersion = 1;

    private const int EntryAlignment = 16;

    /// <summary>
    /// Write a build to an output stream.
    /// </summary>
    /// <param name="data">The build data.</param>
    /// <param name="output">The output stream.</param>
    public static void Write(CookedMeshBuildData data, Stream output)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);

        if (data.Streams.Length == 0)
        {
            throw new InvalidDataException("Cooked mesh needs at least one vertex stream.");
        }

        if (data.Lods.Count == 0)
        {
            throw new InvalidDataException("Cooked mesh needs at least one LOD.");
        }

        uint stride = data.Streams[0].Stride;
        foreach (CookedVertexStream stream in data.Streams)
        {
            if (stream.Stride != stride)
            {
                throw new InvalidDataException("All streams must share the interleaved stride.");
            }
        }

        foreach (CookedMeshBuildLod lod in data.Lods)
        {
            if (lod.Vertices.Length % stride != 0)
            {
                throw new InvalidDataException($"Vertex payload size {lod.Vertices.Length} is not a multiple of stride {stride}.");
            }

            foreach (MeshSubMeshMeta subMesh in lod.SubMeshes)
            {
                if (subMesh.FirstIndex + subMesh.IndexCount > (uint)lod.Indices.Length)
                {
                    throw new InvalidDataException($"Submesh '{subMesh.Name}' range exceeds the LOD's index buffer.");
                }
            }
        }

        CookedMeshFlags flags = CookedMeshFlags.Interleaved;
        if (data.Lods.Count > 1)
        {
            flags |= CookedMeshFlags.HasLods;
        }

        // LOD bounds come from the position streams; the whole-mesh bounds are their union so
        // they stay valid whichever LOD is resident.
        BoundingBox3D[] lodBounds = new BoundingBox3D[data.Lods.Count];
        BoundingBox3D wholeBounds = default;
        bool hasWholeBounds = false;
        for (int i = 0; i < data.Lods.Count; i++)
        {
            if (!TryComputePositionBounds(data.Streams, data.Lods[i].Vertices, out BoundingBox3D bounds))
            {
                continue;
            }

            lodBounds[i] = bounds;
            Vector3 min = hasWholeBounds ? Vector3.Min(wholeBounds.Min, bounds.Min) : bounds.Min;
            Vector3 max = hasWholeBounds ? Vector3.Max(wholeBounds.Max, bounds.Max) : bounds.Max;
            wholeBounds = new BoundingBox3D(min, max);
            hasWholeBounds = true;
        }

        CookedMeshMeta meta = new()
        {
            Name = data.Name,
            Version = CookedMeshFormatVersion.Current,
            Flags = flags,
            SourceHash = data.SourceHash,
            CookerVersion = CookerVersion,
            IndexFormat = Alco.Graphics.IndexFormat.UInt32,
            Bounds = wholeBounds,
        };

        foreach (CookedVertexStream stream in data.Streams)
        {
            meta.AddStream(new VertexStreamMeta(stream.Semantic, stream.Format, stream.Offset, stream.Stride, stream.QuantBounds));
        }

        PackageBuilder<CookedMeshMeta> builder = new()
        {
            Meta = meta,
            EntryAlignment = EntryAlignment,
        };

        uint subMeshFirst = 0;
        for (int i = 0; i < data.Lods.Count; i++)
        {
            CookedMeshBuildLod lod = data.Lods[i];
            string vertexEntry = $"lod{i}/vertices";
            string indexEntry = $"lod{i}/indices";
            byte[] indexPayload = new byte[lod.Indices.Length * sizeof(uint)];
            Buffer.BlockCopy(lod.Indices, 0, indexPayload, 0, indexPayload.Length);

            meta.AddChunk(new MeshChunkMeta(vertexEntry, MeshChunkType.VertexData, MeshChunkCodec.None,
                (uint)lod.Vertices.Length, XxHash64.HashToUInt64(lod.Vertices)));
            meta.AddChunk(new MeshChunkMeta(indexEntry, MeshChunkType.IndexData, MeshChunkCodec.None,
                (uint)indexPayload.Length, XxHash64.HashToUInt64(indexPayload)));
            meta.AddLod(new MeshLodMeta((uint)(lod.Vertices.Length / stride), (uint)lod.Indices.Length, lod.MaxError, lodBounds[i],
                vertexEntry, indexEntry, subMeshFirst, (uint)lod.SubMeshes.Count));

            foreach (MeshSubMeshMeta subMesh in lod.SubMeshes)
            {
                meta.AddSubMesh(subMesh);
            }

            subMeshFirst += (uint)lod.SubMeshes.Count;

            builder.AddOrUpdateFile(vertexEntry, lod.Vertices);
            builder.AddOrUpdateFile(indexEntry, indexPayload);
        }

        builder.Build(output);
    }

    private static bool TryComputePositionBounds(CookedVertexStream[] streams, byte[] vertices, out BoundingBox3D bounds)
    {
        CookedVertexStream position = default;
        bool found = false;
        foreach (CookedVertexStream stream in streams)
        {
            if (stream.Semantic == MeshStreamSemantic.Position)
            {
                position = stream;
                found = true;
                break;
            }
        }

        // Bounds need a float32 position stream; anything else yields no bounds.
        if (!found || position.Format != Alco.Graphics.VertexFormat.Float32x3)
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
                    min = Vector3.Min(min, new Vector3(
                        *(float*)(ptr + offset),
                        *(float*)(ptr + offset + 4),
                        *(float*)(ptr + offset + 8)));
                    max = Vector3.Max(max, new Vector3(
                        *(float*)(ptr + offset),
                        *(float*)(ptr + offset + 4),
                        *(float*)(ptr + offset + 8)));
                }
            }
        }

        bounds = new BoundingBox3D(min, max);
        return true;
    }
}

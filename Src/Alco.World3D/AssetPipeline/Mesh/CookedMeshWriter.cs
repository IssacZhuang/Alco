using System.IO.Hashing;
using System.Numerics;
using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Input of one cooked mesh LOD0: an interleaved vertex payload, its index buffer, stream
/// descriptors and the submesh slot table. Pure geometry — material binding happens in the
/// composition layer (prefab), never here.
/// </summary>
public sealed class CookedMeshBuildData
{
    /// <summary>The mesh name.</summary>
    public required string Name { get; init; }

    /// <summary>xxHash64 of the source asset bytes; 0 when unknown.</summary>
    public ulong SourceHash { get; init; }

    /// <summary>Stream descriptors of the interleaved vertex payload.</summary>
    public required CookedVertexStream[] Streams { get; init; }

    /// <summary>The interleaved LOD0 vertex payload, directly GPU-consumable.</summary>
    public required byte[] Vertices { get; init; }

    /// <summary>The LOD0 index buffer (UInt32 indices).</summary>
    public required uint[] Indices { get; init; }

    /// <summary>Submesh slots, index ranges into <see cref="Indices"/>.</summary>
    public required IReadOnlyList<MeshSubMeshMeta> SubMeshes { get; init; }
}

/// <summary>
/// Writes cooked mesh packages (.amsh). M1 emits a single interleaved LOD with codec None:
/// the payload bytes are the GPU bytes, loads need no parsing.
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

        uint stride = data.Streams[0].Stride;
        foreach (CookedVertexStream stream in data.Streams)
        {
            if (stream.Stride != stride)
            {
                throw new InvalidDataException("All streams must share the interleaved stride.");
            }
        }

        if (data.Vertices.Length % stride != 0)
        {
            throw new InvalidDataException($"Vertex payload size {data.Vertices.Length} is not a multiple of stride {stride}.");
        }

        BoundingBox3D bounds = ComputePositionBounds(data);

        const string vertexEntry = "lod0/vertices";
        const string indexEntry = "lod0/indices";
        byte[] indexPayload = new byte[data.Indices.Length * sizeof(uint)];
        Buffer.BlockCopy(data.Indices, 0, indexPayload, 0, indexPayload.Length);

        CookedMeshMeta meta = new()
        {
            Name = data.Name,
            Version = CookedMeshFormatVersion.Current,
            Flags = (uint)CookedMeshFlags.Interleaved,
            SourceHash = data.SourceHash,
            CookerVersion = CookerVersion,
            IndexFormat = (uint)Alco.Graphics.IndexFormat.UInt32,
            Bounds = bounds,
        };
        meta.AddChunk(new MeshChunkMeta(vertexEntry, (uint)MeshChunkType.VertexData, (uint)MeshChunkCodec.None,
            (uint)data.Vertices.Length, XxHash64.HashToUInt64(data.Vertices)));
        meta.AddChunk(new MeshChunkMeta(indexEntry, (uint)MeshChunkType.IndexData, (uint)MeshChunkCodec.None,
            (uint)indexPayload.Length, XxHash64.HashToUInt64(indexPayload)));

        foreach (CookedVertexStream stream in data.Streams)
        {
            meta.AddStream(new VertexStreamMeta((uint)stream.Semantic, (uint)stream.Format, stream.Offset, stream.Stride, stream.QuantBounds));
        }

        meta.AddLod(new MeshLodMeta((uint)(data.Vertices.Length / stride), (uint)data.Indices.Length, 0.0f, bounds, vertexEntry, indexEntry));

        foreach (MeshSubMeshMeta subMesh in data.SubMeshes)
        {
            meta.AddSubMesh(subMesh);
        }

        PackageBuilder<CookedMeshMeta> builder = new()
        {
            Meta = meta,
            EntryAlignment = EntryAlignment,
        };
        builder.AddOrUpdateFile(vertexEntry, data.Vertices);
        builder.AddOrUpdateFile(indexEntry, indexPayload);
        builder.Build(output);
    }

    private static BoundingBox3D ComputePositionBounds(CookedMeshBuildData data)
    {
        CookedVertexStream position = default;
        bool found = false;
        foreach (CookedVertexStream stream in data.Streams)
        {
            if (stream.Semantic == MeshStreamSemantic.Position)
            {
                position = stream;
                found = true;
                break;
            }
        }

        // Bounds need a float32 position stream; anything else yields a zero box.
        if (!found || position.Format != Alco.Graphics.VertexFormat.Float32x3)
        {
            return default;
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        ReadOnlySpan<byte> payload = data.Vertices;
        unsafe
        {
            fixed (byte* ptr = payload)
            {
                for (uint offset = position.Offset; offset + 12 <= payload.Length; offset += position.Stride)
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

        return new BoundingBox3D(min, max);
    }
}

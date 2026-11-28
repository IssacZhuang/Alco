using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using NUnit.Framework;

namespace Alco.World3D.Test;

/// <summary>
/// Tests mesh asset package writing, reading, hash verification, CPU LOD reads, compaction, and M3 cluster/page chunks.
/// </summary>
[Category("NoGPU")]
public class TestMeshAssetFormat
{
    private static VertexPBR[] CreateQuadVertices()
    {
        return
        [
            new VertexPBR { Position = new Vector3(0, 0, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(0, 0), Tangent = new Vector4(1, 0, 0, 1) },
            new VertexPBR { Position = new Vector3(1, 0, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(1, 0), Tangent = new Vector4(1, 0, 0, 1) },
            new VertexPBR { Position = new Vector3(1, 1, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(1, 1), Tangent = new Vector4(1, 0, 0, 1) },
            new VertexPBR { Position = new Vector3(0, 1, 0), Normal = new Vector3(0, 0, 1), UV = new Vector2(0, 1), Tangent = new Vector4(1, 0, 0, 1) },
        ];
    }

    private static byte[] VerticesToBytes(VertexPBR[] vertices)
    {
        byte[] bytes = new byte[vertices.Length * sizeof(float) * 12];
        int cursor = 0;
        void Write(Vector3 value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), value.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 4), value.Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 8), value.Z);
            cursor += 12;
        }

        foreach (VertexPBR vertex in vertices)
        {
            Write(vertex.Position);
            Write(vertex.Normal);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), vertex.UV.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 4), vertex.UV.Y);
            cursor += 8;
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor), vertex.Tangent.X);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 4), vertex.Tangent.Y);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 8), vertex.Tangent.Z);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(cursor + 12), vertex.Tangent.W);
            cursor += 16;
        }

        return bytes;
    }

    private static MeshAssetBuildData CreateBuildData()
    {
        return new MeshAssetBuildData
        {
            Name = "test_quad",
            SourceHash = 0x0123456789ABCDEF,
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = VerticesToBytes(CreateQuadVertices()),
                    Indices = [0, 1, 2, 0, 2, 3],
                    MaxError = 0.0f,
                    SubMeshes = [new MeshSubMeshMeta("primitive_0", 0, 6)],
                },
            ],
        };
    }

    private static MeshAssetBuildData CreateMultiLodBuildData()
    {
        VertexPBR[] quad = CreateQuadVertices();
        VertexPBR[] triangle = [quad[0], quad[1], quad[2]];
        return new MeshAssetBuildData
        {
            Name = "test_quad_lods",
            SourceHash = 0x0123456789ABCDEF,
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = VerticesToBytes(quad),
                    Indices = [0, 1, 2, 0, 2, 3],
                    MaxError = 0.0f,
                    SubMeshes = [new MeshSubMeshMeta("primitive_0", 0, 6)],
                },
                new MeshAssetBuildLod
                {
                    Vertices = VerticesToBytes(triangle),
                    Indices = [0, 1, 2],
                    MaxError = 0.5f,
                    SubMeshes = [new MeshSubMeshMeta("primitive_0", 0, 3)],
                },
            ],
        };
    }

    private static byte[] WritePackage(MeshAssetBuildData data)
    {
        using MemoryStream stream = new();
        MeshAssetWriter.Write(data, stream);
        return stream.ToArray();
    }

    /// <summary>Tests the mesh package envelope, PackedBinary metadata, and content-relative alignment.</summary>
    [Test]
    public void WriterProducesAlignedReadablePackage()
    {
        MeshAssetBuildData data = CreateBuildData();
        byte[] package = WritePackage(data);
        int metaLength = checked((int)BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(4, 8)));

        using PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenMemory(package);
        MeshAssetMeta meta = reader.Meta;

        Assert.Multiple(() =>
        {
            Assert.That(package.AsSpan(0, 4).SequenceEqual("amsh"u8), Is.True);
            Assert.That(package.AsSpan(12, metaLength).StartsWith("APKB"u8), Is.True);
            Assert.That(package.AsSpan(12 + metaLength, data.Lods[0].Vertices.Length).ToArray(),
                Is.EqualTo(data.Lods[0].Vertices));
            Assert.That(meta.Name, Is.EqualTo("test_quad"));
            Assert.That(meta.SourceHash, Is.EqualTo(0x0123456789ABCDEF));
            Assert.That(meta.CookerVersion, Is.EqualTo(MeshAssetWriter.CookerVersion));
            Assert.That(meta.Flags, Is.EqualTo(MeshAssetFlags.Interleaved));
            Assert.That(meta.IndexFormat, Is.EqualTo(IndexType.UInt32));
            Assert.That(meta.Lods.Count, Is.EqualTo(1));
            Assert.That(meta.Lods[0].VertexCount, Is.EqualTo(4));
            Assert.That(meta.Lods[0].IndexCount, Is.EqualTo(6));
            Assert.That(meta.Streams.Count, Is.EqualTo(4));
            Assert.That(meta.SubMeshes.Count, Is.EqualTo(1));
            Assert.That(meta.Bounds.Min, Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(meta.Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));

            foreach (PackageEntry entry in meta.Entries)
                Assert.That(entry.Start % 16, Is.EqualTo(0), $"entry {entry.Name} must be 16-byte aligned");
        });
    }

    /// <summary>Tests that the writer produces a multi-LOD package with partitioned submeshes.</summary>
    [Test]
    public void WriterProducesMultiLodPackage()
    {
        byte[] package = WritePackage(CreateMultiLodBuildData());

        using PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenMemory(package);
        MeshAssetMeta meta = reader.Meta;

        Assert.Multiple(() =>
        {
            Assert.That(meta.Flags, Is.EqualTo(MeshAssetFlags.Interleaved | MeshAssetFlags.HasLods));
            Assert.That(meta.Lods.Count, Is.EqualTo(2));
            Assert.That(meta.Lods[0].VertexCount, Is.EqualTo(4));
            Assert.That(meta.Lods[0].IndexCount, Is.EqualTo(6));
            Assert.That(meta.Lods[0].MaxError, Is.EqualTo(0.0f));
            Assert.That(meta.Lods[0].VertexEntry, Is.EqualTo("lod0/vertices"));
            Assert.That(meta.Lods[1].VertexCount, Is.EqualTo(3));
            Assert.That(meta.Lods[1].IndexCount, Is.EqualTo(3));
            Assert.That(meta.Lods[1].MaxError, Is.EqualTo(0.5f));
            Assert.That(meta.Lods[1].VertexEntry, Is.EqualTo("lod1/vertices"));
            Assert.That(meta.SubMeshes.Count, Is.EqualTo(2));
            Assert.That(meta.Bounds.Min, Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(meta.Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));
        });
    }

    /// <summary>Tests synchronous and asynchronous pure CPU LOD reads with xxHash64 verification.</summary>
    [Test]
    public async Task MeshAssetReadsLodsSynchronouslyAndAsynchronously()
    {
        MeshAssetBuildData data = CreateMultiLodBuildData();
        byte[] package = WritePackage(data);

        using MeshAsset mesh = MeshAsset.FromMemory(package);
        MeshLodData lod0 = mesh.ReadLod(0);
        MeshLodData lod1 = await mesh.ReadLodAsync(1);

        Assert.Multiple(() =>
        {
            Assert.That(mesh.Name, Is.EqualTo("test_quad_lods"));
            Assert.That(mesh.IndexType, Is.EqualTo(IndexType.UInt32));
            Assert.That(mesh.Streams[0].Format, Is.EqualTo(MeshVertexFormat.R32G32B32Float));
            Assert.That(MeshVertexLayout.GetByteSize(MeshVertexFormat.R32G32B32A32Float), Is.EqualTo(16));

            Assert.That(lod0.LodIndex, Is.EqualTo(0));
            Assert.That(lod0.VertexData, Is.EqualTo(data.Lods[0].Vertices));
            Assert.That(lod0.IndexCount, Is.EqualTo(6));
            Assert.That(lod0.SubMeshes.Count, Is.EqualTo(1));

            Assert.That(lod1.LodIndex, Is.EqualTo(1));
            Assert.That(lod1.VertexData, Is.EqualTo(data.Lods[1].Vertices));
            Assert.That(lod1.IndexCount, Is.EqualTo(3));
        });
    }

    /// <summary>Tests per-submesh vertex compaction, zero-based index remapping, and tight local bounds extraction.</summary>
    [Test]
    public void TryExtractWorld3DGeometryCompactsSubmeshVerticesAndComputesTightBounds()
    {
        VertexPBR[] vertices =
        [
            new(new Vector3(0, 0, 0), Vector3.UnitZ, new Vector2(0, 0)),
            new(new Vector3(2, 0, 0), Vector3.UnitZ, new Vector2(1, 0)),
            new(new Vector3(2, 2, 0), Vector3.UnitZ, new Vector2(1, 1)),
            new(new Vector3(10, 10, 5), Vector3.UnitY, new Vector2(0, 0)),
            new(new Vector3(14, 10, 5), Vector3.UnitY, new Vector2(1, 0)),
            new(new Vector3(14, 12, 8), Vector3.UnitY, new Vector2(1, 1)),
        ];
        MeshAssetBuildData data = new()
        {
            Name = "two_submeshes",
            Streams = MeshVertexLayout.CreatePBR(),
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = VerticesToBytes(vertices),
                    Indices = [0, 1, 2, 3, 4, 5],
                    SubMeshes =
                    [
                        new MeshSubMeshMeta("low", 0, 3),
                        new MeshSubMeshMeta("high", 3, 3),
                    ],
                },
            ],
        };

        using MeshAsset mesh = MeshAsset.FromMemory(WritePackage(data));
        MeshLodData lod0 = mesh.ReadLod(0);

        Assert.That(lod0.TryExtractWorld3DGeometry(1, out VertexPBR[] highVerts, out uint[] highIndices,
            out Vector3 min, out Vector3 max), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(highVerts.Length, Is.EqualTo(3));
            Assert.That(highIndices, Is.EqualTo(new uint[] { 0u, 1u, 2u }));
            Assert.That(highVerts[0].Position, Is.EqualTo(new Vector3(10, 10, 5)));
            Assert.That(highVerts[2].Position, Is.EqualTo(new Vector3(14, 12, 8)));
            Assert.That(min, Is.EqualTo(new Vector3(10, 10, 5)));
            Assert.That(max, Is.EqualTo(new Vector3(14, 12, 8)));
            Assert.That(lod0.TryExtractWorld3DGeometry(2, out _, out _, out _, out _), Is.False);
        });
    }

    /// <summary>Tests M3 cluster and page chunks coexist with standard interleaved LOD reads.</summary>
    [Test]
    public unsafe void M3ClusterAndPageChunksRoundTripAlongsideInterleavedLods()
    {
        ClusterRecord cluster = new()
        {
            ConeCutoff = 0.75f,
            MaxError = 0.25f,
            Flags = 3u,
            VertexOffset = 0u,
            VertexCount = 4u,
            IndexOffset = 0u,
            IndexCount = 6u,
            ParentGroup = -1,
            Lod = 0,
            PageIndex = 0u,
            MaterialSlot = 0,
            Reserved = 0,
        };
        cluster.BoundsMin[0] = 0f;
        cluster.BoundsMax[0] = 1f;
        cluster.Sphere[3] = 0.8f;

        byte[] pageBytes = [1, 2, 3, 4, 5, 6, 7, 8];
        PageRecord page = new()
        {
            Hash = 0x1122334455667788,
            ClusterFirst = 0u,
            ClusterCount = 1u,
            UncompressedSize = (uint)pageBytes.Length,
            Codec = (uint)MeshChunkCodec.None,
            Flags = 0u,
            Reserved = 0u,
        };

        MeshAssetBuildData data = new()
        {
            Name = "m3_ready",
            Streams = MeshVertexLayout.CreatePBR(),
            PageSize = 4096u,
            Pages = new[] { page },
            PageData = pageBytes,
            Lods =
            [
                new MeshAssetBuildLod
                {
                    Vertices = VerticesToBytes(CreateQuadVertices()),
                    Indices = [0, 1, 2, 0, 2, 3],
                    SubMeshes = [new MeshSubMeshMeta("primitive_0", 0, 6)],
                    Clusters = new[] { cluster },
                },
            ],
        };

        using MeshAsset mesh = MeshAsset.FromMemory(WritePackage(data));
        Assert.Multiple(() =>
        {
            Assert.That(mesh.HasClusters, Is.True);
            Assert.That(mesh.IsPaged, Is.True);
            Assert.That(mesh.PageSize, Is.EqualTo(4096u));
            Assert.That(mesh.TryGetClusterTableChunk(0, out PackageEntry? clusterEntry), Is.True);
            Assert.That(clusterEntry!.Name, Is.EqualTo("lod0/clusters"));
            Assert.That(mesh.TryReadClusters(0, out ClusterRecord[]? clusters), Is.True);
            Assert.That(clusters!.Length, Is.EqualTo(1));
            Assert.That(clusters[0].ConeCutoff, Is.EqualTo(0.75f));
            Assert.That(clusters[0].IndexCount, Is.EqualTo(6u));

            Assert.That(mesh.TryGetPageTableChunk(out PackageEntry? pageTableEntry), Is.True);
            Assert.That(pageTableEntry!.Name, Is.EqualTo("pages/table"));
            Assert.That(mesh.TryReadPages(out PageRecord[]? pages), Is.True);
            Assert.That(pages!.Length, Is.EqualTo(1));
            Assert.That(pages[0].Hash, Is.EqualTo(0x1122334455667788UL));
            Assert.That(mesh.TryGetPageDataChunk(out PackageEntry? pageDataEntry), Is.True);
            Assert.That(pageDataEntry!.Size, Is.EqualTo(pageBytes.Length));

            // Standard interleaved LOD reading remains intact when M3 flags are present.
            MeshLodData lod = mesh.ReadLod(0);
            Assert.That(lod.IndexCount, Is.EqualTo(6u));
        });
    }

    /// <summary>Tests that a tampered chunk fails xxHash64 verification.</summary>
    [Test]
    public void TamperedChunkFailsHashVerification()
    {
        byte[] package = WritePackage(CreateBuildData());

        using PackageReader<MeshAssetMeta> probe = PackageReader<MeshAssetMeta>.OpenMemory(package);
        probe.TryGetEntry("lod0/vertices", out PackageEntry? entry);
        long metaLength = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(4, 8));
        int absolute = (int)(12 + metaLength + entry!.Start + 5);
        package[absolute] ^= 0xFF;

        using MeshAsset mesh = MeshAsset.FromMemory(package);
        Assert.That(() => mesh.ReadLod(0), Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Tests that a disposed mesh asset rejects further reads.</summary>
    [Test]
    public void DisposedMeshRejectsFurtherUse()
    {
        byte[] package = WritePackage(CreateBuildData());
        MeshAsset mesh = MeshAsset.FromMemory(package);
        mesh.Dispose();
        mesh.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.IsDisposed, Is.True);
            Assert.That(() => mesh.ReadLod(0), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => mesh.ReadLodAsync(0), Throws.TypeOf<ObjectDisposedException>());
        });
    }
}

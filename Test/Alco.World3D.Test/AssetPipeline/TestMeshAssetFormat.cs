using System.Buffers.Binary;
using System.Numerics;
using NUnit.Framework;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

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

    [Test]
    public void WriterProducesAlignedReadablePackage()
    {
        byte[] package = WritePackage(CreateBuildData());

        using PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenMemory(package);
        MeshAssetMeta meta = reader.Meta;

        Assert.Multiple(() =>
        {
            Assert.That(meta.Name, Is.EqualTo("test_quad"));
            Assert.That(meta.SourceHash, Is.EqualTo(0x0123456789ABCDEF));
            Assert.That(meta.CookerVersion, Is.EqualTo(MeshAssetWriter.CookerVersion));
            Assert.That(meta.Flags, Is.EqualTo(MeshAssetFlags.Interleaved));
            Assert.That(meta.Lods.Count, Is.EqualTo(1));
            Assert.That(meta.Lods[0].VertexCount, Is.EqualTo(4));
            Assert.That(meta.Lods[0].IndexCount, Is.EqualTo(6));
            Assert.That(meta.Streams.Count, Is.EqualTo(4));
            Assert.That(meta.SubMeshes.Count, Is.EqualTo(1));
            Assert.That(meta.Bounds.Min, Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(meta.Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));
            Assert.That(meta.Lods[0].Bounds.Min, Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(meta.Lods[0].Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));

            // Entries must start content-relative 16-byte aligned (readers address
            // content-relative; absolute alignment is not part of the format).
            foreach (PackageEntry entry in meta.Entries)
            {
                Assert.That(entry.Start % 16, Is.EqualTo(0), $"entry {entry.Name} must be 16-byte aligned");
            }
        });
    }

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

            // The submesh table is partitioned per LOD; slot name order stays aligned.
            Assert.That(meta.SubMeshes.Count, Is.EqualTo(2));
            Assert.That(meta.Lods[0].SubMeshFirst, Is.EqualTo(0u));
            Assert.That(meta.Lods[0].SubMeshCount, Is.EqualTo(1u));
            Assert.That(meta.Lods[1].SubMeshFirst, Is.EqualTo(1u));
            Assert.That(meta.Lods[1].SubMeshCount, Is.EqualTo(1u));
            Assert.That(meta.SubMeshes[0].Name, Is.EqualTo("primitive_0"));
            Assert.That(meta.SubMeshes[0].IndexCount, Is.EqualTo(6));
            Assert.That(meta.SubMeshes[1].Name, Is.EqualTo("primitive_0"));
            Assert.That(meta.SubMeshes[1].IndexCount, Is.EqualTo(3));

            // Whole-mesh bounds are the union of the LOD bounds (both live in [0,1]^2).
            Assert.That(meta.Bounds.Min, Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(meta.Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));

            foreach (PackageEntry entry in meta.Entries)
            {
                Assert.That(entry.Start % 16, Is.EqualTo(0), $"entry {entry.Name} must be 16-byte aligned");
            }
        });
    }

    [Test]
    public void ReaderRoundTripsChunkBytes()
    {
        MeshAssetBuildData data = CreateBuildData();
        byte[] package = WritePackage(data);

        using MeshAssetReader reader = MeshAssetReader.OpenMemory(package);
        Assert.That(reader.TryGetEntrySize("lod0/vertices", out long vertexSize), Is.True);
        Assert.That(vertexSize, Is.EqualTo(data.Lods[0].Vertices.Length));

        using SafeMemoryHandle vertexData = new((int)vertexSize);
        reader.ReadChunk(reader.GetChunk("lod0/vertices"), vertexData);
        Assert.That(vertexData.AsReadOnlySpan().ToArray(), Is.EqualTo(data.Lods[0].Vertices));

        using SafeMemoryHandle indexData = new(6 * sizeof(uint));
        reader.ReadChunk(reader.GetChunk("lod0/indices"), indexData);
        uint[] indices = new uint[6];
        Buffer.BlockCopy(indexData.AsReadOnlySpan().ToArray(), 0, indices, 0, 24);
        Assert.That(indices, Is.EqualTo(data.Lods[0].Indices));
    }

    [Test]
    public void ReaderRoundTripsMultiLodChunkBytes()
    {
        MeshAssetBuildData data = CreateMultiLodBuildData();
        byte[] package = WritePackage(data);

        using MeshAssetReader reader = MeshAssetReader.OpenMemory(package);
        using SafeMemoryHandle vertexData = new(data.Lods[1].Vertices.Length);
        reader.ReadChunk(reader.GetChunk("lod1/vertices"), vertexData);
        Assert.That(vertexData.AsReadOnlySpan().ToArray(), Is.EqualTo(data.Lods[1].Vertices));

        using SafeMemoryHandle indexData = new(data.Lods[1].Indices.Length * sizeof(uint));
        reader.ReadChunk(reader.GetChunk("lod1/indices"), indexData);
        uint[] indices = new uint[data.Lods[1].Indices.Length];
        Buffer.BlockCopy(indexData.AsReadOnlySpan().ToArray(), 0, indices, 0, indices.Length * sizeof(uint));
        Assert.That(indices, Is.EqualTo(data.Lods[1].Indices));
    }

    [Test]
    public void TamperedChunkFailsHashVerification()
    {
        byte[] package = WritePackage(CreateBuildData());

        // Flip one payload byte inside the vertex entry (last byte of the file region is index
        // data, so corrupt a vertex byte right after the meta section via the entry offset).
        using PackageReader<MeshAssetMeta> probe = PackageReader<MeshAssetMeta>.OpenMemory(package);
        probe.TryGetEntry("lod0/vertices", out PackageEntry? entry);
        long metaLength = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(4, 8));
        int absolute = (int)(12 + metaLength + entry!.Start + 5);
        package[absolute] ^= 0xFF;

        using MeshAssetReader reader = MeshAssetReader.OpenMemory(package);
        using SafeMemoryHandle vertexData = new((int)entry.Size);
        Assert.That(() => reader.ReadChunk(reader.GetChunk("lod0/vertices"), vertexData),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void MeshAssetExposesStructureWithoutDevice()
    {
        byte[] package = WritePackage(CreateBuildData());

        using MeshAsset mesh = MeshAsset.FromMemory(package, device: null);
        Assert.Multiple(() =>
        {
            Assert.That(mesh.Name, Is.EqualTo("test_quad"));
            Assert.That(mesh.LodCount, Is.EqualTo(1));
            Assert.That(mesh.Bounds.Min, Is.EqualTo(new Vector3(0, 0, 0)));
            Assert.That(mesh.Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));
            Assert.That(mesh.GetLod(0).Bounds.Max, Is.EqualTo(new Vector3(1, 1, 0)));
            Assert.That(mesh.HasClusters, Is.False);
            Assert.That(mesh.Streams.Length, Is.EqualTo(4));
            Assert.That(mesh.Streams[0].Semantic, Is.EqualTo(MeshStreamSemantic.Position));
            Assert.That(mesh.Streams[0].Format, Is.EqualTo(VertexFormat.Float32x3));
            Assert.That(mesh.GetSubMeshes(0).Length, Is.EqualTo(1));
            Assert.That(mesh.GetSubMeshes(0)[0].IndexCount, Is.EqualTo(6));
            Assert.That(() => mesh.LoadLodAsync(0), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void MeshAssetExposesPerLodSubMeshes()
    {
        byte[] package = WritePackage(CreateMultiLodBuildData());

        using MeshAsset mesh = MeshAsset.FromMemory(package, device: null);
        Assert.Multiple(() =>
        {
            Assert.That(mesh.LodCount, Is.EqualTo(2));

            ReadOnlySpan<MeshAssetSubMesh> lod0 = mesh.GetSubMeshes(0);
            Assert.That(lod0.Length, Is.EqualTo(1));
            Assert.That(lod0[0].Name, Is.EqualTo("primitive_0"));
            Assert.That(lod0[0].FirstIndex, Is.EqualTo(0u));
            Assert.That(lod0[0].IndexCount, Is.EqualTo(6u));

            ReadOnlySpan<MeshAssetSubMesh> lod1 = mesh.GetSubMeshes(1);
            Assert.That(lod1.Length, Is.EqualTo(1));
            Assert.That(lod1[0].Name, Is.EqualTo("primitive_0"));
            Assert.That(lod1[0].FirstIndex, Is.EqualTo(0u));
            Assert.That(lod1[0].IndexCount, Is.EqualTo(3u));

            Assert.That(() => mesh.GetSubMeshes(2), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => mesh.LoadLodAsync(1), Throws.TypeOf<InvalidOperationException>());
        });
    }

    [Test]
    public void DisposedStreamRejectsFurtherUse()
    {
        byte[] package = WritePackage(CreateBuildData());

        MeshAsset mesh = MeshAsset.FromMemory(package, device: null);
        mesh.Dispose();
        mesh.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(mesh.IsDisposed, Is.True);
            Assert.That(mesh.TryGetLoadedLod(0, out StreamableMesh? _), Is.False);
            Assert.That(() => mesh.LoadLodAsync(0), Throws.TypeOf<ObjectDisposedException>());
        });
    }
}

using System.Buffers.Binary;
using System.Numerics;
using NUnit.Framework;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D.Test;

public class TestCookedMeshFormat
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

    private static CookedMeshBuildData CreateBuildData()
    {
        return new CookedMeshBuildData
        {
            Name = "test_quad",
            SourceHash = 0x0123456789ABCDEF,
            Streams = CookedVertexLayout.CreatePBR(),
            Vertices = VerticesToBytes(CreateQuadVertices()),
            Indices = [0, 1, 2, 0, 2, 3],
            SubMeshes = [new MeshSubMeshMeta("primitive_0", 0, 6)],
        };
    }

    private static byte[] WritePackage(CookedMeshBuildData data)
    {
        using MemoryStream stream = new();
        CookedMeshWriter.Write(data, stream);
        return stream.ToArray();
    }

    [Test]
    public void WriterProducesAlignedReadablePackage()
    {
        byte[] package = WritePackage(CreateBuildData());

        using PackageReader<CookedMeshMeta> reader = PackageReader<CookedMeshMeta>.OpenMemory(package);
        CookedMeshMeta meta = reader.Meta;

        Assert.Multiple(() =>
        {
            Assert.That(meta.Name, Is.EqualTo("test_quad"));
            Assert.That(meta.SourceHash, Is.EqualTo(0x0123456789ABCDEF));
            Assert.That(meta.CookerVersion, Is.EqualTo(CookedMeshWriter.CookerVersion));
            Assert.That((CookedMeshFlags)meta.Flags, Is.EqualTo(CookedMeshFlags.Interleaved));
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
    public void ReaderRoundTripsChunkBytes()
    {
        CookedMeshBuildData data = CreateBuildData();
        byte[] package = WritePackage(data);

        using MeshFileReader reader = MeshFileReader.OpenMemory(package);
        Assert.That(reader.TryGetEntrySize("lod0/vertices", out long vertexSize), Is.True);
        Assert.That(vertexSize, Is.EqualTo(data.Vertices.Length));

        using SafeMemoryHandle vertexData = new((int)vertexSize);
        reader.ReadChunk(reader.GetChunk("lod0/vertices"), vertexData);
        Assert.That(vertexData.AsReadOnlySpan().ToArray(), Is.EqualTo(data.Vertices));

        using SafeMemoryHandle indexData = new(6 * sizeof(uint));
        reader.ReadChunk(reader.GetChunk("lod0/indices"), indexData);
        uint[] indices = new uint[6];
        Buffer.BlockCopy(indexData.AsReadOnlySpan().ToArray(), 0, indices, 0, 24);
        Assert.That(indices, Is.EqualTo(data.Indices));
    }

    [Test]
    public void TamperedChunkFailsHashVerification()
    {
        byte[] package = WritePackage(CreateBuildData());

        // Flip one payload byte inside the vertex entry (last byte of the file region is index
        // data, so corrupt a vertex byte right after the meta section via the entry offset).
        using PackageReader<CookedMeshMeta> probe = PackageReader<CookedMeshMeta>.OpenMemory(package);
        probe.TryGetEntry("lod0/vertices", out PackageEntry? entry);
        long metaLength = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(4, 8));
        int absolute = (int)(12 + metaLength + entry!.Start + 5);
        package[absolute] ^= 0xFF;

        using MeshFileReader reader = MeshFileReader.OpenMemory(package);
        using SafeMemoryHandle vertexData = new((int)entry.Size);
        Assert.That(() => reader.ReadChunk(reader.GetChunk("lod0/vertices"), vertexData),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void MeshStreamExposesStructureWithoutDevice()
    {
        byte[] package = WritePackage(CreateBuildData());

        using MeshStream mesh = MeshStream.FromMemory(package, device: null);
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
            Assert.That(mesh.SubMeshes.Length, Is.EqualTo(1));
            Assert.That(mesh.SubMeshes[0].IndexCount, Is.EqualTo(6));
            Assert.That(() => mesh.LoadLodAsync(0), Throws.TypeOf<InvalidOperationException>());
        });
    }
}

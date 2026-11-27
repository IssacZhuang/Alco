using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using Alco.IO;

namespace Alco.IO.Test;

/// <summary>Tests package entry alignment and builder output consistency.</summary>
public class TestPackageBuilderAlignment
{
    private static (byte[] Package, long MetaLength) BuildPackage(int alignment)
    {
        PackageBuilder<PackageMeta> builder = new()
        {
            EntryAlignment = alignment,
        };
        builder.AddOrUpdateFile("a", new byte[] { 1, 2, 3 });
        builder.AddOrUpdateFile("b", new byte[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
        byte[] package = builder.Build();
        long metaLength = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(4, 8));
        return (package, metaLength);
    }

    /// <summary>Tests PackedBinary metadata framing and alignment relative to the content base.</summary>
    [Test]
    public void AlignmentMovesEntryStarts()
    {
        const int alignment = 16;
        (byte[] package, long metaLength) = BuildPackage(alignment);
        int contentBase = checked((int)(12 + metaLength));
        ReadOnlyMemory<byte> metaBytes = package.AsMemory(12, (int)metaLength);
        byte[] expected = [4, 5, 6, 7, 8, 9, 10, 11, 12, 13];

        using PackageReader<PackageMeta> reader = PackageReader<PackageMeta>.OpenMemory(package);
        Assert.Multiple(() =>
        {
            Assert.That(package.AsSpan(0, 4).SequenceEqual("alco"u8), Is.True);
            Assert.That(metaBytes.Span[..4].SequenceEqual("APKB"u8), Is.True);
            Assert.That(metaBytes.ToArray(), Is.EqualTo(PackedBinarySerializer.Encode(reader.Meta, elideDefaults: false)));
            Assert.That(reader.TryGetEntry("a", out PackageEntry? entryA), Is.True);
            Assert.That(entryA!.Start, Is.Zero);
            Assert.That(reader.TryGetEntry("b", out PackageEntry? entryB), Is.True);
            Assert.That(entryB!.Start, Is.EqualTo(alignment));
            Assert.That(package.AsSpan(contentBase, 3).ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(package.AsSpan(contentBase + alignment, expected.Length).ToArray(), Is.EqualTo(expected));

            byte[] buffer = new byte[entryB.Size];
            reader.ReadByEntry(entryB, buffer);
            Assert.That(buffer, Is.EqualTo(expected), "entry content must round-trip");
        });
    }

    /// <summary>Tests that alignment 1 packs entries back to back.</summary>
    [Test]
    public void DefaultAlignmentPacksBackToBack()
    {
        (byte[] package, long metaLength) = BuildPackage(1);

        using PackageReader<PackageMeta> reader = PackageReader<PackageMeta>.OpenMemory(package);
        Assert.Multiple(() =>
        {
            Assert.That(reader.TryGetEntry("a", out PackageEntry? entryA), Is.True);
            Assert.That(entryA!.Start, Is.EqualTo(0));
            Assert.That(reader.TryGetEntry("b", out PackageEntry? entryB), Is.True);
            Assert.That(entryB!.Start, Is.EqualTo(3), "no padding with alignment 1");
            // 12 header + meta + 3 + 10 content
            Assert.That(package.Length, Is.EqualTo(12 + (int)metaLength + 13));
        });
    }

    /// <summary>Tests that stream and byte-array builds produce identical output.</summary>
    [Test]
    public void BuildStreamMatchesBuildBytes()
    {
        PackageBuilder<PackageMeta> Build()
        {
            PackageBuilder<PackageMeta> builder = new()
            {
                EntryAlignment = 16,
            };
            builder.AddOrUpdateFile("x", Encoding.UTF8.GetBytes("hello world!!"));
            builder.AddOrUpdateFile("y", new byte[] { 9, 8, 7 });
            return builder;
        }

        byte[] fromBytes = Build().Build();
        using MemoryStream stream = new();
        Build().Build(stream);

        Assert.That(stream.ToArray(), Is.EqualTo(fromBytes), "stream output must be byte-identical to array output");
    }

    /// <summary>Tests that invalid alignment values are rejected.</summary>
    [Test]
    public void InvalidAlignmentIsRejected()
    {
        PackageBuilder<PackageMeta> builder = new();
        Assert.Multiple(() =>
        {
            Assert.That(() => builder.EntryAlignment = 0, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => builder.EntryAlignment = 3, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => builder.EntryAlignment = 24, Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }
}

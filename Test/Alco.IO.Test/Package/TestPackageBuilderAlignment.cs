using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using Alco.IO;

namespace Alco.IO.Test;

public class TestPackageBuilderAlignment
{
    private static byte[] BuildPackage(int alignment)
    {
        PackageBuilder<PackageMeta> builder = new()
        {
            EntryAlignment = alignment,
        };
        builder.AddOrUpdateFile("a", new byte[] { 1, 2, 3 });
        builder.AddOrUpdateFile("b", new byte[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 });
        return builder.Build();
    }

    [Test]
    public void AlignmentMovesEntryStarts()
    {
        const int alignment = 16;
        byte[] package = BuildPackage(alignment);

        using PackageReader<PackageMeta> reader = PackageReader<PackageMeta>.OpenMemory(package);
        Assert.Multiple(() =>
        {
            Assert.That(reader.TryGetEntry("a", out PackageEntry? entryA), Is.True);
            Assert.That(entryA!.Start % alignment, Is.EqualTo(0), "first entry must start aligned");
            Assert.That(reader.TryGetEntry("b", out PackageEntry? entryB), Is.True);
            Assert.That(entryB!.Start % alignment, Is.EqualTo(0), "second entry must start aligned");

            byte[] buffer = new byte[entryB.Size];
            reader.ReadByEntry(entryB, buffer);
            Assert.That(buffer, Is.EqualTo(new byte[] { 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }), "entry content must round-trip");
        });
    }

    [Test]
    public void DefaultAlignmentPacksBackToBack()
    {
        byte[] package = BuildPackage(1);

        using PackageReader<PackageMeta> reader = PackageReader<PackageMeta>.OpenMemory(package);
        Assert.Multiple(() =>
        {
            Assert.That(reader.TryGetEntry("a", out PackageEntry? entryA), Is.True);
            Assert.That(entryA!.Start, Is.EqualTo(0));
            Assert.That(reader.TryGetEntry("b", out PackageEntry? entryB), Is.True);
            Assert.That(entryB!.Start, Is.EqualTo(3), "no padding with alignment 1");
        });

        // Layout: [64B header][3 + 10 content][tail directory referenced by descriptor A].
        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(package.AsSpan(0, 4)), Is.EqualTo("alco"));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(4, 4)), Is.EqualTo(PackageFormat.Version));

            long directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(PackageFormat.DescriptorAOffset, 8));
            uint directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(PackageFormat.DescriptorAOffset + 8, 4));
            uint sequenceA = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(PackageFormat.DescriptorAOffset + 20, 4));
            uint sequenceB = BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(PackageFormat.DescriptorBOffset + 20, 4));

            Assert.That(directoryOffset, Is.EqualTo(PackageFormat.HeaderSize + 13), "directory must start right after the 13 content bytes");
            Assert.That(package.Length, Is.EqualTo((int)(directoryOffset + directoryLength)), "package ends with the directory");
            Assert.That(sequenceA, Is.EqualTo(PackageFormat.InitialSequence));
            Assert.That(sequenceB, Is.EqualTo(0), "sealed packages leave descriptor B unused");
        });
    }

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

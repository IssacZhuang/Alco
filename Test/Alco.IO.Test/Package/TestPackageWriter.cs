using System.Buffers.Binary;
using System.Text;
using NUnit.Framework;
using Alco;
using Alco.IO;

namespace Alco.IO.Test;

/// <summary>
/// Test meta carrying a mutable bound field, exercising generic magic dispatch and
/// <see cref="PackageWriter{TMeta}.CommitMeta"/> round-trips.
/// </summary>
public sealed class TestPackageWriterMeta : PackageMetaBase, IPackageMeta
{
    private static readonly byte[] s_magic = "tstm"u8.ToArray();

    private int _revision;

    public static ReadOnlySpan<byte> Magic => s_magic;

    public int Revision { get => _revision; set => _revision = value; }

    public override void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        base.OnSerialize(node, mode);
        node.BindValue(nameof(_revision), ref _revision);
    }
}

/// <summary>
/// Unit tests for the incremental package writer: append-only commits, descriptor fallback,
/// compaction, and entry checksum verification.
/// </summary>
public sealed class TestPackageWriter
{
    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"alco_pkg_{Guid.NewGuid():N}.pkg");
    }

    private static byte[] ReadEntry(PackageReader<TestPackageWriterMeta> reader, string name)
    {
        Assert.That(reader.TryGetEntry(name, out PackageEntry? entry), Is.True, $"entry '{name}' must exist");
        byte[] buffer = new byte[entry!.Size];
        reader.ReadByEntry(entry, buffer);
        return buffer;
    }

    [Test]
    public void CreateAndReadBack()
    {
        string path = CreateTempPath();
        try
        {
            byte[] a = Encoding.ASCII.GetBytes("AAA");
            byte[] b = new byte[] { 1, 2, 3 };
            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path, new TestPackageWriterMeta { Revision = 7 }))
            {
                Assert.That(writer.GarbageBytes, Is.EqualTo(0), "a fresh package contains only the initial directory");

                writer.UpdateFile("a", a);
                writer.UpdateFile("b", b);
            }

            using PackageReader<TestPackageWriterMeta> reader = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(reader.AllFileNames, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(ReadEntry(reader, "a"), Is.EqualTo(a));
            Assert.That(ReadEntry(reader, "b"), Is.EqualTo(b));
            Assert.That(reader.Meta.Revision, Is.EqualTo(7));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void IncrementalUpdateReplacesEntryAndTracksGarbage()
    {
        string path = CreateTempPath();
        try
        {
            byte[] v1 = new byte[100];
            byte[] v2 = Encoding.ASCII.GetBytes("shorter replacement");
            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path))
            {
                Assert.That(writer.GarbageBytes, Is.EqualTo(0));

                writer.UpdateFile("a", v1);
                writer.UpdateFile("b", new byte[] { 9 });

                writer.UpdateFile("a", v2);
                Assert.That(writer.GarbageBytes, Is.GreaterThanOrEqualTo(v1.Length), "replaced entry bytes must count as garbage");
            }

            using PackageReader<TestPackageWriterMeta> reader = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(reader.TryGetEntry("a", out PackageEntry? entry), Is.True);
            Assert.That(entry!.Size, Is.EqualTo(v2.Length));
            Assert.That(ReadEntry(reader, "a"), Is.EqualTo(v2));
            Assert.That(ReadEntry(reader, "b"), Is.EqualTo(new byte[] { 9 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void RemoveFileRemovesEntryOnly()
    {
        string path = CreateTempPath();
        try
        {
            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path))
            {
                writer.UpdateFile("a", new byte[] { 1 });
                writer.UpdateFile("b", new byte[] { 2 });
                writer.RemoveFile("a");
                Assert.That(writer.RemoveFile("missing"), Is.False);
            }

            using PackageReader<TestPackageWriterMeta> reader = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(reader.TryGetEntry("a", out _), Is.False);
            Assert.That(ReadEntry(reader, "b"), Is.EqualTo(new byte[] { 2 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void CommitMetaPersistsMetaFieldChanges()
    {
        string path = CreateTempPath();
        try
        {
            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path, new TestPackageWriterMeta { Revision = 3 }))
            {
                writer.UpdateFile("a", new byte[] { 5 });
            }

            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.OpenFile(path))
            {
                writer.Meta.Revision = 9;
                writer.CommitMeta();
            }

            using PackageReader<TestPackageWriterMeta> reader = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(reader.Meta.Revision, Is.EqualTo(9));
            Assert.That(ReadEntry(reader, "a"), Is.EqualTo(new byte[] { 5 }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void CorruptedActiveDescriptorFallsBackToPreviousCommit()
    {
        string path = CreateTempPath();
        try
        {
            byte[] v1 = Encoding.ASCII.GetBytes("version one");
            byte[] v2 = Encoding.ASCII.GetBytes("version two - longer");
            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path))
            {
                writer.UpdateFile("a", v1); // commit 1 -> descriptor A
            }

            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.OpenFile(path))
            {
                writer.UpdateFile("a", v2); // commit 2 -> descriptor B (active)
            }

            // Simulate a torn descriptor patch: corrupt the ACTIVE descriptor's hash in place.
            // The active slot is whichever carries the higher sequence (it alternates per commit:
            // Create -> A, then B, A, B, ... so with three commits here it is A again).
            byte[] file = File.ReadAllBytes(path);
            uint sequenceA = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(PackageFormat.DescriptorAOffset + 20, 4));
            uint sequenceB = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(PackageFormat.DescriptorBOffset + 20, 4));
            int activeOffset = sequenceA >= sequenceB ? PackageFormat.DescriptorAOffset : PackageFormat.DescriptorBOffset;
            file[activeOffset + 12] ^= 0xFF; // [offset 8][length 4][hash 8]
            File.WriteAllBytes(path, file);

            using PackageReader<TestPackageWriterMeta> reader = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(reader.TryGetEntry("a", out PackageEntry? entry), Is.True);
            Assert.That(entry!.Size, Is.EqualTo(v1.Length), "reader must fall back to the previous directory");
            Assert.That(ReadEntry(reader, "a"), Is.EqualTo(v1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReaderOpenedDuringWriterSessionSeesCommittedState()
    {
        string path = CreateTempPath();
        try
        {
            using PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path);
            writer.UpdateFile("a", Encoding.ASCII.GetBytes("one"));

            // A reader can open while the writer holds the file (FileShare.Read | FileShare.Write).
            using PackageReader<TestPackageWriterMeta> concurrent = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(ReadEntry(concurrent, "a"), Is.EqualTo(Encoding.ASCII.GetBytes("one")));

            writer.UpdateFile("b", Encoding.ASCII.GetBytes("two"));

            // A reader opened after the commit observes the new entry.
            using PackageReader<TestPackageWriterMeta> fresh = PackageReader<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(ReadEntry(fresh, "a"), Is.EqualTo(Encoding.ASCII.GetBytes("one")));
            Assert.That(ReadEntry(fresh, "b"), Is.EqualTo(Encoding.ASCII.GetBytes("two")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void CompactFileRemovesGarbageAndKeepsContent()
    {
        string path = CreateTempPath();
        try
        {
            byte[] latest = Encoding.ASCII.GetBytes("latest contents of a");
            using (PackageWriter<TestPackageWriterMeta> writer = PackageWriter<TestPackageWriterMeta>.Create(path, new TestPackageWriterMeta { Revision = 4 }))
            {
                writer.UpdateFile("a", new byte[256]);
                writer.UpdateFile("a", new byte[128]);
                writer.UpdateFile("a", latest);
                writer.UpdateFile("b", new byte[] { 7, 7, 7 });
                writer.RemoveFile("b");
                Assert.That(writer.GarbageBytes, Is.GreaterThan(0));
            }

            long sizeBefore = new FileInfo(path).Length;
            PackageWriter<TestPackageWriterMeta>.CompactFile(path);
            long sizeAfter = new FileInfo(path).Length;
            Assert.That(sizeAfter, Is.LessThan(sizeBefore), "compaction must reclaim garbage");

            using (PackageReader<TestPackageWriterMeta> reader = PackageReader<TestPackageWriterMeta>.OpenFile(path))
            {
                Assert.That(ReadEntry(reader, "a"), Is.EqualTo(latest));
                Assert.That(reader.TryGetEntry("b", out _), Is.False);
                Assert.That(reader.Meta.Revision, Is.EqualTo(4), "meta fields must survive compaction");
            }

            using PackageWriter<TestPackageWriterMeta> reopened = PackageWriter<TestPackageWriterMeta>.OpenFile(path);
            Assert.That(reopened.GarbageBytes, Is.EqualTo(0), "compacted layout has no garbage");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void PreV1LayoutIsRejected()
    {
        // Old layout: [magic][Int64 meta length][...]. The Int64 length is now read as
        // version+descriptor fields, so the version check rejects it cleanly.
        byte[] oldLayout = new byte[64];
        "tstm"u8.CopyTo(oldLayout.AsSpan(0, 4));
        BinaryPrimitives.WriteInt64LittleEndian(oldLayout.AsSpan(4, 8), 50);

        Assert.Throws<InvalidDataException>(() => PackageReader<TestPackageWriterMeta>.OpenMemory(oldLayout));
    }

    [Test]
    public void EntryChecksumDetectsTampering()
    {
        byte[] data = new byte[16];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 3);
        }

        PackageBuilder<TestPackageWriterMeta> builder = new();
        builder.AddOrUpdateFile("e", data);
        byte[] package = builder.Build();

        // Tamper with one byte inside the first entry's content (absolute offset HeaderSize).
        package[PackageFormat.HeaderSize + 1] ^= 0xFF;

        using PackageReader<TestPackageWriterMeta> strict = PackageReader<TestPackageWriterMeta>.OpenMemory(package);
        strict.VerifyEntryChecksums = true;
        Assert.That(strict.TryGetEntry("e", out PackageEntry? entry), Is.True);
        Assert.Throws<InvalidDataException>(() => strict.ReadByEntry(entry!, new byte[entry!.Size]));

        // Verification is opt-in: default readers do not pay the hash pass.
        using PackageReader<TestPackageWriterMeta> lax = PackageReader<TestPackageWriterMeta>.OpenMemory(package);
        Assert.That(lax.TryGetEntry("e", out PackageEntry? laxEntry), Is.True);
        byte[] buffer = new byte[laxEntry!.Size];
        Assert.DoesNotThrow(() => lax.ReadByEntry(laxEntry, buffer));
    }

    [Test]
    public void OpenFileMissingThrows()
    {
        Assert.Throws<FileNotFoundException>(() => PackageWriter<TestPackageWriterMeta>.OpenFile(Path.Combine(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.pkg")));
    }
}

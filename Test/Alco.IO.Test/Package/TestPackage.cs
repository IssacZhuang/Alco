using NUnit.Framework;
using System.Text;
using System.Threading.Tasks;
using Alco.IO;

namespace Alco.IO.Test;

/// <summary>
/// Unit tests for package building and reading using in-memory byte arrays.
/// </summary>
public sealed class TestPackage
{
    /// <summary>
    /// Builds a package with a single file and reads it back.
    /// </summary>
    [Test]
    public void BuildAndRead_SingleFile()
    {
        var builder = new PackageBuilder();
        byte[] content = Encoding.UTF8.GetBytes("hello");
        builder.AddOrUpdateFile("a.txt", content);

        byte[] package = builder.Build();

        using var reader = PackageReader.OpenMemory(package);
        Assert.That(reader.TryGetEntry("a.txt", out var entry), Is.True);
        Assert.That(entry!.Size, Is.EqualTo(content.Length));

        byte[] buffer = new byte[content.Length];
        reader.ReadByEntry(entry, buffer);
        Assert.That(buffer, Is.EqualTo(content));
    }

    /// <summary>
    /// Multiple threads reading the same file concurrently should return identical data.
    /// </summary>
    [Test]
    public void ConcurrentRead_SameFile()
    {
        var builder = new PackageBuilder();
        byte[] data = new byte[256 * 1024];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(i & 0xFF);
        builder.AddOrUpdateFile("same.bin", data);

        byte[] package = builder.Build();
        using var reader = PackageReader.OpenMemory(package);
        Assert.That(reader.TryGetEntry("same.bin", out var entry), Is.True);

        Parallel.For(0, 64, _ =>
        {
            var buf = new byte[data.Length];
            reader.ReadByEntry(entry!, buf);
            Assert.That(buf, Is.EqualTo(data));
        });
    }

    /// <summary>
    /// Multiple threads reading different files concurrently should each get the correct content.
    /// </summary>
    [Test]
    public void ConcurrentRead_DifferentFiles()
    {
        var builder = new PackageBuilder();
        var names = new List<string>();
        var original = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < 16; i++)
        {
            string name = $"file_{i}.dat";
            var bytes = new byte[4096 + i * 10];
            for (int j = 0; j < bytes.Length; j++) bytes[j] = (byte)((i + j) & 0xFF);
            builder.AddOrUpdateFile(name, bytes);
            names.Add(name);
            original[name] = bytes;
        }

        byte[] package = builder.Build();
        using var reader = PackageReader.OpenMemory(package);

        // Pre-resolve entries to avoid lookup cost inside the parallel loop
        var entries = new List<PackageEntry>();
        foreach (var n in names)
        {
            Assert.That(reader.TryGetEntry(n, out var e), Is.True);
            entries.Add(e!);
        }

        Parallel.ForEach(entries, e =>
        {
            var buf = new byte[e.Size];
            reader.ReadByEntry(e, buf);
            Assert.That(buf, Is.EqualTo(original[e.Name]));
        });
    }

    /// <summary>
    /// Builds a package with multiple files and verifies contents and independence.
    /// </summary>
    [Test]
    public void BuildAndRead_MultipleFiles()
    {
        var builder = new PackageBuilder();
        byte[] a = Encoding.ASCII.GetBytes("AAA");
        byte[] b = Encoding.ASCII.GetBytes("BBBB");
        byte[] c = Encoding.ASCII.GetBytes("CC");

        builder.AddOrUpdateFile("a.bin", a);
        builder.AddOrUpdateFile("b.bin", b);
        builder.AddOrUpdateFile("c.bin", c);

        byte[] package = builder.Build();

        using var reader = PackageReader.OpenMemory(package);

        Assert.That(reader.TryGetEntry("a.bin", out var ea), Is.True);
        Assert.That(reader.TryGetEntry("b.bin", out var eb), Is.True);
        Assert.That(reader.TryGetEntry("c.bin", out var ec), Is.True);

        var ba = new byte[a.Length];
        var bb = new byte[b.Length];
        var bc = new byte[c.Length];
        reader.ReadByEntry(ea!, ba);
        reader.ReadByEntry(eb!, bb);
        reader.ReadByEntry(ec!, bc);

        Assert.That(ba, Is.EqualTo(a));
        Assert.That(bb, Is.EqualTo(b));
        Assert.That(bc, Is.EqualTo(c));
    }

    /// <summary>
    /// Updating an existing file should replace content and size.
    /// </summary>
    [Test]
    public void UpdateFile_OverridesContentAndSize()
    {
        var builder = new PackageBuilder();
        builder.AddOrUpdateFile("x.dat", new byte[] { 1, 2, 3 });
        builder.AddOrUpdateFile("x.dat", new byte[] { 9, 9 });

        byte[] package = builder.Build();
        using var reader = PackageReader.OpenMemory(package);
        Assert.That(reader.TryGetEntry("x.dat", out var e), Is.True);
        Assert.That(e!.Size, Is.EqualTo(2));

        var buf = new byte[2];
        reader.ReadByEntry(e, buf);
        Assert.That(buf, Is.EqualTo(new byte[] { 9, 9 }));
    }

    /// <summary>
    /// Remove and Clear should affect the built package accordingly.
    /// </summary>
    [Test]
    public void RemoveAndClear_Behavior()
    {
        var builder = new PackageBuilder();
        builder.AddOrUpdateFile("a", new byte[] { 1 });
        builder.AddOrUpdateFile("b", new byte[] { 2 });
        builder.RemoveFile("a");

        byte[] package = builder.Build();
        using var reader = PackageReader.OpenMemory(package);
        Assert.That(reader.TryGetEntry("a", out _), Is.False);
        Assert.That(reader.TryGetEntry("b", out var eb), Is.True);
        var bb = new byte[1];
        reader.ReadByEntry(eb!, bb);
        Assert.That(bb[0], Is.EqualTo(2));

        builder.Clear();
        byte[] emptyPackage = builder.Build();
        using var reader2 = PackageReader.OpenMemory(emptyPackage);
        Assert.That(reader2.TryGetEntry("b", out _), Is.False);
    }

    /// <summary>
    /// Reader should validate buffer size when reading by entry.
    /// </summary>
    [Test]
    public void ReadByEntry_BufferSizeMustMatch()
    {
        var builder = new PackageBuilder();
        builder.AddOrUpdateFile("f", new byte[] { 1, 2, 3, 4 });
        byte[] package = builder.Build();

        using var reader = PackageReader.OpenMemory(package);
        Assert.That(reader.TryGetEntry("f", out var e), Is.True);

        // smaller buffer than entry size
        Assert.Throws<ArgumentException>(() => reader.ReadByEntry(e!, new byte[3]));
    }
}


using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using Alco.IO;

namespace Alco.IO.Test;

/// <summary>
/// Round-trip and failure-mode tests for optional AES-256-GCM package encryption (v2 format).
/// </summary>
public sealed class TestPackageEncryption
{
    private static byte[] NewKey()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    private static byte[] Pattern(int length, int seed)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)((i * 31 + seed) & 0xFF);
        }
        return data;
    }

    /// <summary>
    /// Encrypted multi-chunk entry round-trips through a full read.
    /// </summary>
    [Test]
    public void EncryptedRoundTrip_FullRead_MultiChunk()
    {
        byte[] key = NewKey();
        byte[] data = Pattern(200_000, 7);

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("big.bin", data);
        byte[] package = builder.Build();

        using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);
        Assert.That(reader.TryGetEntry("big.bin", out var entry), Is.True);
        Assert.That(entry!.Size, Is.EqualTo(data.Length), "entry size must stay the plaintext size");

        byte[] buffer = new byte[data.Length];
        reader.ReadByEntry(entry, buffer);
        Assert.That(buffer, Is.EqualTo(data));
    }

    /// <summary>
    /// Random-offset reads spanning chunk boundaries return the exact plaintext slices.
    /// </summary>
    [Test]
    public void EncryptedRangedReads_AcrossChunkBoundaries()
    {
        byte[] key = NewKey();
        byte[] data = Pattern(150_000, 11);

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("big.bin", data);
        byte[] package = builder.Build();

        using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);
        Assert.That(reader.TryGetEntry("big.bin", out var entry), Is.True);

        (long Offset, int Length)[] cases =
        {
            (0, 1), (0, 65536), (1, 100), (65535, 2), (65535, 65536), (65536, 65536),
            (65_540, 3_000), (100_000, 50_000), (149_999, 1),
        };
        byte[] buffer = new byte[65536];
        foreach (var (offset, length) in cases)
        {
            int read = reader.ReadByEntry(entry!, buffer.AsSpan(0, length), offset);
            Assert.That(read, Is.EqualTo(length), $"offset {offset}, length {length}");
            Assert.That(buffer.AsSpan(0, length).ToArray(), Is.EqualTo(data.AsSpan((int)offset, length).ToArray()),
                $"offset {offset}, length {length}");
        }
    }

    /// <summary>
    /// Entries sized exactly at and around the 64 KiB chunk boundary decrypt correctly.
    /// </summary>
    [Test]
    public void EncryptedChunkBoundarySizes()
    {
        byte[] key = NewKey();
        int[] sizes = { 0, 1, 65_535, 65_536, 65_537, 131_072 };
        var original = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        foreach (int size in sizes)
        {
            string name = $"f{size}.bin";
            original[name] = Pattern(size, size);
            builder.AddOrUpdateFile(name, original[name]);
        }

        byte[] package = builder.Build();
        using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);

        foreach (int size in sizes)
        {
            string name = $"f{size}.bin";
            Assert.That(reader.TryGetEntry(name, out var entry), Is.True, name);
            byte[] buffer = new byte[size];
            if (size > 0)
            {
                reader.ReadByEntry(entry!, buffer);
                Assert.That(buffer, Is.EqualTo(original[name]), $"full read {name}");
            }

            if (size > 100)
            {
                byte[] tail = new byte[100];
                int read = reader.ReadByEntry(entry!, tail, size - 100);
                Assert.That(read, Is.EqualTo(100), $"ranged read {name}");
                Assert.That(tail, Is.EqualTo(original[name].AsSpan(size - 100, 100).ToArray()),
                    $"ranged read {name}");
            }
        }
    }

    /// <summary>
    /// Encrypted packages interoperate with entry alignment: starts stay aligned and content
    /// round-trips.
    /// </summary>
    [Test]
    public void EncryptedWithAlignment_KeepsStartsAligned()
    {
        byte[] key = NewKey();
        const int alignment = 256;

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key, EntryAlignment = alignment };
        byte[] a = Pattern(100, 1);
        byte[] b = Pattern(70_000, 2);
        builder.AddOrUpdateFile("a.bin", a);
        builder.AddOrUpdateFile("b.bin", b);

        byte[] package = builder.Build();
        using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);

        Assert.Multiple(() =>
        {
            Assert.That(reader.TryGetEntry("a.bin", out var entryA), Is.True);
            Assert.That(entryA!.Start % alignment, Is.EqualTo(0), "first entry must start aligned");
            Assert.That(reader.TryGetEntry("b.bin", out var entryB), Is.True);
            Assert.That(entryB!.Start % alignment, Is.EqualTo(0), "second entry must start aligned");

            byte[] buffer = new byte[entryB!.Size];
            reader.ReadByEntry(entryB, buffer);
            Assert.That(buffer, Is.EqualTo(b));
        });
    }

    /// <summary>
    /// Encrypted package bytes must not contain entry names or content in plaintext.
    /// </summary>
    [Test]
    public void EncryptedPackage_HidesNamesAndContent()
    {
        byte[] key = NewKey();
        string secretName = "secret/assetname.txt";
        byte[] content = Encoding.UTF8.GetBytes("TOPSECRET-PLAINTEXT-PAYLOAD-" + new string('x', 256));

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile(secretName, content);
        byte[] package = builder.Build();

        Assert.That(package.AsSpan().IndexOf(Encoding.ASCII.GetBytes(secretName)), Is.LessThan(0),
            "entry name must not appear in plaintext");
        Assert.That(package.AsSpan().IndexOf(Encoding.ASCII.GetBytes("TOPSECRET-PLAINTEXT-PAYLOAD")), Is.LessThan(0),
            "entry content must not appear in plaintext");
    }

    /// <summary>
    /// Opening with the wrong key fails with a clear error during meta decryption.
    /// </summary>
    [Test]
    public void WrongKey_ThrowsInvalidData()
    {
        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = NewKey() };
        builder.AddOrUpdateFile("a.txt", Encoding.UTF8.GetBytes("hello"));
        byte[] package = builder.Build();

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => PackageReader<PackageMeta>.OpenMemory(package, NewKey()));
        Assert.That(ex!.Message, Does.Contain("wrong key or corrupted package"));
    }

    /// <summary>
    /// Opening an encrypted package without a key fails with a clear error.
    /// </summary>
    [Test]
    public void MissingKey_ThrowsInvalidData()
    {
        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = NewKey() };
        builder.AddOrUpdateFile("a.txt", Encoding.UTF8.GetBytes("hello"));
        byte[] package = builder.Build();

        InvalidDataException ex = Assert.Throws<InvalidDataException>(
            () => PackageReader<PackageMeta>.OpenMemory(package));
        Assert.That(ex!.Message, Does.Contain("no encryption key was provided"));
    }

    /// <summary>
    /// Flipping a ciphertext byte in the content region must fail authentication on read.
    /// </summary>
    [Test]
    public void TamperedContentChunk_ThrowsOnRead()
    {
        byte[] key = NewKey();
        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("big.bin", Pattern(100_000, 3));
        byte[] package = builder.Build();

        // First entry starts at the content base (v2 header: sign bit + section length).
        long sectionLength = BinaryPrimitives.ReadInt64LittleEndian(package.AsSpan(4, 8)) & long.MaxValue;
        int contentBase = (int)(12 + sectionLength);
        package[contentBase + 5] ^= 0xFF;

        using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);
        Assert.That(reader.TryGetEntry("big.bin", out var entry), Is.True);
        byte[] buffer = new byte[entry!.Size];
        Assert.Throws<InvalidDataException>(() => reader.ReadByEntry(entry, buffer));
    }

    /// <summary>
    /// Flipping a byte inside the encrypted meta must fail at open time.
    /// </summary>
    [Test]
    public void TamperedMeta_ThrowsOnOpen()
    {
        byte[] key = NewKey();
        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("a.txt", Encoding.UTF8.GetBytes("hello"));
        byte[] package = builder.Build();

        package[30] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => PackageReader<PackageMeta>.OpenMemory(package, key));
    }

    /// <summary>
    /// Build(Stream) output for encrypted packages decrypts back to the same content.
    /// </summary>
    [Test]
    public void BuildStream_EncryptedRoundTrip()
    {
        byte[] key = NewKey();
        byte[] data = Pattern(70_000, 5);

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("a.bin", data);
        using var stream = new MemoryStream();
        builder.Build(stream);

        using var reader = PackageReader<PackageMeta>.OpenMemory(stream.ToArray(), key);
        Assert.That(reader.TryGetEntry("a.bin", out var entry), Is.True);
        byte[] buffer = new byte[data.Length];
        reader.ReadByEntry(entry!, buffer);
        Assert.That(buffer, Is.EqualTo(data));
    }

    /// <summary>
    /// Fresh salt per build makes encrypted output non-deterministic while both outputs decrypt
    /// to identical content.
    /// </summary>
    [Test]
    public void EncryptedBuild_IsNonDeterministic()
    {
        byte[] key = NewKey();
        byte[] data = Pattern(1_000, 9);

        byte[] Build()
        {
            var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
            builder.AddOrUpdateFile("a.bin", data);
            return builder.Build();
        }

        byte[] first = Build();
        byte[] second = Build();
        Assert.That(first, Is.Not.EqualTo(second), "random salt should change the output bytes");

        foreach (byte[] package in new[] { first, second })
        {
            using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);
            Assert.That(reader.TryGetEntry("a.bin", out var entry), Is.True);
            byte[] buffer = new byte[data.Length];
            reader.ReadByEntry(entry!, buffer);
            Assert.That(buffer, Is.EqualTo(data));
        }
    }

    /// <summary>
    /// Concurrent full and ranged reads of an encrypted entry are correct.
    /// </summary>
    [Test]
    public void ConcurrentReads_Encrypted()
    {
        byte[] key = NewKey();
        byte[] data = Pattern(200_000, 13);

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("big.bin", data);
        byte[] package = builder.Build();

        using var reader = PackageReader<PackageMeta>.OpenMemory(package, key);
        Assert.That(reader.TryGetEntry("big.bin", out var entry), Is.True);

        Parallel.For(0, 32, i =>
        {
            if (i % 2 == 0)
            {
                var buffer = new byte[data.Length];
                reader.ReadByEntry(entry!, buffer);
                Assert.That(buffer, Is.EqualTo(data));
            }
            else
            {
                long offset = (i * 997) % (data.Length - 1_000);
                var buffer = new byte[1_000];
                int read = reader.ReadByEntry(entry!, buffer, offset);
                Assert.That(read, Is.EqualTo(1_000));
                Assert.That(buffer, Is.EqualTo(data.AsSpan((int)offset, 1_000).ToArray()));
            }
        });
    }

    /// <summary>
    /// A plaintext package still opens when a key is provided (key is ignored for v1).
    /// </summary>
    [Test]
    public void PlainPackage_ReadableWithKeyProvided()
    {
        var builder = new PackageBuilder<PackageMeta>();
        builder.AddOrUpdateFile("a.txt", Encoding.UTF8.GetBytes("hello"));
        byte[] package = builder.Build();

        using var reader = PackageReader<PackageMeta>.OpenMemory(package, NewKey());
        Assert.That(reader.TryGetEntry("a.txt", out var entry), Is.True);
        byte[] buffer = new byte[5];
        reader.ReadByEntry(entry!, buffer);
        Assert.That(buffer, Is.EqualTo(Encoding.UTF8.GetBytes("hello")));
    }

    /// <summary>
    /// PackageFileSource over an encrypted package serves whole-entry data and seekable streams.
    /// </summary>
    [Test]
    public void PackageFileSource_Encrypted_DataAndStream()
    {
        byte[] key = NewKey();
        byte[] data = Pattern(130_000, 17);

        var builder = new PackageBuilder<PackageMeta> { EncryptionKey = key };
        builder.AddOrUpdateFile("data.bin", data);
        byte[] package = builder.Build();

        using var source = new PackageFileSource<PackageMeta>(new MemoryStream(package), "encrypted", key);
        Assert.That(source.AllFileNames, Is.EqualTo(new[] { "data.bin" }));

        Assert.That(source.TryGetData("data.bin", out var handle, out var failureReason), Is.True,
            () => failureReason);
        using (handle!)
        {
            Assert.That(handle.AsSpan().ToArray(), Is.EqualTo(data));
        }

        Assert.That(source.TryGetStream("data.bin", out var stream, out failureReason), Is.True, () => failureReason);
        using (var entryStream = stream!)
        {
            entryStream.Seek(70_000, SeekOrigin.Begin);
            byte[] partial = new byte[500];
            int read = entryStream.Read(partial, 0, partial.Length);
            Assert.That(read, Is.EqualTo(500));
            Assert.That(partial, Is.EqualTo(data.AsSpan(70_000, 500).ToArray()));
        }
    }
}

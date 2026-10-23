using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Alco.IO;

namespace Alco.Benchmark;

/// <summary>
/// Compares package build/open/read performance with and without AES-256-GCM encryption (v2).
/// The payload mix mirrors a game bundle: one 4 MB "texture" blob spanning 64 chunks, one
/// single-chunk 64 KB entry, and 100 small 2 KB "config" entries (~4.3 MB total). Readers are
/// memory-backed so both variants measure pure CPU cost (copy vs decrypt) without disk I/O.
/// </summary>
public class BenchmarkPackage
{
    private const int LargeSize = 4 * 1024 * 1024;
    private const int MediumSize = 64 * 1024;
    private const int SmallSize = 2 * 1024;
    private const int SmallCount = 100;
    private const int RangedOffset = 1_000_000;
    private const int RangedSize = 4 * 1024;

    private byte[] _key = null!;
    private byte[] _plainPackage = null!;
    private byte[] _encryptedPackage = null!;
    private PackageBuilder<PackageMeta> _plainBuilder = null!;
    private PackageBuilder<PackageMeta> _encryptedBuilder = null!;
    private PackageReader<PackageMeta> _plainReader = null!;
    private PackageReader<PackageMeta> _encryptedReader = null!;
    private PackageEntry _plainLarge = null!;
    private PackageEntry _plainSmall = null!;
    private PackageEntry _encryptedLarge = null!;
    private PackageEntry _encryptedSmall = null!;
    private byte[] _largeBuffer = null!;
    private byte[] _smallBuffer = null!;
    private byte[] _rangedBuffer = null!;

    /// <summary>
    /// Builds the plain and encrypted packages and prepares the readers and buffers.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _key = new byte[32];
        for (int i = 0; i < _key.Length; i++)
        {
            _key[i] = (byte)(i * 7 + 1);
        }

        byte[] large = new byte[LargeSize];
        byte[] medium = new byte[MediumSize];
        byte[] small = new byte[SmallSize];
        for (int i = 0; i < large.Length; i++)
        {
            large[i] = (byte)i;
            medium[i % medium.Length] = (byte)(i >> 3);
            small[i % small.Length] = (byte)(i >> 2);
        }

        _plainBuilder = new PackageBuilder<PackageMeta>();
        _encryptedBuilder = new PackageBuilder<PackageMeta> { EncryptionKey = _key };
        foreach (PackageBuilder<PackageMeta> builder in new[] { _plainBuilder, _encryptedBuilder })
        {
            builder.AddOrUpdateFile("Textures/large.dds", large);
            builder.AddOrUpdateFile("Meshes/medium.mesh", medium);
            for (int i = 0; i < SmallCount; i++)
            {
                builder.AddOrUpdateFile($"Configs/file_{i}.jsonc", small);
            }
        }

        _plainPackage = _plainBuilder.Build();
        _encryptedPackage = _encryptedBuilder.Build();
        _plainReader = PackageReader<PackageMeta>.OpenMemory(_plainPackage);
        _encryptedReader = PackageReader<PackageMeta>.OpenMemory(_encryptedPackage, _key);
        _plainLarge = GetRequiredEntry(_plainReader, "Textures/large.dds");
        _plainSmall = GetRequiredEntry(_plainReader, "Configs/file_0.jsonc");
        _encryptedLarge = GetRequiredEntry(_encryptedReader, "Textures/large.dds");
        _encryptedSmall = GetRequiredEntry(_encryptedReader, "Configs/file_0.jsonc");

        _largeBuffer = new byte[LargeSize];
        _smallBuffer = new byte[SmallSize];
        _rangedBuffer = new byte[RangedSize];
    }

    /// <summary>
    /// Benchmarks opening a plain package from memory.
    /// </summary>
    [Benchmark(Description = "Package open (plain)")]
    public void OpenPlain()
    {
        using var reader = PackageReader<PackageMeta>.OpenMemory(_plainPackage);
    }

    /// <summary>
    /// Benchmarks opening an encrypted package from memory.
    /// </summary>
    [Benchmark(Description = "Package open (encrypted)")]
    public void OpenEncrypted()
    {
        using var reader = PackageReader<PackageMeta>.OpenMemory(_encryptedPackage, _key);
    }

    /// <summary>
    /// Benchmarks reading a 4 MB entry from a plain package.
    /// </summary>
    [Benchmark(Description = "Read 4MB entry (plain)")]
    public void ReadLargePlain()
    {
        _plainReader.ReadByEntry(_plainLarge, _largeBuffer);
    }

    /// <summary>
    /// Benchmarks reading a 4 MB entry from an encrypted package.
    /// </summary>
    [Benchmark(Description = "Read 4MB entry (encrypted)")]
    public void ReadLargeEncrypted()
    {
        _encryptedReader.ReadByEntry(_encryptedLarge, _largeBuffer);
    }

    /// <summary>
    /// Benchmarks reading a 2 KB entry from a plain package.
    /// </summary>
    [Benchmark(Description = "Read 2KB entry (plain)")]
    public void ReadSmallPlain()
    {
        _plainReader.ReadByEntry(_plainSmall, _smallBuffer);
    }

    /// <summary>
    /// Benchmarks reading a 2 KB entry from an encrypted package.
    /// </summary>
    [Benchmark(Description = "Read 2KB entry (encrypted)")]
    public void ReadSmallEncrypted()
    {
        _encryptedReader.ReadByEntry(_encryptedSmall, _smallBuffer);
    }

    /// <summary>
    /// Benchmarks reading a 4 KB range from a 4 MB entry in a plain package.
    /// </summary>
    [Benchmark(Description = "Read 4KB range of 4MB entry (plain)")]
    public void ReadRangePlain()
    {
        _plainReader.ReadByEntry(_plainLarge, _rangedBuffer, RangedOffset);
    }

    /// <summary>
    /// Benchmarks reading a 4 KB range from a 4 MB entry in an encrypted package.
    /// </summary>
    [Benchmark(Description = "Read 4KB range of 4MB entry (encrypted)")]
    public void ReadRangeEncrypted()
    {
        _encryptedReader.ReadByEntry(_encryptedLarge, _rangedBuffer, RangedOffset);
    }

    /// <summary>
    /// Benchmarks building a 4.3 MB plain package.
    /// </summary>
    [Benchmark(Description = "Package build 4.3MB (plain)")]
    public byte[] BuildPlain()
    {
        return _plainBuilder.Build();
    }

    /// <summary>
    /// Benchmarks building a 4.3 MB encrypted package.
    /// </summary>
    [Benchmark(Description = "Package build 4.3MB (encrypted)")]
    public byte[] BuildEncrypted()
    {
        return _encryptedBuilder.Build();
    }

    private static PackageEntry GetRequiredEntry(PackageReader<PackageMeta> reader, string name)
    {
        return reader.TryGetEntry(name, out PackageEntry entry)
            ? entry
            : throw new InvalidOperationException($"Entry not found: {name}");
    }
}

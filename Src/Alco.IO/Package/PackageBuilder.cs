using System.Buffers.Binary;
using System.IO;
using System.IO.Hashing;

using Alco;

namespace Alco.IO;

/// <summary>
/// Builds a sealed Alco package in-memory following the documented format:
/// [fixed 64-byte header][content payload][directory payload at the tail]. The meta type
/// <typeparamref name="TMeta"/> supplies the magic number and may carry type-specific fields beyond
/// the inherited entry directory. The produced layout is fully compact (zero garbage, entries in
/// insertion order); it is what <see cref="PackageWriter{TMeta}.CompactFile"/> converges to.
/// </summary>
/// <typeparam name="TMeta">The package metadata type, which must implement <see cref="IPackageMeta"/>.</typeparam>
public sealed class PackageBuilder<TMeta> where TMeta : PackageMetaBase, IPackageMeta, new()
{
    private readonly Dictionary<string, byte[]> _nameToBytes = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>
    /// The metadata to encode into the package. When <see langword="null"/>, <see cref="Build"/>
    /// uses a default-constructed <typeparamref name="TMeta"/> (entry directory only). Set this
    /// to carry type-specific fields (e.g. a save's player name and timestamp).
    /// </summary>
    public TMeta? Meta { get; set; }

    /// <summary>
    /// Content-relative alignment in bytes applied to the start of every entry (each entry is
    /// padded up so the following entry starts aligned). Must be a power of two ≥ 1. Default 1
    /// packs entries back-to-back with no padding. Use 16 or 256 for payloads intended for
    /// direct GPU upload/binding. Readers are unaffected: they use the recorded entry offsets.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when set to a non-power-of-two or &lt; 1.</exception>
    public int EntryAlignment
    {
        get => _entryAlignment;
        set
        {
            if (value < 1 || (value & (value - 1)) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Entry alignment must be a power of two >= 1.");
            }

            _entryAlignment = value;
        }
    }

    private int _entryAlignment = 1;

    /// <summary>
    /// Adds a new entry or updates an existing entry's content.
    /// </summary>
    /// <param name="entryName">Logical entry name (e.g., virtual path)</param>
    /// <param name="data">Entry bytes</param>
    public void AddOrUpdateFile(string entryName, ReadOnlySpan<byte> data)
    {
        if (string.IsNullOrEmpty(entryName))
        {
            throw new ArgumentException("Entry name must not be null or empty.", nameof(entryName));
        }

        byte[] owned = data.ToArray();
        if (_nameToBytes.ContainsKey(entryName))
        {
            _nameToBytes[entryName] = owned;
        }
        else
        {
            _nameToBytes.Add(entryName, owned);
            _order.Add(entryName);
        }
    }

    /// <summary>
    /// Removes an entry by name. No-op if the entry does not exist.
    /// </summary>
    public void RemoveFile(string entryName)
    {
        if (_nameToBytes.Remove(entryName))
        {
            _order.Remove(entryName);
        }
    }

    /// <summary>
    /// Removes all entries.
    /// </summary>
    public void Clear()
    {
        _nameToBytes.Clear();
        _order.Clear();
    }

    /// <summary>
    /// Builds the package bytes:
    /// [fixed header][content payload][directory payload]. Descriptor A points at the tail
    /// directory with the initial sequence; descriptor B is unused. Entries are padded to
    /// <see cref="EntryAlignment"/> (content-relative).
    /// </summary>
    /// <returns>Package bytes</returns>
    public byte[] Build()
    {
        (TMeta meta, ReadOnlyMemory<byte> metaBytes, long totalContentLength) = PrepareBuild();

        long directoryOffset = PackageFormat.HeaderSize + totalContentLength;
        ulong directoryHash = XxHash64.HashToUInt64(metaBytes.Span);

        int finalLength = checked((int)(PackageFormat.HeaderSize + totalContentLength + metaBytes.Length));
        byte[] package = new byte[finalLength];

        PackageDirectoryDescriptor descriptor = new()
        {
            Offset = directoryOffset,
            Length = (uint)metaBytes.Length,
            Hash = directoryHash,
            Sequence = PackageFormat.InitialSequence,
        };
        PackageHeader.Encode(package.AsSpan(0, PackageFormat.HeaderSize), TMeta.Magic, descriptor, PackageDirectoryDescriptor.Unused);

        int cursor = PackageFormat.HeaderSize;
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue;
            }

            Buffer.BlockCopy(bytes, 0, package, cursor, bytes.Length);
            cursor += AlignUp(bytes.Length, _entryAlignment);
        }

        metaBytes.Span.CopyTo(package.AsSpan(checked((int)directoryOffset)));

        return package;
    }

    /// <summary>
    /// Builds the package directly into an output stream following the same layout as
    /// <see cref="Build()"/>: [fixed header][content payload][directory payload], written strictly
    /// sequentially (the header is finalized up-front, so no seek-back is required). Use for
    /// payloads too large to materialize as a single managed array on top of the builder's own
    /// buffers.
    /// </summary>
    /// <param name="output">The output stream; written from its current position.</param>
    public void Build(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        (TMeta meta, ReadOnlyMemory<byte> metaBytes, long totalContentLength) = PrepareBuild();

        PackageDirectoryDescriptor descriptor = new()
        {
            Offset = PackageFormat.HeaderSize + totalContentLength,
            Length = (uint)metaBytes.Length,
            Hash = XxHash64.HashToUInt64(metaBytes.Span),
            Sequence = PackageFormat.InitialSequence,
        };
        Span<byte> header = stackalloc byte[PackageFormat.HeaderSize];
        PackageHeader.Encode(header, TMeta.Magic, descriptor, PackageDirectoryDescriptor.Unused);
        output.Write(header);

        byte[] padding = new byte[Math.Max(0, _entryAlignment - 1)];
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue;
            }

            output.Write(bytes);

            int pad = AlignUp(bytes.Length, _entryAlignment) - bytes.Length;
            if (pad > 0)
            {
                output.Write(padding.AsSpan(0, pad));
            }
        }

        output.Write(metaBytes.Span);
    }

    /// <summary>
    /// Compute entry offsets (content-relative, honoring <see cref="EntryAlignment"/>), content
    /// checksums, and the encoded meta payload. Shared by both Build overloads.
    /// </summary>
    private (TMeta Meta, ReadOnlyMemory<byte> MetaBytes, long TotalContentLength) PrepareBuild()
    {
        TMeta meta = Meta ?? new TMeta();
        meta.ClearEntries();

        long runningOffset = 0;
        long totalContentLength = 0;
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue; // Should not happen, but tolerate
            }

            int size = bytes.Length;
            meta.AddEntry(name, runningOffset, size, XxHash64.HashToUInt64(bytes));
            int paddedSize = AlignUp(size, _entryAlignment);
            runningOffset += paddedSize;
            totalContentLength += paddedSize;
        }

        if (totalContentLength > int.MaxValue)
        {
            throw new InvalidOperationException("Content payload too large.");
        }

        ReadOnlyMemory<byte> metaBytes = BinaryParser.Encode(meta);
        if (metaBytes.Length > int.MaxValue)
        {
            throw new InvalidOperationException("Meta payload too large.");
        }

        return (meta, metaBytes, totalContentLength);
    }

    private int AlignUp(int value, int alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Packs every file under <paramref name="directory"/> (recursively, entry names relative to
    /// the directory with <c>/</c> separators) into a package at <paramref name="packagePath"/>.
    /// </summary>
    public static void PackDirectory(string directory, string packagePath)
    {
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("Directory must not be null or empty.", nameof(directory));
        }
        if (string.IsNullOrEmpty(packagePath))
        {
            throw new ArgumentException("Package path must not be null or empty.", nameof(packagePath));
        }
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {directory}");
        }

        // Gather files deterministically
        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        var builder = new PackageBuilder<TMeta>();
        foreach (string file in files)
        {
            // Compute entry name relative to root and normalize separators to '/'
            string relative = Path.GetRelativePath(directory, file);
            relative = relative.Replace('\\', '/');

            byte[] data = File.ReadAllBytes(file);
            builder.AddOrUpdateFile(relative, data);
        }

        byte[] package = builder.Build();

        string? outDir = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }
        File.WriteAllBytes(packagePath, package);
    }
}

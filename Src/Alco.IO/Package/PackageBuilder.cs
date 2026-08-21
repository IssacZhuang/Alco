using System.Buffers.Binary;
using System.IO;

using Alco;

namespace Alco.IO;

/// <summary>
/// Builds an Alco package in-memory following the documented format:
/// [magic][Int64 LE meta length][meta payload via BinaryParser][content payload].
/// The meta type <typeparamref name="TMeta"/> supplies the magic number and may carry type-specific
/// fields beyond the inherited entry directory.
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
    /// [<see cref="IPackageMeta.Magic"/>][meta length (Int64 LE)][meta payload][content payload].
    /// Entries are padded to <see cref="EntryAlignment"/> (content-relative).
    /// </summary>
    /// <returns>Package bytes</returns>
    public byte[] Build()
    {
        (TMeta meta, ReadOnlyMemory<byte> metaBytes, long totalContentLength) = PrepareBuild();

        int finalLength = checked((int)(12L + metaBytes.Length + totalContentLength));
        byte[] package = new byte[finalLength];

        // Write magic number
        TMeta.Magic.CopyTo(package.AsSpan(0, 4));

        // Write meta length (Int64 LE)
        BinaryPrimitives.WriteInt64LittleEndian(package.AsSpan(4, 8), metaBytes.Length);

        // Write meta payload
        metaBytes.Span.CopyTo(package.AsSpan(12));

        // Write content payload
        int cursor = 12 + metaBytes.Length;
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue;
            }

            Buffer.BlockCopy(bytes, 0, package, cursor, bytes.Length);
            cursor += AlignUp(bytes.Length, _entryAlignment);
        }

        return package;
    }

    /// <summary>
    /// Builds the package directly into an output stream following the same layout as
    /// <see cref="Build()"/>: [<see cref="IPackageMeta.Magic"/>][meta length (Int64 LE)]
    /// [meta payload][content payload]. Use for payloads too large to materialize as a single
    /// managed array on top of the builder's own buffers.
    /// </summary>
    /// <param name="output">The output stream; written from its current position.</param>
    public void Build(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);

        (TMeta meta, ReadOnlyMemory<byte> metaBytes, long _) = PrepareBuild();

        // Write magic number
        Span<byte> header = stackalloc byte[12];
        TMeta.Magic.CopyTo(header[..4]);
        BinaryPrimitives.WriteInt64LittleEndian(header.Slice(4, 8), metaBytes.Length);
        output.Write(header);

        // Write meta payload
        output.Write(metaBytes.Span);

        // Write content payload
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
    }

    /// <summary>
    /// Compute entry offsets (content-relative, honoring <see cref="EntryAlignment"/>) and
    /// encode the meta payload. Shared by both Build overloads.
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
            meta.AddEntry(name, runningOffset, size);
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

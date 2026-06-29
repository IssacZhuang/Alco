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
    /// uses a default-constructed <typeparamref name="TMeta"/> (entry directory only). Set this to
    /// carry type-specific fields (e.g. a save's player name and timestamp).
    /// </summary>
    public TMeta? Meta { get; set; }

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
    /// </summary>
    /// <returns>Package bytes</returns>
    public byte[] Build()
    {
        // Build meta with running offsets relative to the start of the content section
        long runningOffset = 0;
        TMeta meta = Meta ?? new TMeta();

        long totalContentLength = 0;
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue; // Should not happen, but tolerate
            }
            long size = bytes.LongLength;
            meta.AddEntry(name, runningOffset, size);
            runningOffset += size;
            totalContentLength += size;
        }

        if (totalContentLength > int.MaxValue)
        {
            throw new InvalidOperationException("Content payload too large.");
        }

        ReadOnlyMemory<byte> metaBytes = BinaryParser.Encode(meta);
        int metaLength = metaBytes.Length;
        if (metaLength > int.MaxValue)
        {
            throw new InvalidOperationException("Meta payload too large.");
        }

        int finalLength = checked((int)(12L + metaLength + totalContentLength));
        byte[] package = new byte[finalLength];

        // Write magic number
        TMeta.Magic.CopyTo(package.AsSpan(0, 4));

        // Write meta length (Int64 LE)
        BinaryPrimitives.WriteInt64LittleEndian(package.AsSpan(4, 8), metaLength);

        // Write meta payload
        metaBytes.Span.CopyTo(package.AsSpan(12));

        // Write content payload
        int contentBase = 12 + (int)metaLength;
        int cursor = contentBase;
        foreach (string name in _order)
        {
            if (!_nameToBytes.TryGetValue(name, out byte[]? bytes))
            {
                continue;
            }
            Buffer.BlockCopy(bytes, 0, package, cursor, bytes.Length);
            cursor += bytes.Length;
        }

        return package;
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

using System.Buffers.Binary;

using Alco;

namespace Alco.IO;

/// <summary>
/// Builds an Alco package in-memory following the documented format:
/// [Int64 LE meta length][meta payload via BinaryParser][content payload].
/// </summary>
public sealed class PackageBuilder
{
    private readonly Dictionary<string, byte[]> _nameToBytes = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

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
    /// Builds the package bytes: [meta length (Int64 LE)][meta payload][content payload].
    /// </summary>
    /// <returns>Package bytes</returns>
    public byte[] Build()
    {
        // Build meta with running offsets relative to the start of the content section
        long runningOffset = 0;
        PackageMeta meta = new PackageMeta();

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
            throw new InvalidOperationException("Content payload too large to fit into a single byte array.");
        }

        byte[] metaBytes = BinaryParser.Encode(meta);
        long metaLength = metaBytes.LongLength;
        if (metaLength > int.MaxValue)
        {
            throw new InvalidOperationException("Meta payload too large to fit into a single byte array.");
        }

        int finalLength = checked((int)(8L + metaLength + totalContentLength));
        byte[] package = new byte[finalLength];

        // Write meta length (Int64 LE)
        BinaryPrimitives.WriteInt64LittleEndian(package.AsSpan(0, 8), metaLength);

        // Write meta payload
        metaBytes.CopyTo(package, 8);

        // Write content payload
        int contentBase = 8 + (int)metaLength;
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
}



using Alco;

namespace Alco.IO;

public sealed class PackageEntry : ISerializable
{
    private string _name = string.Empty;
    private long _start;
    private long _size;
    private ulong _checksum;

    public string Name => _name;
    public long Start => _start;
    public long Size => _size;

    /// <summary>Gets the XxHash64 checksum of the entry's content bytes.</summary>
    public ulong Checksum => _checksum;

    /// <summary>
    /// Serialization-only parameterless constructor, filled by the serializer when loading a
    /// package entry. Do not call manually.
    /// </summary>
    public PackageEntry(){

    }

    public PackageEntry(string name, long start, long size, ulong checksum)
    {
        _name = name;
        _start = start;
        _size = size;
        _checksum = checksum;
    }

    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindValue(nameof(_start), ref _start);
        node.BindValue(nameof(_size), ref _size);
        node.BindValue(nameof(_checksum), ref _checksum);
    }
}

/// <summary>
/// Abstract base for package metadata. Owns the entry directory (a ZIP-like central directory of
/// named entries with content-relative offsets and content checksums) and serializes it. The
/// directory is stored at the tail of the package file (see <see cref="PackageFormat"/>) and is
/// rewritten wholesale on every commit. Concrete package meta types (e.g. <see cref="PackageMeta"/>,
/// save metas) derive from this, declare <see cref="IPackageMeta"/> directly, and add their own
/// fields and magic number.
/// </summary>
/// <remarks>
/// This base deliberately implements <see cref="ISerializable"/> but <b>not</b>
/// <see cref="IPackageMeta"/>, so each concrete meta's static magic slot is the one generic
/// dispatch resolves to. See the remarks on <see cref="IPackageMeta"/>.
/// </remarks>
public abstract class PackageMetaBase : ISerializable
{
    private string _name = string.Empty;
    private string _version = "1.0";
    private readonly List<PackageEntry> _entries = new();

    /// <summary>Gets the package name (free-form label).</summary>
    public string Name
    {
        get => _name;
        init => _name = value;
    }

    /// <summary>Gets the format version string.</summary>
    public string Version
    {
        get => _version;
        init => _version = value;
    }

    /// <summary>Gets the directory of named entries with content-relative offsets.</summary>
    public IReadOnlyList<PackageEntry> Entries => _entries;

    /// <summary>
    /// Serializes the package name, version, and entry directory. Derived metas override this,
    /// call <see langword="base"/>.<see cref="OnSerialize"/> first, then bind their own fields.
    /// </summary>
    public virtual void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindString(nameof(_version), ref _version);
        node.BindCollectionSerializable(nameof(_entries), _entries);
    }

    /// <summary>Appends an entry descriptor to the directory.</summary>
    public void AddEntry(string name, long start, long size, ulong checksum)
    {
        _entries.Add(new PackageEntry(name, start, size, checksum));
    }

    /// <summary>
    /// Adds an entry descriptor, replacing any existing entry with the same name in place
    /// (keeping its directory position).
    /// </summary>
    /// <param name="name">Entry name.</param>
    /// <param name="start">Content-relative start offset.</param>
    /// <param name="size">Entry length in bytes.</param>
    /// <param name="checksum">XxHash64 checksum of the entry content.</param>
    public void AddOrUpdateEntry(string name, long start, long size, ulong checksum)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.Ordinal))
            {
                _entries[i] = new PackageEntry(name, start, size, checksum);
                return;
            }
        }

        _entries.Add(new PackageEntry(name, start, size, checksum));
    }

    /// <summary>
    /// Removes an entry descriptor by name.
    /// </summary>
    /// <param name="name">Entry name.</param>
    /// <returns>True when an entry was removed; false when no such entry exists.</returns>
    public bool RemoveEntry(string name)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, name, StringComparison.Ordinal))
            {
                _entries.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>Clears all entry descriptors.</summary>
    public void ClearEntries()
    {
        _entries.Clear();
    }
}

/// <summary>
/// Concrete package metadata for Alco asset bundles (magic <c>"alco"</c>). Owns no fields beyond
/// the inherited entry directory and version; the magic identifies general-purpose asset packages.
/// </summary>
public sealed class PackageMeta : PackageMetaBase, IPackageMeta
{
    private static readonly byte[] s_magic = "alco"u8.ToArray();

    /// <summary>Gets the 4-byte magic that identifies Alco asset bundle packages.</summary>
    public static ReadOnlySpan<byte> Magic => s_magic;
}

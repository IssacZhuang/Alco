
using Alco;

namespace Alco.IO;

public sealed class PackageEntry : ISerializable
{
    private string _name = string.Empty;
    private long _start;
    private long _size;

    public string Name => _name;
    public long Start => _start;
    public long Size => _size;

    //empty constructor for serialization
    public PackageEntry(){

    }

    public PackageEntry(string name, long start, long size)
    {
        _name = name;
        _start = start;
        _size = size;
    }

    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindValue(nameof(_start), ref _start);
        node.BindValue(nameof(_size), ref _size);
    }
}

/// <summary>
/// Abstract base for package metadata. Owns the entry directory (a ZIP-like central directory of
/// named entries with content-relative offsets) and serializes it. Concrete package meta types
/// (e.g. <see cref="PackageMeta"/>, save metas) derive from this, declare <see cref="IPackageMeta"/>
/// directly, and add their own fields and magic number.
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
    public void AddEntry(string name, long start, long size)
    {
        _entries.Add(new PackageEntry(name, start, size));
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

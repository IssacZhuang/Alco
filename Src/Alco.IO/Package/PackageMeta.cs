
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

public sealed class PackageMeta : ISerializable
{
    private string _name = string.Empty;
    private string _version = "1.0";
    private readonly List<PackageEntry> _entries = new();

    public string Version => _version;
    public IReadOnlyList<PackageEntry> Entries => _entries;


    public void OnSerialize(SerializeNode node, SerializeMode mode)
    {
        node.BindString(nameof(_name), ref _name);
        node.BindString(nameof(_version), ref _version);
        node.BindCollectionSerializable(nameof(_entries), _entries);
    }

    public void AddEntry(string name, long start, long size)
    {
        _entries.Add(new PackageEntry(name, start, size));
    }

    public void ClearEntries()
    {
        _entries.Clear();
    }
}
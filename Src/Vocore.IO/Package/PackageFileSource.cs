using System.Diagnostics.CodeAnalysis;

namespace Vocore.IO;

/// <summary>
/// A file source that reads from a package file.
/// </summary>
public class PackageFileSource : IFileSource
{
    public int Order => 0;
    private readonly Package _package;
    private readonly List<PackageEntry> _entries;
    private readonly Dictionary<string, PackageEntry> _entryLookup = new Dictionary<string, PackageEntry>();

    public PackageFileSource(string packagePath)
    {
        _entries = new List<PackageEntry>();

        _package = new Package();
        _package.Read(packagePath);
        foreach (var entry in _package.AllEntries)
        {
            _entries.Add(entry);
            _entryLookup[entry.FileName] = entry;
        }
    }

    public IEnumerable<string> AllFileNames
    {
        get
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                yield return _entries[i].FileName;
            }
        }
    }

    public void Dispose()
    {
        _package.Dispose();
    }

    public bool TryGetData(string path, [NotNullWhen(true)] out ReadOnlySpan<byte> data)
    {
        if (_entryLookup.TryGetValue(path, out var entry))
        {
            _package.ReadEntry(entry, out byte[] fileData);
            data = fileData;
            return true;
        }

        data = ReadOnlySpan<byte>.Empty;
        return false;
    }
}
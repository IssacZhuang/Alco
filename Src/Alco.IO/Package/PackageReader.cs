using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;


namespace Alco.IO;

public unsafe sealed class PackageReader : AutoDisposable
{

    //only one of the two will be used
    private readonly SafeFileHandle? _file;
    private readonly SafeMemoryHandle? _memory;
    private readonly long _length;

    private readonly FrozenDictionary<string, PackageEntry> _entries;

    internal PackageReader(string path)
    {
        //open with read
        _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        _length = RandomAccess.GetLength(_file);
        _entries = ReadEntries();
    }

    internal PackageReader(byte[] data)
    {
        _memory = new SafeMemoryHandle(data);
        _length = data.Length;
        _entries = ReadEntries();
    }

    internal PackageReader(byte* data, int size)
    {
        _memory = new SafeMemoryHandle(data, size);
        _length = size;
        _entries = ReadEntries();
    }

    public bool TryGetEntry(string name, [NotNullWhen(true)] out PackageEntry? entry)
    {
        return _entries.TryGetValue(name, out entry);
    }

    public void ReadByEntry(PackageEntry entry, Span<byte> buffer)
    {
        CheckLength(entry.Start, (int)entry.Size);
        Read(buffer, (int)entry.Start);
    }

    private int Read(Span<byte> buffer, int offset)
    {
        CheckLength(offset, buffer.Length);
        if (_file != null)
        {
            return RandomAccess.Read(_file, buffer, offset);
        }
        else if (_memory != null)
        {
            Span<byte> memory = _memory.AsSpan();
            int lengthToRead = (int)Math.Min(_length, (long)memory.Length - offset);
            memory.Slice(offset, lengthToRead).CopyTo(buffer);
            return lengthToRead;
        }
        else
        {
            throw new InvalidOperationException("No file or memory handle is available");
        }
    }

    private int ReadUnsafe(byte* buffer, int offset, int size)
    {
        CheckLength(offset, size);
        if (_file != null)
        {
            return RandomAccess.Read(_file, new Span<byte>(buffer, size), offset);
        }
        else if (_memory != null)
        {
            Span<byte> memory = _memory.AsSpan();
            memory.Slice(offset, size).CopyTo(new Span<byte>(buffer, size));
            return size;
        }
        else
        {
            throw new InvalidOperationException("No file or memory handle is available");
        }
    }

    private T ReadValue<T>(int offset) where T : unmanaged
    {
        byte* ptr = stackalloc byte[sizeof(T)];
        ReadUnsafe(ptr, offset, sizeof(T));
        T value = *(T*)ptr;
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckLength(long offset, int size)
    {
        if (offset + size > _length)
        {
            throw new IndexOutOfRangeException($"Offset and size exceed the package length. Offset: {offset}, Size: {size}, Length: {_length}");
        }
    }

    private FrozenDictionary<string, PackageEntry> ReadEntries()
    {
        long metaLength = ReadValue<long>(0);
        byte[] meta = new byte[metaLength];
        Read(meta, 8);
        PackageMeta packageMeta = BinaryParser.Decode<PackageMeta>(meta);
        return packageMeta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
    }

    protected override void Dispose(bool disposing)
    {
        _file?.Dispose();
    }

    public static PackageReader OpenFile(string path)
    {
        return new PackageReader(path);
    }

    public static PackageReader OpenMemory(byte[] data)
    {
        return new PackageReader(data);
    }

    public static PackageReader OpenUnsafeMemory(byte* data, int size)
    {
        return new PackageReader(data, size);
    }
}
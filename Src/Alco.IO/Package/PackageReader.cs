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

    // Base offset of the content section: 8 + MetaLength
    private readonly long _contentBase;

    private readonly FrozenDictionary<string, PackageEntry> _entries;

    /// <summary>
    /// Opens a package reader from a file path.
    /// </summary>
    /// <param name="path">Package file path</param>
    internal PackageReader(string path)
    {
        //open with read
        _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        _length = RandomAccess.GetLength(_file);
        _entries = ReadEntries(out _contentBase);
    }

    /// <summary>
    /// Opens a package reader over a managed byte array.
    /// </summary>
    /// <param name="data">Package bytes</param>
    internal PackageReader(byte[] data)
    {
        _memory = new SafeMemoryHandle(data);
        _length = data.Length;
        _entries = ReadEntries(out _contentBase);
    }

    /// <summary>
    /// Opens a package reader over an unmanaged memory region.
    /// </summary>
    /// <param name="data">Pointer to package bytes</param>
    /// <param name="size">Size of the package in bytes</param>
    internal PackageReader(byte* data, int size)
    {
        _memory = new SafeMemoryHandle(data, size);
        _length = size;
        _entries = ReadEntries(out _contentBase);
    }

    /// <summary>
    /// Tries to get an entry by its name.
    /// </summary>
    /// <param name="name">Entry name</param>
    /// <param name="entry">Resolved entry when found</param>
    /// <returns>True if found; otherwise false</returns>
    public bool TryGetEntry(string name, [NotNullWhen(true)] out PackageEntry? entry)
    {
        return _entries.TryGetValue(name, out entry);
    }

    /// <summary>
    /// Reads the full content of the specified entry into the provided buffer.
    /// Buffer length must equal the entry size.
    /// </summary>
    /// <param name="entry">Entry descriptor</param>
    /// <param name="buffer">Destination buffer; length must equal entry size</param>
    public void ReadByEntry(PackageEntry entry, Span<byte> buffer)
    {
        if (entry.Size < 0 || entry.Size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(entry.Size), "Entry size must be within Int32 range.");
        }
        if (buffer.Length != (int)entry.Size)
        {
            throw new ArgumentException("Buffer length must equal entry size.", nameof(buffer));
        }

        long absoluteOffset = checked(_contentBase + entry.Start);
        CheckLength(absoluteOffset, (int)entry.Size);
        Read(buffer, absoluteOffset);
    }

    private int Read(Span<byte> buffer, long offset)
    {
        CheckLength(offset, buffer.Length);
        if (_file != null)
        {
            return RandomAccess.Read(_file, buffer, offset);
        }
        else if (_memory != null)
        {
            Span<byte> memory = _memory.AsSpan();
            int offsetInt = checked((int)offset);
            memory.Slice(offsetInt, buffer.Length).CopyTo(buffer);
            return buffer.Length;
        }
        else
        {
            throw new InvalidOperationException("No file or memory handle is available");
        }
    }

    private int ReadUnsafe(byte* buffer, long offset, int size)
    {
        CheckLength(offset, size);
        if (_file != null)
        {
            return RandomAccess.Read(_file, new Span<byte>(buffer, size), offset);
        }
        else if (_memory != null)
        {
            Span<byte> memory = _memory.AsSpan();
            int offsetInt = checked((int)offset);
            memory.Slice(offsetInt, size).CopyTo(new Span<byte>(buffer, size));
            return size;
        }
        else
        {
            throw new InvalidOperationException("No file or memory handle is available");
        }
    }

    private T ReadValue<T>(long offset) where T : unmanaged
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

    private FrozenDictionary<string, PackageEntry> ReadEntries(out long contentBase)
    {
        long metaLength = ReadValue<long>(0);
        if (metaLength < 0)
        {
            throw new InvalidDataException($"Negative meta length: {metaLength}");
        }
        if (8L + metaLength > _length)
        {
            throw new InvalidDataException($"Meta section exceeds package length. MetaLength={metaLength}, Length={_length}");
        }
        if (metaLength > int.MaxValue)
        {
            throw new InvalidDataException($"Meta length too large (>{int.MaxValue}).");
        }

        int metaLengthInt = (int)metaLength;
        byte[] meta = new byte[metaLengthInt];
        Read(meta, 8L);
        PackageMeta packageMeta = Alco.BinaryParser.Decode<PackageMeta>(meta);
        contentBase = 8L + metaLength;
        return packageMeta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
    }

    protected override void Dispose(bool disposing)
    {
        _file?.Dispose();
    }

    /// <summary>
    /// Opens a package reader from a file path.
    /// </summary>
    public static PackageReader OpenFile(string path)
    {
        return new PackageReader(path);
    }

    /// <summary>
    /// Opens a package reader over a managed byte array.
    /// </summary>
    public static PackageReader OpenMemory(byte[] data)
    {
        return new PackageReader(data);
    }

    /// <summary>
    /// Opens a package reader over an unmanaged memory region.
    /// </summary>
    public static PackageReader OpenUnsafeMemory(byte* data, int size)
    {
        return new PackageReader(data, size);
    }
}
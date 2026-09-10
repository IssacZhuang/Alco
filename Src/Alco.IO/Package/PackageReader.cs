using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Hashing;
using System.Text;


namespace Alco.IO;

/// <summary>
/// Reads an Alco package over a file, byte array, unmanaged memory, or seekable <see cref="Stream"/>.
/// Validates the file magic and format version against <typeparamref name="TMeta"/>'s magic, then
/// loads the entry directory referenced by the newest valid header descriptor. Supports concurrent
/// positional reads; each thread supplies its own destination buffer.
/// </summary>
/// <typeparam name="TMeta">The package metadata type, which must implement <see cref="IPackageMeta"/>.</typeparam>
public unsafe sealed class PackageReader<TMeta> : AutoDisposable where TMeta : PackageMetaBase, IPackageMeta, new()
{
    // Only one of the backing stores is used.
    private readonly SafeFileHandle? _file;
    private readonly SafeMemoryHandle? _memory;
    private readonly Stream? _stream;
    private readonly bool _ownsStream;
    private readonly long _length;

    // Base offset of the content section: the fixed header size (see PackageFormat)
    private readonly long _contentBase;

    private readonly FrozenDictionary<string, PackageEntry> _entries;
    private readonly string[] _allFileNames;

    /// <summary>Gets the sorted list of all entry names.</summary>
    public IReadOnlyList<string> AllFileNames => _allFileNames;

    /// <summary>Gets the decoded package metadata (entry directory + any type-specific fields).</summary>
    public TMeta Meta { get; }

    /// <summary>
    /// Gets or sets a value indicating whether full-entry reads verify the entry's XxHash64
    /// checksum and throw <see cref="InvalidDataException"/> on mismatch (torn writes, bit rot).
    /// Off by default: verification costs one hash pass per read. Partial reads
    /// (<see cref="ReadByEntry(PackageEntry, Span{byte}, long)"/>) are never verified.
    /// </summary>
    public bool VerifyEntryChecksums { get; set; }

    /// <summary>
    /// Opens a package reader from a file path.
    /// </summary>
    /// <param name="path">Package file path</param>
    internal PackageReader(string path)
    {
        //open with read; FileShare.Write tolerates a concurrently open PackageWriter (append-only
        //commits never move bytes a resolved directory references, so positional reads stay safe)
        _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Write, FileOptions.Asynchronous);
        _length = RandomAccess.GetLength(_file);
        Meta = ReadEntries(out _contentBase);
        _entries = Meta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
        _allFileNames = _entries.Keys.ToArray();
        Array.Sort(_allFileNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Opens a package reader over a managed byte array.
    /// </summary>
    /// <param name="data">Package bytes</param>
    internal PackageReader(byte[] data)
    {
        _memory = new SafeMemoryHandle(data);
        _length = data.Length;
        Meta = ReadEntries(out _contentBase);
        _entries = Meta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
        _allFileNames = _entries.Keys.ToArray();
        Array.Sort(_allFileNames, StringComparer.Ordinal);
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
        Meta = ReadEntries(out _contentBase);
        _entries = Meta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
        _allFileNames = _entries.Keys.ToArray();
        Array.Sort(_allFileNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Opens a package reader over a seekable stream. The stream is not disposed by the reader when
    /// <paramref name="ownsStream"/> is <see langword="false"/> (the caller owns its lifetime).
    /// </summary>
    /// <param name="stream">Seekable read stream positioned at the package start (offset 0).</param>
    /// <param name="ownsStream">When <see langword="true"/>, the reader disposes the stream.</param>
    internal PackageReader(Stream stream, bool ownsStream)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
        }

        _stream = stream;
        _ownsStream = ownsStream;
        _length = stream.Length;
        Meta = ReadEntries(out _contentBase);
        _entries = Meta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
        _allFileNames = _entries.Keys.ToArray();
        Array.Sort(_allFileNames, StringComparer.Ordinal);
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

        if (VerifyEntryChecksums && entry.Checksum != 0 && XxHash64.HashToUInt64(buffer) != entry.Checksum)
        {
            throw new InvalidDataException($"Entry '{entry.Name}' content checksum mismatch; the package is corrupt.");
        }
    }

    /// <summary>
    /// Reads a portion of the specified entry into the provided buffer.
    /// </summary>
    /// <param name="entry">Entry descriptor</param>
    /// <param name="buffer">Destination buffer</param>
    /// <param name="entryOffset">Offset within the entry to start reading from</param>
    /// <returns>The number of bytes read</returns>
    public int ReadByEntry(PackageEntry entry, Span<byte> buffer, long entryOffset)
    {
        if (entry.Size < 0 || entry.Size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(entry.Size), "Entry size must be within Int32 range.");
        }
        if (entryOffset < 0 || entryOffset >= entry.Size)
        {
            throw new ArgumentOutOfRangeException(nameof(entryOffset), "Entry offset must be within entry bounds.");
        }

        int bytesToRead = Math.Min(buffer.Length, (int)(entry.Size - entryOffset));
        if (bytesToRead == 0)
        {
            return 0;
        }

        long absoluteOffset = checked(_contentBase + entry.Start + entryOffset);
        CheckLength(absoluteOffset, bytesToRead);
        Read(buffer[..bytesToRead], absoluteOffset);
        return bytesToRead;
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
        else if (_stream != null)
        {
            _stream.Position = offset;
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int read = _stream.Read(buffer[totalRead..]);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"Unexpected end of stream at offset {offset + totalRead}.");
                }
                totalRead += read;
            }
            return totalRead;
        }
        else
        {
            throw new InvalidOperationException("No file, memory, or stream backing is available");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckLength(long offset, int size)
    {
        if (offset + size > _length)
        {
            throw new IndexOutOfRangeException($"Offset and size exceed the package length. Offset: {offset}, Size: {size}, Length: {_length}");
        }
    }

    private TMeta ReadEntries(out long contentBase)
    {
        ReadOnlySpan<byte> expectedMagic = TMeta.Magic;

        if (_length < PackageFormat.HeaderSize)
        {
            throw new InvalidDataException($"Package too small ({_length} bytes) to contain a {PackageFormat.HeaderSize}-byte header.");
        }

        // Verify magic number and format version, then resolve the entry directory through the
        // two double-buffered header descriptors (see PackageFormat).
        Span<byte> header = stackalloc byte[PackageFormat.HeaderSize];
        Read(header, 0);
        if (!header[..4].SequenceEqual(expectedMagic))
        {
            throw new InvalidDataException($"Invalid package magic. Expected '{Encoding.ASCII.GetString(expectedMagic)}'.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (version != PackageFormat.Version)
        {
            throw new InvalidDataException($"Unsupported package format version {version} (expected {PackageFormat.Version}).");
        }

        PackageDirectoryDescriptor a = PackageDirectoryDescriptor.Parse(header, PackageFormat.DescriptorAOffset);
        PackageDirectoryDescriptor b = PackageDirectoryDescriptor.Parse(header, PackageFormat.DescriptorBOffset);

        // Prefer the higher sequence (ties resolve to A); fall back to the other descriptor when
        // the preferred one fails validation (torn descriptor patch, corruption).
        TMeta? meta = b.Sequence > a.Sequence
            ? TryReadDirectory(b) ?? TryReadDirectory(a)
            : TryReadDirectory(a) ?? TryReadDirectory(b);

        if (meta == null)
        {
            throw new InvalidDataException("No valid entry directory descriptor found; the package is corrupt.");
        }

        contentBase = PackageFormat.HeaderSize;
        return meta;
    }

    /// <summary>
    /// Loads and validates the directory referenced by one descriptor. Returns null when the
    /// descriptor is unused, out of bounds, or fails its hash check — never throws.
    /// </summary>
    /// <param name="descriptor">The header descriptor to attempt.</param>
    /// <returns>The decoded meta, or null when the descriptor is not usable.</returns>
    private TMeta? TryReadDirectory(in PackageDirectoryDescriptor descriptor)
    {
        if (descriptor.IsUnused || descriptor.Length == 0)
        {
            return null;
        }

        if (descriptor.Offset < PackageFormat.HeaderSize || checked(descriptor.Offset + descriptor.Length) > _length)
        {
            return null;
        }

        byte[] directory = new byte[descriptor.Length];
        Read(directory, descriptor.Offset);
        if (XxHash64.HashToUInt64(directory) != descriptor.Hash)
        {
            return null;
        }

        return Alco.BinaryParser.Decode<TMeta>(directory);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file?.Dispose();
            if (_ownsStream)
            {
                _stream?.Dispose();
            }
        }
    }

    /// <summary>
    /// Opens a package reader from a file path.
    /// </summary>
    public static PackageReader<TMeta> OpenFile(string path)
    {
        return new PackageReader<TMeta>(path);
    }

    /// <summary>
    /// Opens a package reader over a managed byte array.
    /// </summary>
    public static PackageReader<TMeta> OpenMemory(byte[] data)
    {
        return new PackageReader<TMeta>(data);
    }

    /// <summary>
    /// Opens a package reader over an unmanaged memory region.
    /// </summary>
    public static PackageReader<TMeta> OpenUnsafeMemory(byte* data, int size)
    {
        return new PackageReader<TMeta>(data, size);
    }

    /// <summary>
    /// Opens a package reader over a seekable stream. The reader does not dispose the stream; the
    /// caller is responsible for its lifetime (e.g. via a <see langword="using"/> block).
    /// </summary>
    /// <param name="stream">Seekable read stream positioned at the package start (offset 0).</param>
    public static PackageReader<TMeta> OpenStream(Stream stream)
    {
        return new PackageReader<TMeta>(stream, ownsStream: false);
    }
}

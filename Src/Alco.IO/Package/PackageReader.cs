using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;


namespace Alco.IO;

/// <summary>
/// Reads an Alco package over a file, byte array, unmanaged memory, or seekable <see cref="Stream"/>.
/// Validates the file magic against <typeparamref name="TMeta"/>'s magic and decodes the entry
/// directory (the <see cref="PackageMetaBase.Entries"/> inherited by <typeparamref name="TMeta"/>).
/// Supports both plaintext (v1) and AES-256-GCM encrypted (v2) packages; encrypted packages
/// require the master key and are decrypted transparently, including random-offset entry reads.
/// Supports concurrent positional reads; each thread supplies its own destination buffer.
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

    // Base offset of the content section: 12 + meta section length
    private readonly long _contentBase;

    // Cipher for encrypted (v2) packages; null for plaintext (v1) packages
    private readonly PackageCipher? _cipher;

    private readonly FrozenDictionary<string, PackageEntry> _entries;
    private readonly string[] _allFileNames;

    /// <summary>Gets the sorted list of all entry names.</summary>
    public IReadOnlyList<string> AllFileNames => _allFileNames;

    /// <summary>Gets the decoded package metadata (entry directory + any type-specific fields).</summary>
    public TMeta Meta { get; }

    /// <summary>
    /// Opens a package reader from a file path.
    /// </summary>
    /// <param name="path">Package file path</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    internal PackageReader(string path, byte[]? encryptionKey)
    {
        //open with read
        _file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.Asynchronous);
        _length = RandomAccess.GetLength(_file);
        Meta = ReadEntries(out _contentBase, out _cipher, encryptionKey);
        _entries = Meta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
        _allFileNames = _entries.Keys.ToArray();
        Array.Sort(_allFileNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Opens a package reader over a managed byte array.
    /// </summary>
    /// <param name="data">Package bytes</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    internal PackageReader(byte[] data, byte[]? encryptionKey)
    {
        _memory = new SafeMemoryHandle(data);
        _length = data.Length;
        Meta = ReadEntries(out _contentBase, out _cipher, encryptionKey);
        _entries = Meta.Entries.ToFrozenDictionary(entry => entry.Name, entry => entry);
        _allFileNames = _entries.Keys.ToArray();
        Array.Sort(_allFileNames, StringComparer.Ordinal);
    }

    /// <summary>
    /// Opens a package reader over an unmanaged memory region.
    /// </summary>
    /// <param name="data">Pointer to package bytes</param>
    /// <param name="size">Size of the package in bytes</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    internal PackageReader(byte* data, int size, byte[]? encryptionKey)
    {
        _memory = new SafeMemoryHandle(data, size);
        _length = size;
        Meta = ReadEntries(out _contentBase, out _cipher, encryptionKey);
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
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    internal PackageReader(Stream stream, bool ownsStream, byte[]? encryptionKey)
    {
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
        }

        _stream = stream;
        _ownsStream = ownsStream;
        _length = stream.Length;
        Meta = ReadEntries(out _contentBase, out _cipher, encryptionKey);
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
        if (_cipher == null)
        {
            CheckLength(absoluteOffset, (int)entry.Size);
            Read(buffer, absoluteOffset);
            return;
        }

        CheckLength(absoluteOffset, (int)PackageCipher.StoredEntryLength(entry.Size));
        ReadEncryptedEntry(entry, buffer);
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
        if (_cipher == null)
        {
            CheckLength(absoluteOffset, bytesToRead);
            Read(buffer[..bytesToRead], absoluteOffset);
            return bytesToRead;
        }

        return ReadEncryptedEntryRanged(entry, buffer[..bytesToRead], entryOffset);
    }

    /// <summary>
    /// Decrypts an entire entry in place: each chunk's ciphertext is read into its destination
    /// slice, its tag read separately, then the slice decrypted in place.
    /// </summary>
    private void ReadEncryptedEntry(PackageEntry entry, Span<byte> buffer)
    {
        Span<byte> tag = stackalloc byte[PackageCipher.TagSize];
        int chunkStride = PackageCipher.ChunkSize + PackageCipher.TagSize;
        int written = 0;
        int chunkCount = PackageCipher.ChunkCount(entry.Size);
        for (int i = 0; i < chunkCount; i++)
        {
            int chunkLength = (int)Math.Min(PackageCipher.ChunkSize, entry.Size - written);
            long chunkOffset = checked(_contentBase + entry.Start + (long)i * chunkStride);
            Span<byte> destination = buffer.Slice(written, chunkLength);
            Read(destination, chunkOffset);
            Read(tag, chunkOffset + chunkLength);
            try
            {
                _cipher!.DecryptChunk(entry.Start, i, destination, tag);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException(
                    $"Failed to decrypt chunk {i} of entry '{entry.Name}' (wrong key or corrupted package).", ex);
            }

            written += chunkLength;
        }
    }

    /// <summary>
    /// Decrypts an entry range by decrypting each covering chunk into a pooled scratch buffer and
    /// copying the requested overlap.
    /// </summary>
    private int ReadEncryptedEntryRanged(PackageEntry entry, Span<byte> buffer, long entryOffset)
    {
        int chunkStride = PackageCipher.ChunkSize + PackageCipher.TagSize;
        long firstChunk = entryOffset / PackageCipher.ChunkSize;
        long lastChunk = (entryOffset + buffer.Length - 1) / PackageCipher.ChunkSize;
        long endOffset = entryOffset + buffer.Length;

        byte[] scratch = ArrayPool<byte>.Shared.Rent(chunkStride);
        try
        {
            int copied = 0;
            for (long chunk = firstChunk; chunk <= lastChunk; chunk++)
            {
                int chunkLength = (int)Math.Min(PackageCipher.ChunkSize, entry.Size - chunk * PackageCipher.ChunkSize);
                long chunkOffset = checked(_contentBase + entry.Start + chunk * chunkStride);
                CheckLength(chunkOffset, chunkLength + PackageCipher.TagSize);
                Read(scratch.AsSpan(0, chunkLength + PackageCipher.TagSize), chunkOffset);
                try
                {
                    _cipher!.DecryptChunk(entry.Start, (int)chunk,
                        scratch.AsSpan(0, chunkLength), scratch.AsSpan(chunkLength, PackageCipher.TagSize));
                }
                catch (CryptographicException ex)
                {
                    throw new InvalidDataException(
                        $"Failed to decrypt chunk {chunk} of entry '{entry.Name}' (wrong key or corrupted package).", ex);
                }

                long chunkStart = chunk * PackageCipher.ChunkSize;
                long overlapStart = Math.Max(chunkStart, entryOffset);
                long overlapEnd = Math.Min(chunkStart + chunkLength, endOffset);
                int overlapLength = (int)(overlapEnd - overlapStart);
                scratch.AsSpan((int)(overlapStart - chunkStart), overlapLength)
                    .CopyTo(buffer.Slice(copied, overlapLength));
                copied += overlapLength;
            }

            return copied;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
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
        else if (_stream != null)
        {
            return Read(new Span<byte>(buffer, size), offset);
        }
        else
        {
            throw new InvalidOperationException("No file, memory, or stream backing is available");
        }
    }

    private long ReadInt64LittleEndian(long offset)
    {
        byte* ptr = stackalloc byte[8];
        ReadUnsafe(ptr, offset, 8);
        return BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(ptr, 8));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckLength(long offset, int size)
    {
        if (offset + size > _length)
        {
            throw new IndexOutOfRangeException($"Offset and size exceed the package length. Offset: {offset}, Size: {size}, Length: {_length}");
        }
    }

    /// <summary>
    /// Validates the magic and decodes the meta section. A negative header length field marks an
    /// encrypted (v2) package: the low 63 bits give the size of the salt + encrypted meta section.
    /// </summary>
    private TMeta ReadEntries(out long contentBase, out PackageCipher? cipher, byte[]? encryptionKey)
    {
        ReadOnlySpan<byte> expectedMagic = TMeta.Magic;

        // Verify magic number
        Span<byte> magicBuffer = stackalloc byte[4];
        Read(magicBuffer, 0);
        if (!magicBuffer.SequenceEqual(expectedMagic))
        {
            throw new InvalidDataException($"Invalid package magic. Expected '{Encoding.ASCII.GetString(expectedMagic)}'.");
        }

        long headerField = ReadInt64LittleEndian(4);
        byte[] meta;
        if (headerField < 0)
        {
            long sectionLength = headerField & long.MaxValue;
            if (sectionLength <= PackageCipher.SaltSize + PackageCipher.TagSize)
            {
                throw new InvalidDataException($"Encrypted meta section too small: {sectionLength}");
            }
            if (12L + sectionLength > _length)
            {
                throw new InvalidDataException($"Meta section exceeds package length. MetaLength={sectionLength}, Length={_length}");
            }
            if (encryptionKey == null)
            {
                throw new InvalidDataException(
                    "Package is encrypted but no encryption key was provided. Pass the master key when opening the package.");
            }

            Span<byte> salt = stackalloc byte[PackageCipher.SaltSize];
            Read(salt, 12L);

            cipher = PackageCipher.Create(encryptionKey, salt);
            byte[] encryptedMeta = new byte[sectionLength - PackageCipher.SaltSize];
            Read(encryptedMeta, 12L + PackageCipher.SaltSize);
            try
            {
                meta = cipher.DecryptMeta(encryptedMeta);
            }
            catch (CryptographicException ex)
            {
                cipher.Dispose();
                throw new InvalidDataException("Failed to decrypt package metadata (wrong key or corrupted package).", ex);
            }

            contentBase = 12L + sectionLength;
        }
        else
        {
            long metaLength = headerField;
            if (12L + metaLength > _length)
            {
                throw new InvalidDataException($"Meta section exceeds package length. MetaLength={metaLength}, Length={_length}");
            }
            if (metaLength > int.MaxValue)
            {
                throw new InvalidDataException($"Meta length too large (>{int.MaxValue}).");
            }

            int metaLengthInt = (int)metaLength;
            meta = new byte[metaLengthInt];
            Read(meta, 12L);
            cipher = null;
            contentBase = 12L + metaLength;
        }

        return Alco.BinaryParser.Decode<TMeta>(meta);
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
            _cipher?.Dispose();
        }
    }

    /// <summary>
    /// Opens a package reader from a file path.
    /// </summary>
    /// <param name="path">Package file path</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    public static PackageReader<TMeta> OpenFile(string path, byte[]? encryptionKey = null)
    {
        return new PackageReader<TMeta>(path, encryptionKey);
    }

    /// <summary>
    /// Opens a package reader over a managed byte array.
    /// </summary>
    /// <param name="data">Package bytes</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    public static PackageReader<TMeta> OpenMemory(byte[] data, byte[]? encryptionKey = null)
    {
        return new PackageReader<TMeta>(data, encryptionKey);
    }

    /// <summary>
    /// Opens a package reader over an unmanaged memory region.
    /// </summary>
    /// <param name="data">Pointer to package bytes</param>
    /// <param name="size">Size of the package in bytes</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    public static PackageReader<TMeta> OpenUnsafeMemory(byte* data, int size, byte[]? encryptionKey = null)
    {
        return new PackageReader<TMeta>(data, size, encryptionKey);
    }

    /// <summary>
    /// Opens a package reader over a seekable stream. The reader does not dispose the stream; the
    /// caller is responsible for its lifetime (e.g. via a <see langword="using"/> block).
    /// </summary>
    /// <param name="stream">Seekable read stream positioned at the package start (offset 0).</param>
    /// <param name="encryptionKey">Master key for encrypted packages; ignored for plaintext packages</param>
    public static PackageReader<TMeta> OpenStream(Stream stream, byte[]? encryptionKey = null)
    {
        return new PackageReader<TMeta>(stream, ownsStream: false, encryptionKey);
    }
}

using System.IO;
using System.IO.Hashing;
using Microsoft.Win32.SafeHandles;

using Alco;

namespace Alco.IO;

/// <summary>
/// Incremental (append-only) writer for Alco packages. Unlike <see cref="PackageBuilder{TMeta}"/>,
/// which seals a package in one full rewrite, the writer keeps a file open and commits individual
/// updates cheaply: new entry bytes and a new entry directory are appended at the end of the file,
/// then one of the two double-buffered header descriptors is repointed at the new directory
/// (a single 24-byte in-place patch, sequence encoded last). Superseded bytes stay behind as
/// garbage until <see cref="CompactFile"/> reclaims them.
///
/// Crash safety: a crash before or during the descriptor patch leaves the previous descriptor
/// fully intact, so the package always opens at the last committed directory; the partially
/// appended bytes are ignored as garbage. At worst one commit is lost, never the package.
///
/// Concurrency: commits are serialized internally; the file is opened with
/// <see cref="FileShare.Read"/> so <see cref="PackageReader{TMeta}"/> instances may read
/// concurrently while the writer is open. Readers opened before a commit keep observing the
/// directory they were opened with.
/// </summary>
/// <typeparam name="TMeta">The package metadata type, which must implement <see cref="IPackageMeta"/>.</typeparam>
public sealed class PackageWriter<TMeta> : AutoDisposable where TMeta : PackageMetaBase, IPackageMeta, new()
{
    private readonly SafeFileHandle _file;
    private readonly object _commitLock = new();
    private readonly TMeta _meta;

    // File length as of the last commit; appends start here.
    private long _length;

    // Sum of live entry sizes and the size of the current directory, tracked for GarbageBytes.
    private long _liveBytes;
    private long _directoryLength;
    private uint _sequence;

    // Header descriptor index (0 = A, 1 = B) to patch on the next commit.
    private int _nextDescriptor;

    /// <summary>Gets the live package metadata (entry directory + type-specific fields).</summary>
    public TMeta Meta => _meta;

    /// <summary>
    /// Gets the number of bytes occupied by garbage: replaced or removed entry payloads and
    /// superseded directories. Zero right after <see cref="Create"/> or
    /// <see cref="PackageBuilder{TMeta}.Build()"/>; grows with every update that rewrites or
    /// removes an entry. Reclaimed by <see cref="CompactFile"/>.
    /// </summary>
    public long GarbageBytes
    {
        get
        {
            lock (_commitLock)
            {
                return _length - PackageFormat.HeaderSize - _liveBytes - _directoryLength;
            }
        }
    }

    private PackageWriter(SafeFileHandle file, TMeta meta, long length, long liveBytes, long directoryLength, uint sequence, int nextDescriptor)
    {
        _file = file;
        _meta = meta;
        _length = length;
        _liveBytes = liveBytes;
        _directoryLength = directoryLength;
        _sequence = sequence;
        _nextDescriptor = nextDescriptor;
    }

    /// <summary>
    /// Creates (or truncates) a package file with an empty entry directory. The initial directory
    /// commit lands in descriptor A with the initial sequence; descriptor B stays unused.
    /// </summary>
    /// <param name="path">Package file path.</param>
    /// <param name="meta">Initial metadata; when null, a default-constructed <typeparamref name="TMeta"/>.</param>
    /// <returns>An open writer over the new file.</returns>
    public static PackageWriter<TMeta> Create(string path, TMeta? meta = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        SafeFileHandle file = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous);
        try
        {
            Span<byte> header = stackalloc byte[PackageFormat.HeaderSize];
            PackageHeader.Encode(header, TMeta.Magic, PackageDirectoryDescriptor.Unused, PackageDirectoryDescriptor.Unused);
            RandomAccess.Write(file, header, 0);

            PackageWriter<TMeta> writer = new(file, meta ?? new TMeta(), PackageFormat.HeaderSize, 0, 0, 0, nextDescriptor: 0);
            writer.CommitDirectory();
            return writer;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an existing package file for incremental updates. The newest valid header descriptor
    /// selects the starting directory; invalid descriptors are skipped exactly as in
    /// <see cref="PackageReader{TMeta}"/>.
    /// </summary>
    /// <param name="path">Package file path.</param>
    /// <returns>An open writer over the existing file.</returns>
    public static PackageWriter<TMeta> OpenFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous);
        try
        {
            long length = RandomAccess.GetLength(file);
            if (length < PackageFormat.HeaderSize)
            {
                throw new InvalidDataException($"Package '{path}' too small ({length} bytes) to contain a {PackageFormat.HeaderSize}-byte header.");
            }

            Span<byte> header = stackalloc byte[PackageFormat.HeaderSize];
            ReadExactly(file, header, 0);
            if (!header[..4].SequenceEqual(TMeta.Magic))
            {
                throw new InvalidDataException($"Invalid package magic in '{path}'.");
            }

            uint version = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if (version != PackageFormat.Version)
            {
                throw new InvalidDataException($"Unsupported package format version {version} in '{path}' (expected {PackageFormat.Version}).");
            }

            PackageDirectoryDescriptor a = PackageDirectoryDescriptor.Parse(header, PackageFormat.DescriptorAOffset);
            PackageDirectoryDescriptor b = PackageDirectoryDescriptor.Parse(header, PackageFormat.DescriptorBOffset);

            bool preferB = b.Sequence > a.Sequence;
            PackageDirectoryDescriptor preferred = preferB ? b : a;
            PackageDirectoryDescriptor fallback = preferB ? a : b;
            int preferredIndex = preferB ? 1 : 0;

            TMeta? meta = TryLoadDirectory(file, length, preferred);
            PackageDirectoryDescriptor chosen = preferred;
            int chosenIndex = preferredIndex;
            if (meta == null)
            {
                meta = TryLoadDirectory(file, length, fallback);
                chosen = fallback;
                chosenIndex = 1 - preferredIndex;
                if (meta == null)
                {
                    throw new InvalidDataException($"Package '{path}' has no valid entry directory descriptor; the file is corrupt.");
                }
            }

            long liveBytes = 0;
            foreach (PackageEntry entry in meta.Entries)
            {
                liveBytes += entry.Size;
            }

            return new PackageWriter<TMeta>(file, meta, length, liveBytes, chosen.Length, chosen.Sequence, 1 - chosenIndex);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adds a new entry or updates an existing entry's content: the bytes are appended at the end
    /// of the file and a new directory is committed. The entry's previous bytes (if any) become
    /// garbage. This is the whole cost of an incremental save — no other entry is rewritten.
    /// </summary>
    /// <param name="entryName">Logical entry name (e.g., virtual path).</param>
    /// <param name="data">Entry bytes.</param>
    public void UpdateFile(string entryName, ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        if (string.IsNullOrEmpty(entryName))
        {
            throw new ArgumentException("Entry name must not be null or empty.", nameof(entryName));
        }

        lock (_commitLock)
        {
            long entryOffset = _length;
            RandomAccess.Write(_file, data, entryOffset);
            _length += data.Length;

            if (TryGetEntrySize(entryName, out long previousSize))
            {
                _liveBytes -= previousSize;
            }
            _liveBytes += data.Length;
            _meta.AddOrUpdateEntry(entryName, entryOffset - PackageFormat.HeaderSize, data.Length, XxHash64.HashToUInt64(data));

            CommitDirectory();
        }
    }

    /// <summary>
    /// Removes an entry by name without touching any bytes: the entry's payload becomes garbage
    /// and a new directory is committed.
    /// </summary>
    /// <param name="entryName">Logical entry name.</param>
    /// <returns>True when an entry was removed; false when no such entry exists.</returns>
    public bool RemoveFile(string entryName)
    {
        ThrowIfDisposed();

        lock (_commitLock)
        {
            if (!TryGetEntrySize(entryName, out long previousSize) || !_meta.RemoveEntry(entryName))
            {
                return false;
            }

            _liveBytes -= previousSize;
            CommitDirectory();
            return true;
        }
    }

    /// <summary>
    /// Commits the current state of <see cref="Meta"/> (e.g. mutated type-specific fields such as
    /// a save's play time) as a new directory without touching any entry bytes.
    /// </summary>
    public void CommitMeta()
    {
        ThrowIfDisposed();

        lock (_commitLock)
        {
            CommitDirectory();
        }
    }

    /// <summary>
    /// Reclaims all garbage in a package: reads every live entry, writes a fully compact package
    /// (the <see cref="PackageBuilder{TMeta}.Build()"/> layout) to a temporary file, then atomically
    /// replaces the original. All writers and readers must be closed before calling this. On
    /// failure the original file is left untouched.
    /// </summary>
    /// <param name="path">Package file path.</param>
    public static void CompactFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string tempPath = path + ".compact.tmp";
        try
        {
            using (PackageReader<TMeta> reader = PackageReader<TMeta>.OpenFile(path))
            {
                PackageBuilder<TMeta> builder = new() { Meta = reader.Meta };
                foreach (string name in reader.AllFileNames)
                {
                    if (!reader.TryGetEntry(name, out PackageEntry? entry))
                    {
                        continue;
                    }

                    byte[] buffer = new byte[entry.Size];
                    reader.ReadByEntry(entry, buffer);
                    builder.AddOrUpdateFile(name, buffer);
                }

                using FileStream output = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                builder.Build(output);
                output.Flush(flushToDisk: true);
            }

            File.Replace(tempPath, path, null);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>
    /// Appends a freshly encoded directory and repoints the stale header descriptor at it. The
    /// flush order is the crash-safety contract: blob bytes (written by the caller) and the
    /// directory must be durable before the descriptor patch, so a crash leaves the previous
    /// descriptor authoritative.
    /// </summary>
    private void CommitDirectory()
    {
        ReadOnlyMemory<byte> directory = BinaryParser.Encode(_meta);
        long directoryOffset = _length;
        RandomAccess.Write(_file, directory.Span, directoryOffset);
        RandomAccess.FlushToDisk(_file);

        PackageDirectoryDescriptor descriptor = new()
        {
            Offset = directoryOffset,
            Length = (uint)directory.Length,
            Hash = XxHash64.HashToUInt64(directory.Span),
            Sequence = _sequence + 1,
        };
        Span<byte> encoded = stackalloc byte[PackageFormat.DescriptorSize];
        descriptor.Encode(encoded, 0);
        RandomAccess.Write(_file, encoded, PackageFormat.DescriptorOffset(_nextDescriptor));
        RandomAccess.FlushToDisk(_file);

        _length += directory.Length;
        _directoryLength = directory.Length;
        _sequence++;
        _nextDescriptor = 1 - _nextDescriptor;
    }

    private bool TryGetEntrySize(string entryName, out long size)
    {
        foreach (PackageEntry entry in _meta.Entries)
        {
            if (string.Equals(entry.Name, entryName, StringComparison.Ordinal))
            {
                size = entry.Size;
                return true;
            }
        }

        size = 0;
        return false;
    }

    private static TMeta? TryLoadDirectory(SafeFileHandle file, long fileLength, in PackageDirectoryDescriptor descriptor)
    {
        if (descriptor.IsUnused || descriptor.Length == 0)
        {
            return null;
        }

        if (descriptor.Offset < PackageFormat.HeaderSize || checked(descriptor.Offset + descriptor.Length) > fileLength)
        {
            return null;
        }

        byte[] directory = new byte[descriptor.Length];
        ReadExactly(file, directory, descriptor.Offset);
        if (XxHash64.HashToUInt64(directory) != descriptor.Hash)
        {
            return null;
        }

        return BinaryParser.Decode<TMeta>(directory);
    }

    /// <summary>
    /// Reads until <paramref name="buffer"/> is filled or the stream ends.
    /// </summary>
    /// <param name="file">The file handle to read from.</param>
    /// <param name="buffer">Destination buffer.</param>
    /// <param name="offset">File offset to read from.</param>
    private static void ReadExactly(SafeFileHandle file, Span<byte> buffer, long offset)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = RandomAccess.Read(file, buffer[totalRead..], offset + totalRead);
            if (read <= 0)
            {
                throw new EndOfStreamException($"Unexpected end of file at offset {offset + totalRead}.");
            }

            totalRead += read;
        }
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(PackageWriter<TMeta>));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file.Dispose();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Enumeration;
using System.Threading;

namespace Alco.IO;

/// <summary>
/// Thread-safe in-memory <see cref="IRandomAccessStorage"/> modeling inode replacement and unlinked open handles.
/// </summary>
public sealed class InMemoryRandomAccessStorage : IRandomAccessStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MemoryFile> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a standalone read-only handle over an immutable in-memory buffer.
    /// </summary>
    /// <param name="data">The backing bytes.</param>
    /// <returns>A read-only handle.</returns>
    public static IRandomAccessHandle CreateReadOnlyHandle(ReadOnlySpan<byte> data) =>
        new MemoryRandomAccessHandle(new MemoryFile(data), writable: false);

    /// <summary>
    /// Creates an empty writable in-memory handle and returns both the handle and a snapshot reader for its contents.
    /// </summary>
    /// <returns>A writable in-memory handle.</returns>
    public static MemoryBufferHandle CreateStandaloneWritableHandle() => new();

    /// <inheritdoc/>
    public bool FileExists(string relativePath)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        lock (_gate)
        {
            return _files.ContainsKey(normalized);
        }
    }

    /// <inheritdoc/>
    public bool TryOpenRead(string relativePath, [NotNullWhen(true)] out IRandomAccessHandle? handle)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        MemoryFile? file;
        lock (_gate)
        {
            if (!_files.TryGetValue(normalized, out file))
            {
                handle = null;
                return false;
            }
        }

        handle = new MemoryRandomAccessHandle(file, writable: false);
        return true;
    }

    /// <inheritdoc/>
    public IRandomAccessHandle OpenAppendOrCreate(string relativePath)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        MemoryFile? file;
        lock (_gate)
        {
            if (!_files.TryGetValue(normalized, out file))
            {
                file = new MemoryFile();
                _files.Add(normalized, file);
            }
        }

        return new MemoryRandomAccessHandle(file, writable: true);
    }

    /// <inheritdoc/>
    public void WriteAtomic(string relativePath, ReadOnlySpan<byte> data, bool flushToDisk = true)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        var replacement = new MemoryFile(data);
        lock (_gate)
        {
            _files[normalized] = replacement;
        }
    }

    /// <inheritdoc/>
    public bool TryReadAllBytes(string relativePath, [NotNullWhen(true)] out byte[]? data)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        MemoryFile? file;
        lock (_gate)
        {
            if (!_files.TryGetValue(normalized, out file))
            {
                data = null;
                return false;
            }
        }

        data = file.ToArray();
        return true;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateFiles(string relativeDirectory, string searchPattern, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        string normalizedDir = StreamingSavePaths.NormalizeRelativeDirectoryPath(relativeDirectory);
        string prefix = normalizedDir.Length == 0 ? string.Empty : normalizedDir + "/";

        var results = new List<string>();
        lock (_gate)
        {
            foreach (string key in _files.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                ReadOnlySpan<char> remainder = key.AsSpan(prefix.Length);
                if (!recursive && remainder.Contains('/'))
                    continue;

                int lastSlash = remainder.LastIndexOf('/');
                ReadOnlySpan<char> fileName = lastSlash >= 0 ? remainder[(lastSlash + 1)..] : remainder;
                if (FileSystemName.MatchesSimpleExpression(searchPattern, fileName, ignoreCase: false))
                    results.Add(key);
            }
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateDirectories(string relativeDirectory)
    {
        string normalizedDir = StreamingSavePaths.NormalizeRelativeDirectoryPath(relativeDirectory);
        string prefix = normalizedDir.Length == 0 ? string.Empty : normalizedDir + "/";

        var directories = new HashSet<string>(StringComparer.Ordinal);
        lock (_gate)
        {
            foreach (string key in _files.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                ReadOnlySpan<char> remainder = key.AsSpan(prefix.Length);
                int slashIndex = remainder.IndexOf('/');
                if (slashIndex > 0)
                    directories.Add(string.Concat(prefix, remainder[..slashIndex]));
            }
        }

        var results = new List<string>(directories);
        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <inheritdoc/>
    public bool DeleteFile(string relativePath)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        lock (_gate)
        {
            return _files.Remove(normalized);
        }
    }

    /// <inheritdoc/>
    public void DeleteDirectoryIfEmpty(string relativeDirectory)
    {
        _ = StreamingSavePaths.NormalizeRelativeDirectoryPath(relativeDirectory);
    }

    /// <summary>
    /// Standalone writable in-memory <see cref="IRandomAccessHandle"/> for archive export and testing.
    /// </summary>
    public sealed class MemoryBufferHandle : IRandomAccessHandle
    {
        private readonly MemoryFile _file = new();
        private volatile bool _disposed;

        /// <inheritdoc/>
        public long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _file.Length;
            }
        }

        /// <inheritdoc/>
        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            return _file.ReadAt(offset, destination);
        }

        /// <inheritdoc/>
        public void WriteAt(long offset, ReadOnlySpan<byte> source)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            _file.WriteAt(offset, source);
        }

        /// <inheritdoc/>
        public void FlushToDisk()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// Copies the current contents of this handle into a new byte array.
        /// </summary>
        /// <returns>A copy of the written bytes.</returns>
        public byte[] ToArray() => _file.ToArray();

        /// <summary>
        /// Creates a new independent read-only handle over the underlying buffer.
        /// </summary>
        /// <returns>A read-only handle sharing the same buffer.</returns>
        public IRandomAccessHandle CreateReadHandle() => new MemoryRandomAccessHandle(_file, writable: false);

        /// <inheritdoc/>
        public void Dispose() => _disposed = true;
    }

    private sealed class MemoryFile
    {
        private readonly ReaderWriterLockSlim _lock = new();
        private byte[] _buffer;
        private long _length;

        public MemoryFile()
        {
            _buffer = [];
            _length = 0;
        }

        public MemoryFile(ReadOnlySpan<byte> initialData)
        {
            _buffer = initialData.IsEmpty ? [] : initialData.ToArray();
            _length = initialData.Length;
        }

        public long Length
        {
            get
            {
                _lock.EnterReadLock();
                try
                {
                    return _length;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        public int ReadAt(long offset, Span<byte> destination)
        {
            if (destination.IsEmpty)
                return 0;

            _lock.EnterReadLock();
            try
            {
                if (offset >= _length)
                    return 0;

                int available = (int)Math.Min(destination.Length, _length - offset);
                _buffer.AsSpan((int)offset, available).CopyTo(destination);
                return available;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void WriteAt(long offset, ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty && offset <= _length)
                return;

            _lock.EnterWriteLock();
            try
            {
                long end = checked(offset + source.Length);
                if (end > Array.MaxLength)
                    throw new IOException("In-memory file exceeds maximum array size.");

                int requiredCapacity = (int)end;
                if (requiredCapacity > _buffer.Length)
                {
                    int newCapacity = _buffer.Length == 0 ? 4096 : _buffer.Length;
                    while (newCapacity < requiredCapacity)
                    {
                        newCapacity = newCapacity <= Array.MaxLength / 2 ? newCapacity * 2 : Array.MaxLength;
                    }

                    Array.Resize(ref _buffer, newCapacity);
                }

                if (offset > _length)
                    _buffer.AsSpan((int)_length, (int)(offset - _length)).Clear();

                if (!source.IsEmpty)
                    source.CopyTo(_buffer.AsSpan((int)offset, source.Length));

                if (end > _length)
                    _length = end;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public byte[] ToArray()
        {
            _lock.EnterReadLock();
            try
            {
                return _length == 0 ? [] : _buffer.AsSpan(0, (int)_length).ToArray();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    private sealed class MemoryRandomAccessHandle : IRandomAccessHandle
    {
        private readonly MemoryFile _file;
        private readonly bool _writable;
        private volatile bool _disposed;

        public MemoryRandomAccessHandle(MemoryFile file, bool writable)
        {
            _file = file;
            _writable = writable;
        }

        public long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _file.Length;
            }
        }

        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            return _file.ReadAt(offset, destination);
        }

        public void WriteAt(long offset, ReadOnlySpan<byte> source)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_writable)
                throw new NotSupportedException("Handle is read-only.");

            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            _file.WriteAt(offset, source);
        }

        public void FlushToDisk()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}

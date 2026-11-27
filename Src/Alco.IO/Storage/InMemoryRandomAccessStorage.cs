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
    private readonly Dictionary<string, InMemoryFile> _files = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool FileExists(string relativePath)
    {
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        lock (_gate)
        {
            return _files.ContainsKey(normalized);
        }
    }

    /// <inheritdoc/>
    public bool TryOpenRead(string relativePath, [NotNullWhen(true)] out IRandomAccessHandle? handle)
    {
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        InMemoryFile? file;
        lock (_gate)
        {
            if (!_files.TryGetValue(normalized, out file))
            {
                handle = null;
                return false;
            }
        }

        handle = new InMemoryRandomAccessHandle(file, writable: false);
        return true;
    }

    /// <inheritdoc/>
    public IRandomAccessHandle OpenAppendOrCreate(string relativePath)
    {
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        InMemoryFile? file;
        lock (_gate)
        {
            if (!_files.TryGetValue(normalized, out file))
            {
                file = new InMemoryFile();
                _files.Add(normalized, file);
            }
        }

        return new InMemoryRandomAccessHandle(file, writable: true);
    }

    /// <inheritdoc/>
    public void WriteAtomic(string relativePath, ReadOnlySpan<byte> data, bool flushToDisk = true)
    {
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        var replacement = new InMemoryFile(data);
        lock (_gate)
        {
            _files[normalized] = replacement;
        }
    }

    /// <inheritdoc/>
    public bool TryReadAllBytes(string relativePath, [NotNullWhen(true)] out byte[]? data)
    {
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        InMemoryFile? file;
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
        string normalizedDir = RelativePathUtility.NormalizeRelativeDirectoryPath(relativeDirectory);
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
        string normalizedDir = RelativePathUtility.NormalizeRelativeDirectoryPath(relativeDirectory);
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
        string normalized = RelativePathUtility.NormalizeRelativeFilePath(relativePath);
        lock (_gate)
        {
            return _files.Remove(normalized);
        }
    }

    /// <inheritdoc/>
    public void DeleteDirectoryIfEmpty(string relativeDirectory)
    {
        _ = RelativePathUtility.NormalizeRelativeDirectoryPath(relativeDirectory);
    }
}

internal sealed class InMemoryFile
{
    private readonly ReaderWriterLockSlim _lock = new();
    private byte[] _buffer;
    private long _length;

    public InMemoryFile()
    {
        _buffer = [];
        _length = 0;
    }

    public InMemoryFile(ReadOnlySpan<byte> initialData)
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

internal class InMemoryRandomAccessHandle(InMemoryFile file, bool writable) : IRandomAccessHandle
{
    private volatile bool _disposed;

    protected InMemoryFile File => file;

    public long Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return file.Length;
        }
    }

    public int ReadAt(long offset, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return file.ReadAt(offset, destination);
    }

    public void WriteAt(long offset, ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!writable)
            throw new NotSupportedException("Handle is read-only.");

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        file.WriteAt(offset, source);
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

internal sealed class InMemoryBufferHandle : InMemoryRandomAccessHandle
{
    private InMemoryBufferHandle(InMemoryFile file, bool writable) : base(file, writable)
    {
    }

    public InMemoryBufferHandle() : base(new InMemoryFile(), writable: true)
    {
    }

    public static IRandomAccessHandle CreateReadOnly(ReadOnlySpan<byte> data) =>
        new InMemoryBufferHandle(new InMemoryFile(data), writable: false);

    public byte[] ToArray() => File.ToArray();

    public IRandomAccessHandle CreateReadHandle() => new InMemoryRandomAccessHandle(File, writable: false);
}

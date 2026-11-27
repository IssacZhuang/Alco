using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Microsoft.Win32.SafeHandles;

namespace Alco.IO;

/// <summary>
/// Disk-backed <see cref="IRandomAccessStorage"/> implementation using <see cref="SafeFileHandle"/> and <see cref="RandomAccess"/>.
/// </summary>
public sealed class DirectoryRandomAccessStorage : IRandomAccessStorage
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private readonly string _rootDirectory;
    private readonly string _rootPrefix;

    /// <summary>
    /// Gets the normalized full path to the storage root directory.
    /// </summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Initializes a new directory-backed storage rooted at <paramref name="rootDirectory"/>.
    /// </summary>
    /// <param name="rootDirectory">The directory path on disk; created if it does not exist.</param>
    public DirectoryRandomAccessStorage(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
        _rootPrefix = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Attempts to open an arbitrary external file path on disk for concurrent random-access reading.
    /// </summary>
    /// <param name="filePath">The external file path.</param>
    /// <param name="handle">The opened handle when successful.</param>
    /// <returns><see langword="true"/> if the file existed and was opened; otherwise, <see langword="false"/>.</returns>
    public static bool TryOpenExternalRead(string filePath, [NotNullWhen(true)] out IRandomAccessHandle? handle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            handle = null;
            return false;
        }

        try
        {
            SafeFileHandle fileHandle = File.OpenHandle(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess);
            handle = new SafeFileRandomAccessHandle(fileHandle, writable: false);
            return true;
        }
        catch (FileNotFoundException)
        {
            handle = null;
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            handle = null;
            return false;
        }
    }

    /// <summary>
    /// Creates or replaces a temporary staging file for atomic external writes, returning its handle and temp path.
    /// </summary>
    /// <param name="tempFilePath">The destination temporary file path to create.</param>
    /// <returns>A writable random-access handle for the new temporary file.</returns>
    public static IRandomAccessHandle CreateExternalTempWrite(string tempFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFilePath);
        string fullPath = Path.GetFullPath(tempFilePath);
        string? parentDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        SafeFileHandle fileHandle = File.OpenHandle(
            fullPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            FileOptions.None);
        return new SafeFileRandomAccessHandle(fileHandle, writable: true);
    }

    /// <inheritdoc/>
    public bool FileExists(string relativePath)
    {
        string fullPath = ResolveFilePath(relativePath);
        return File.Exists(fullPath);
    }

    /// <inheritdoc/>
    public bool TryOpenRead(string relativePath, [NotNullWhen(true)] out IRandomAccessHandle? handle)
    {
        string fullPath = ResolveFilePath(relativePath);
        return TryOpenExternalRead(fullPath, out handle);
    }

    /// <inheritdoc/>
    public IRandomAccessHandle OpenAppendOrCreate(string relativePath)
    {
        string fullPath = ResolveFilePath(relativePath);
        string? parentDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        SafeFileHandle fileHandle = File.OpenHandle(
            fullPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.RandomAccess);
        return new SafeFileRandomAccessHandle(fileHandle, writable: true);
    }

    /// <inheritdoc/>
    public void WriteAtomic(string relativePath, ReadOnlySpan<byte> data, bool flushToDisk = true)
    {
        string fullPath = ResolveFilePath(relativePath);
        string? parentDir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        string tempPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (SafeFileHandle tempHandle = File.OpenHandle(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       FileOptions.None))
            {
                if (!data.IsEmpty)
                    RandomAccess.Write(tempHandle, data, 0);

                if (flushToDisk)
                    RandomAccess.FlushToDisk(tempHandle);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Preserve the original write failure.
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public bool TryReadAllBytes(string relativePath, [NotNullWhen(true)] out byte[]? data)
    {
        if (!TryOpenRead(relativePath, out IRandomAccessHandle? handle))
        {
            data = null;
            return false;
        }

        using (handle)
        {
            long length = handle.Length;
            if (length > Array.MaxLength)
                throw new IOException($"File '{relativePath}' exceeds maximum array length.");

            int byteLength = (int)length;
            if (byteLength == 0)
            {
                data = [];
                return true;
            }

            byte[] buffer = new byte[byteLength];
            int totalRead = handle.ReadAt(0, buffer);
            data = totalRead == byteLength ? buffer : buffer[..totalRead];
            return true;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateFiles(string relativeDirectory, string searchPattern, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        string dirPath = ResolveDirectoryPath(relativeDirectory);
        if (!Directory.Exists(dirPath))
            return [];

        SearchOption option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var results = new List<string>();
        foreach (string fullFilePath in Directory.EnumerateFiles(dirPath, searchPattern, option))
        {
            results.Add(Path.GetRelativePath(_rootDirectory, fullFilePath).Replace('\\', '/'));
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> EnumerateDirectories(string relativeDirectory)
    {
        string dirPath = ResolveDirectoryPath(relativeDirectory);
        if (!Directory.Exists(dirPath))
            return [];

        var results = new List<string>();
        foreach (string fullSubDir in Directory.EnumerateDirectories(dirPath, "*", SearchOption.TopDirectoryOnly))
        {
            results.Add(Path.GetRelativePath(_rootDirectory, fullSubDir).Replace('\\', '/'));
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    /// <inheritdoc/>
    public bool DeleteFile(string relativePath)
    {
        string fullPath = ResolveFilePath(relativePath);
        if (!File.Exists(fullPath))
            return false;

        try
        {
            File.Delete(fullPath);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public void DeleteDirectoryIfEmpty(string relativeDirectory)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeDirectoryPath(relativeDirectory);
        if (normalized.Length == 0)
            return;

        string dirPath = ResolveNormalizedDirectoryPath(normalized);
        if (!Directory.Exists(dirPath))
            return;

        try
        {
            if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
                Directory.Delete(dirPath, recursive: false);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
    }

    private string ResolveFilePath(string relativePath)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeFilePath(relativePath);
        string combined = Path.GetFullPath(Path.Combine(_rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_rootPrefix, PathComparison))
            throw new ArgumentException($"Resolved path escapes root directory: '{relativePath}'.", nameof(relativePath));

        return combined;
    }

    private string ResolveDirectoryPath(string relativeDirectory)
    {
        string normalized = StreamingSavePaths.NormalizeRelativeDirectoryPath(relativeDirectory);
        return ResolveNormalizedDirectoryPath(normalized);
    }

    private string ResolveNormalizedDirectoryPath(string normalized)
    {
        if (normalized.Length == 0)
            return _rootDirectory;

        string combined = Path.GetFullPath(Path.Combine(_rootDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(_rootPrefix, PathComparison))
            throw new ArgumentException($"Resolved directory escapes root directory: '{normalized}'.", nameof(normalized));

        return combined;
    }

    private sealed class SafeFileRandomAccessHandle : IRandomAccessHandle
    {
        private readonly SafeFileHandle _handle;
        private readonly bool _writable;
        private volatile bool _disposed;

        public SafeFileRandomAccessHandle(SafeFileHandle handle, bool writable)
        {
            _handle = handle;
            _writable = writable;
        }

        public long Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return RandomAccess.GetLength(_handle);
            }
        }

        public int ReadAt(long offset, Span<byte> destination)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (destination.IsEmpty)
                return 0;

            int totalRead = 0;
            while (totalRead < destination.Length)
            {
                int read = RandomAccess.Read(_handle, destination[totalRead..], offset + totalRead);
                if (read == 0)
                    break;

                totalRead += read;
            }

            return totalRead;
        }

        public void WriteAt(long offset, ReadOnlySpan<byte> source)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_writable)
                throw new NotSupportedException("Handle is read-only.");

            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            if (source.IsEmpty)
                return;

            RandomAccess.Write(_handle, source, offset);
        }

        public void FlushToDisk()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_writable)
                return;

            RandomAccess.FlushToDisk(_handle);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _handle.Dispose();
        }
    }
}
